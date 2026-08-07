using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using ApexAstro.Core;
using ApexAstro.Wpf.Services;

namespace ApexAstro.Wpf
{
    public partial class TemporalSnrPreflightDialog : Window, INotifyPropertyChanged
    {
        private readonly TemporalSnrPreflightPlan _plan;
        private bool _useFullMachine;
        private bool _saveIntermediateData;
        private bool _stopOnMismatch;

        public TemporalSnrPreflightDialog(TemporalSnrPreflightPlan plan, TemporalSnrCacheManifest? currentCacheManifest = null, string? folderPath = null)
        {
            _plan = plan;
            InitializeComponent();
            DataContext = this;

            EstimateText = $"Estimated {FormatSpan(plan.EstimatedMinimumDuration)} to {FormatSpan(plan.EstimatedMaximumDuration)} on this machine.\nTarget duration: about {FormatSpan(plan.EstimatedDuration)}.";
            HardwareText = $"{plan.Hardware.CpuName} • {plan.Hardware.PhysicalCores} cores / {plan.Hardware.LogicalProcessors} threads • {FormatRam(plan.Hardware.TotalRamBytes)} RAM";

            CurrentResults = new ObservableCollection<TemporalSnrFilterSummary>();

            if (currentCacheManifest != null)
            {
                HasCurrentResults = true;
                CurrentResultsSummaryText = $"Current SNR results are up to date (computed {currentCacheManifest.ComputedUtc.ToLocalTime():g}).";
                string sourceFolder = !string.IsNullOrWhiteSpace(folderPath)
                    ? folderPath
                    : currentCacheManifest.FolderPath;

                foreach (var filter in currentCacheManifest.Filters.OrderBy(f => f.FilterName, StringComparer.OrdinalIgnoreCase))
                {
                    double? integrationMinutes = ComputeIntegrationMinutes(filter, sourceFolder);
                    CurrentResults.Add(new TemporalSnrFilterSummary
                    {
                        FilterName = filter.FilterName,
                        MedianTemporalSnr = filter.MedianTemporalSnr,
                        FrameCount = filter.FrameCount,
                        IntegrationMinutes = integrationMinutes,
                        AdditionalMinutesTo30 = ComputeAdditionalMinutesForTargetSnr(filter.MedianTemporalSnr, integrationMinutes, 30.0),
                        AdditionalMinutesTo60 = ComputeAdditionalMinutesForTargetSnr(filter.MedianTemporalSnr, integrationMinutes, 60.0),
                        AdditionalMinutesTo90 = ComputeAdditionalMinutesForTargetSnr(filter.MedianTemporalSnr, integrationMinutes, 90.0)
                    });
                }
            }
            else
            {
                HasCurrentResults = false;
                CurrentResultsSummaryText = "No current SNR cache found for this folder. A new compute is needed to populate results.";
            }

            ComputeStepsText = "Steps to compute SNR:\n1. Validate and group frames by filter\n2. Align frames per filter\n3. Compute SNR from sampled pixels (10,000 default)\n4. Save cache and validation data";

            UseFullMachine = plan.UseFullMachineDefault;
            SaveIntermediateData = plan.SaveIntermediateDataDefault;
            StopOnMismatch = plan.StopOnMismatchDefault;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string EstimateText { get; }
        public string HardwareText { get; }
        public string CurrentResultsSummaryText { get; }
        public string ComputeStepsText { get; }
        public bool HasCurrentResults { get; }
        public bool ShowComputeSteps => !HasCurrentResults;
        public ObservableCollection<TemporalSnrFilterSummary> CurrentResults { get; }

        public bool UseFullMachine
        {
            get => _useFullMachine;
            set
            {
                if (_useFullMachine != value)
                {
                    _useFullMachine = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool SaveIntermediateData
        {
            get => _saveIntermediateData;
            set
            {
                if (_saveIntermediateData != value)
                {
                    _saveIntermediateData = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool StopOnMismatch
        {
            get => _stopOnMismatch;
            set
            {
                if (_stopOnMismatch != value)
                {
                    _stopOnMismatch = value;
                    OnPropertyChanged();
                }
            }
        }

        public TemporalSnrPreflightPlan Plan => _plan;

        private void StartCompute_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private static string FormatSpan(TimeSpan span)
        {
            return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
        }

        private static string FormatRam(ulong bytes)
        {
            if (bytes == 0)
                return "unknown";

            return $"{bytes / 1024.0 / 1024.0 / 1024.0:0.#} GB";
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static double? ComputeIntegrationMinutes(TemporalSnrCacheFilterEntry filter, string folderPath)
        {
            if (filter.SourceFiles == null || filter.SourceFiles.Count == 0)
                return null;

            double totalSeconds = 0.0;
            int exposureCount = 0;

            foreach (var source in filter.SourceFiles)
            {
                string filePath = Path.Combine(folderPath, source.FileName);
                double? exposureSeconds = TryReadExposureSeconds(filePath, source.FileName);
                if (!exposureSeconds.HasValue || exposureSeconds.Value <= 0)
                    continue;

                totalSeconds += exposureSeconds.Value;
                exposureCount++;
            }

            if (exposureCount == 0)
                return null;

            return totalSeconds / 60.0;
        }

        private static double? TryReadExposureSeconds(string filePath, string fallbackFileName)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var headers = FitsParser.ParseHeaderFromFile(filePath);
                    if (TryGetExposureFromHeader(headers, out double headerExposureSeconds))
                        return headerExposureSeconds;
                }
            }
            catch
            {
                // Fall back to filename parsing.
            }

            return TryParseExposureFromFilename(fallbackFileName);
        }

        private static bool TryGetExposureFromHeader(Dictionary<string, object> headers, out double exposureSeconds)
        {
            exposureSeconds = 0.0;

            if (TryGetHeaderDouble(headers, "EXPTIME", out exposureSeconds))
                return true;

            if (TryGetHeaderDouble(headers, "EXPOSURE", out exposureSeconds))
                return true;

            return false;
        }

        private static bool TryGetHeaderDouble(Dictionary<string, object> headers, string key, out double value)
        {
            value = 0.0;
            if (!headers.TryGetValue(key, out var raw) || raw == null)
                return false;

            if (raw is double d)
            {
                value = d;
                return value > 0;
            }

            if (raw is float f)
            {
                value = f;
                return value > 0;
            }

            if (raw is int i)
            {
                value = i;
                return value > 0;
            }

            if (raw is long l)
            {
                value = l;
                return value > 0;
            }

            string text = raw.ToString() ?? string.Empty;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                value = parsed;
                return value > 0;
            }

            return false;
        }

        private static double? TryParseExposureFromFilename(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(nameWithoutExtension))
                return null;

            string[] tokens = nameWithoutExtension.Split('_', StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                if (!token.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                    continue;

                string numericPart = token[..^1];
                if (double.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                    && seconds > 0)
                {
                    return seconds;
                }
            }

            return null;
        }

        private static double? ComputeAdditionalMinutesForTargetSnr(double currentMedianSnr, double? integrationMinutes, double targetSnr)
        {
            if (!integrationMinutes.HasValue || !double.IsFinite(integrationMinutes.Value) || integrationMinutes.Value <= 0)
                return null;

            if (!double.IsFinite(currentMedianSnr) || currentMedianSnr <= 0)
                return null;

            if (currentMedianSnr >= targetSnr)
                return 0.0;

            double scale = targetSnr / currentMedianSnr;
            double requiredTotalMinutes = integrationMinutes.Value * scale * scale;
            double additionalMinutes = requiredTotalMinutes - integrationMinutes.Value;
            return Math.Max(0.0, additionalMinutes);
        }
    }
}
