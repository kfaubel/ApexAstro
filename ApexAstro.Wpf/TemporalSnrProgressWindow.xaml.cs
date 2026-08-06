using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using ApexAstro.Core;

namespace ApexAstro.Wpf
{
    public partial class TemporalSnrProgressWindow : Window, INotifyPropertyChanged
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly TimeSpan _estimatedTotal;
        private readonly DispatcherTimer _remainingTimer;
        private bool _completed;
        private bool _cancelRequested;
        private double _totalProgressValue;
        private double _filterProgressValue;
        private string _statusText = "Preparing temporal SNR computation...";
        private string _resultText = string.Empty;
        private string _percentText = "0% overall";
        private string _filterPercentText = "0% filter";
        private string _currentFilterText = "Current filter: --";
        private string _etaText = "Time remaining: --:--";

        public TemporalSnrProgressWindow(TimeSpan estimatedTotal)
        {
            _estimatedTotal = estimatedTotal;
            InitializeComponent();
            DataContext = this;

            Steps = new ObservableCollection<TemporalSnrProgressStep>
            {
                new("Validate and group frames"),
                new("Align frames by filter"),
                new("Compute pixel-wise temporal SNR"),
                new("Save cache and validation data")
            };

            FilterSummaries = new ObservableCollection<TemporalSnrFilterSummary>();

            _remainingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _remainingTimer.Tick += (_, _) => UpdateRemainingTime();
            _remainingTimer.Start();

            UpdateRemainingTime();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? CancelRequestedAction;
        public ObservableCollection<TemporalSnrProgressStep> Steps { get; }
        public ObservableCollection<TemporalSnrFilterSummary> FilterSummaries { get; }
        public bool CancelRequested => _cancelRequested;
        public bool HasFilterSummaries => FilterSummaries.Count > 0;

        public double TotalProgressValue
        {
            get => _totalProgressValue;
            private set
            {
                if (Math.Abs(_totalProgressValue - value) > 0.01)
                {
                    _totalProgressValue = value;
                    OnPropertyChanged();
                    UpdateRemainingTime();
                }
            }
        }

        public double FilterProgressValue
        {
            get => _filterProgressValue;
            private set
            {
                if (Math.Abs(_filterProgressValue - value) > 0.01)
                {
                    _filterProgressValue = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ResultText
        {
            get => _resultText;
            private set
            {
                if (_resultText != value)
                {
                    _resultText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasResultText));
                }
            }
        }

        public bool HasResultText => !string.IsNullOrWhiteSpace(_resultText);

        public string CurrentFilterText
        {
            get => _currentFilterText;
            private set
            {
                if (_currentFilterText != value)
                {
                    _currentFilterText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string PercentText
        {
            get => _percentText;
            private set
            {
                if (_percentText != value)
                {
                    _percentText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FilterPercentText
        {
            get => _filterPercentText;
            private set
            {
                if (_filterPercentText != value)
                {
                    _filterPercentText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string EtaText
        {
            get => _etaText;
            private set
            {
                if (_etaText != value)
                {
                    _etaText = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan Elapsed => _stopwatch.Elapsed;

        public void UpdateProgress(TemporalSnrProgressInfo info)
        {
            if (info.StageIndex > 0 && info.StageIndex <= Steps.Count)
            {
                for (int i = 0; i < info.StageIndex - 1; i++)
                {
                    Steps[i].IsCompleted = true;
                }
            }

            StatusText = info.Message;

            bool isComputeStage = info.Stage.Equals("Compute", StringComparison.OrdinalIgnoreCase);
            double overallPercent = info.OverallPercentComplete.HasValue
                ? Math.Clamp(info.OverallPercentComplete.Value * 100.0, 0.0, 100.0)
                : Math.Clamp(info.PercentComplete * 100.0, 0.0, 100.0);
            TotalProgressValue = Math.Max(TotalProgressValue, overallPercent);
            PercentText = $"{TotalProgressValue:0}% overall";

            if (isComputeStage)
            {
                double filterPercent = info.CurrentFilterPercentComplete.HasValue
                    ? info.CurrentFilterPercentComplete.Value
                    : info.PercentComplete;
                FilterProgressValue = Math.Clamp(filterPercent * 100.0, 0.0, 100.0);
                CurrentFilterText = string.IsNullOrWhiteSpace(info.FilterName)
                    ? "Current filter: computing"
                    : $"Current filter: {info.FilterName}";
            }
            else if (info.Stage.Equals("Filter", StringComparison.OrdinalIgnoreCase))
            {
                FilterProgressValue = 0.0;
                CurrentFilterText = string.IsNullOrWhiteSpace(info.FilterName)
                    ? "Current filter: preparing"
                    : $"Current filter: {info.FilterName}";
            }
            else if (info.Stage.Equals("Save", StringComparison.OrdinalIgnoreCase))
            {
                FilterProgressValue = 100.0;
                CurrentFilterText = "Current filter: complete";
            }
            else
            {
                FilterProgressValue = 0.0;
                CurrentFilterText = "Current filter: --";
            }

            FilterPercentText = $"{FilterProgressValue:0}% filter";
            UpdateRemainingTime();
        }

        public void MarkAllComplete(string finalMessage, string? resultText = null, IEnumerable<TemporalSnrFilterSummary>? filterSummaries = null)
        {
            foreach (var step in Steps)
            {
                step.IsCompleted = true;
            }

            _completed = true;
            _remainingTimer.Stop();
            StatusText = finalMessage;
            ResultText = resultText ?? string.Empty;
            FilterSummaries.Clear();
            if (filterSummaries != null)
            {
                foreach (var summary in filterSummaries)
                {
                    FilterSummaries.Add(summary);
                }
            }
            OnPropertyChanged(nameof(HasFilterSummaries));
            TotalProgressValue = 100;
            FilterProgressValue = 100;
            PercentText = "100% overall";
            FilterPercentText = "100% filter";
            CurrentFilterText = "Current filter: complete";
            EtaText = $"Completed in {FormatElapsed(_stopwatch.Elapsed)}";
            CancelButton.Visibility = Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Visible;
            CloseButton.Focus();
        }

        public void MarkCancelled(string message)
        {
            _completed = true;
            _remainingTimer.Stop();
            StatusText = message;
            ResultText = string.Empty;
            FilterPercentText = $"{FilterProgressValue:0}% filter";
            EtaText = $"Canceled after {FormatElapsed(_stopwatch.Elapsed)}";
            CancelButton.Visibility = Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Visible;
            CloseButton.Focus();
        }

        private void UpdateRemainingTime()
        {
            if (_completed)
                return;

            double progressRatio = Math.Clamp(_totalProgressValue / 100.0, 0.0, 1.0);
            if (progressRatio <= 0.001)
            {
                EtaText = $"Time remaining: {FormatElapsed(_estimatedTotal)}";
                return;
            }

            TimeSpan elapsed = _stopwatch.Elapsed;
            double expectedTotalSeconds = Math.Max(_estimatedTotal.TotalSeconds, elapsed.TotalSeconds / progressRatio);
            double remainingSeconds = Math.Max(0.0, expectedTotalSeconds - elapsed.TotalSeconds);
            var remaining = TimeSpan.FromSeconds(remainingSeconds);
            EtaText = $"Time remaining: {FormatElapsed(remaining)}";
        }

        private static string FormatElapsed(TimeSpan span)
        {
            return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cancelRequested = true;
            StatusText = "Cancel requested. Finishing current work...";
            CancelButton.IsEnabled = false;
            CancelRequestedAction?.Invoke();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _remainingTimer.Stop();
            Close();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class TemporalSnrProgressStep : INotifyPropertyChanged
    {
        private bool _isCompleted;

        public TemporalSnrProgressStep(string displayText)
        {
            DisplayText = displayText;
        }

        public string DisplayText { get; }

        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (_isCompleted != value)
                {
                    _isCompleted = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCompleted)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed class TemporalSnrFilterSummary
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
