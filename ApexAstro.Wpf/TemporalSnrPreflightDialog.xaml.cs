using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using ApexAstro.Wpf.Services;

namespace ApexAstro.Wpf
{
    public partial class TemporalSnrPreflightDialog : Window, INotifyPropertyChanged
    {
        private readonly TemporalSnrPreflightPlan _plan;
        private bool _useFullMachine;
        private string _keepBestPercentText = "80";
        private bool _saveIntermediateData;
        private bool _stopOnMismatch;

        public TemporalSnrPreflightDialog(TemporalSnrPreflightPlan plan, TemporalSnrCacheManifest? currentCacheManifest = null)
        {
            _plan = plan;
            InitializeComponent();
            DataContext = this;

            EstimateText = $"Estimated {FormatSpan(plan.EstimatedMinimumDuration)} to {FormatSpan(plan.EstimatedMaximumDuration)} on this machine.\nTarget duration: about {FormatSpan(plan.EstimatedDuration)}.";
            HardwareText = $"{plan.Hardware.CpuName} • {plan.Hardware.PhysicalCores} cores / {plan.Hardware.LogicalProcessors} threads • {FormatRam(plan.Hardware.TotalRamBytes)} RAM";

            CurrentResults = new ObservableCollection<TemporalSnrPreflightResultRow>();

            if (currentCacheManifest != null)
            {
                HasCurrentResults = true;
                CurrentResultsSummaryText = $"Current SNR results are up to date (computed {currentCacheManifest.ComputedUtc.ToLocalTime():g}).";

                foreach (var filter in currentCacheManifest.Filters.OrderBy(f => f.FilterName, StringComparer.OrdinalIgnoreCase))
                {
                    CurrentResults.Add(new TemporalSnrPreflightResultRow
                    {
                        FilterName = filter.FilterName,
                        MedianTemporalSnr = filter.MedianTemporalSnr,
                        MeanTemporalSnr = filter.MeanTemporalSnr,
                        StandardDeviationTemporalSnr = filter.StandardDeviationTemporalSnr,
                        FrameCount = filter.FrameCount
                    });
                }
            }
            else
            {
                HasCurrentResults = false;
                CurrentResultsSummaryText = "No current SNR cache found for this folder. A new compute is needed to populate results.";
            }

            ComputeStepsText = "Steps to compute SNR:\n1. Validate and group frames by filter\n2. Align frames per filter\n3. Compute pixel-wise temporal SNR\n4. Save cache and validation data";

            UseFullMachine = plan.UseFullMachineDefault;
            KeepBestPercentText = "80";
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
        public ObservableCollection<TemporalSnrPreflightResultRow> CurrentResults { get; }

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

        public string KeepBestPercentText
        {
            get => _keepBestPercentText;
            set
            {
                if (_keepBestPercentText != value)
                {
                    _keepBestPercentText = value;
                    OnPropertyChanged();
                }
            }
        }

        public double KeepBestPercent { get; private set; } = 80.0;

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
            if (!double.TryParse(KeepBestPercentText, out double keepBestPercent) || keepBestPercent <= 0 || keepBestPercent > 100)
            {
                System.Windows.MessageBox.Show(
                    "Keep best % must be a number between 0 and 100.",
                    "Invalid Keep Best %",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            KeepBestPercent = keepBestPercent;
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
    }

    public sealed class TemporalSnrPreflightResultRow
    {
        public string FilterName { get; init; } = string.Empty;
        public double MedianTemporalSnr { get; init; }
        public double MeanTemporalSnr { get; init; }
        public double StandardDeviationTemporalSnr { get; init; }
        public int FrameCount { get; init; }

        public double CoefficientOfVariation => MeanTemporalSnr > 1e-9
            ? StandardDeviationTemporalSnr / MeanTemporalSnr
            : 0.0;

        public string CvDisplay => $"{CoefficientOfVariation:P1}";

        public string CvGrade => CoefficientOfVariation switch
        {
            < 0.15 => "Green",
            < 0.30 => "Yellow",
            _ => "Red"
        };

        public System.Windows.Media.Brush CvGradeBrush => CvGrade switch
        {
            "Green" => System.Windows.Media.Brushes.ForestGreen,
            "Yellow" => System.Windows.Media.Brushes.Goldenrod,
            _ => System.Windows.Media.Brushes.IndianRed
        };

        public string TargetDataGuidance => CvGrade switch
        {
            "Green" => "On target",
            "Yellow" => "Add ~25-50%",
            _ => "Add ~75-100%"
        };
    }
}
