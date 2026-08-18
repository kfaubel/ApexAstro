using System;
using System.Collections.Generic;
using System.Windows;

namespace ApexAstro.Wpf
{
    /// <summary>
    /// Dialog for auto-selecting files based on custom keyword value ranges
    /// </summary>
    public partial class AutoSelectDialog : Window
    {
        /// <summary>
        /// Event raised when Apply is clicked with the selected criteria
        /// </summary>
        public event EventHandler<Dictionary<string, (double? min, double? max)>>? ApplyRequested;

        private Dictionary<string, KeywordRangeViewModel> _keywordRanges;

        public AutoSelectDialog(IEnumerable<string> customKeywords)
        {
            InitializeComponent();

            // Initialize keyword ranges from the provided keywords with defaults
            _keywordRanges = new Dictionary<string, KeywordRangeViewModel>();
            foreach (var keyword in customKeywords)
            {
                var vm = new KeywordRangeViewModel { Key = keyword };
                
                // Set default values for known keywords
                switch (keyword.ToUpperInvariant())
                {
                    case "RMS":
                        vm.MinValue = "0.01";
                        vm.MaxValue = "1.2";
                        break;
                    case "STARS":
                        vm.MinValue = "100";
                        vm.MaxValue = "";
                        break;
                    case "ECC":
                        vm.MinValue = "0.01";
                        vm.MaxValue = "0.6";
                        break;
                    case "HFR":
                        vm.MinValue = "0.01";
                        vm.MaxValue = "3.0";
                        break;
                    default:
                        vm.MinValue = "";
                        vm.MaxValue = "";
                        break;
                }
                
                _keywordRanges[keyword] = vm;
            }

            // Bind the keywords to the UI
            KeywordsItemsControl.ItemsSource = _keywordRanges.Values;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Convert text values to numeric ranges
                var numericRanges = new Dictionary<string, (double? min, double? max)>();

                foreach (var kvp in _keywordRanges)
                {
                    double? min = null;
                    double? max = null;

                    // Parse min value
                    if (!string.IsNullOrWhiteSpace(kvp.Value.MinValue))
                    {
                        if (double.TryParse(kvp.Value.MinValue, out var minVal))
                        {
                            min = minVal;
                        }
                        else
                        {
                            System.Windows.MessageBox.Show($"Invalid minimum value for {kvp.Key}: '{kvp.Value.MinValue}' is not a number.",
                                "Invalid Input", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                            return;
                        }
                    }

                    // Parse max value
                    if (!string.IsNullOrWhiteSpace(kvp.Value.MaxValue))
                    {
                        if (double.TryParse(kvp.Value.MaxValue, out var maxVal))
                        {
                            max = maxVal;
                        }
                        else
                        {
                            System.Windows.MessageBox.Show($"Invalid maximum value for {kvp.Key}: '{kvp.Value.MaxValue}' is not a number.",
                                "Invalid Input", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                            return;
                        }
                    }

                    // Only add if at least one range is specified
                    if (min.HasValue || max.HasValue)
                    {
                        numericRanges[kvp.Key] = (min, max);
                    }
                }

                // Raise the event with the numeric ranges
                ApplyRequested?.Invoke(this, numericRanges);

                // Close the dialog
                this.DialogResult = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error processing ranges: {ex.Message}", 
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// ViewModel for a keyword range entry in the dialog
    /// </summary>
    public class KeywordRangeViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private string _minValue = "";
        private string _maxValue = "";

        public string Key { get; set; } = "";
        
        public string MinValue
        {
            get => _minValue;
            set
            {
                if (_minValue != value)
                {
                    _minValue = value;
                    OnPropertyChanged(nameof(MinValue));
                    OnPropertyChanged(nameof(MinDisplayValue));
                }
            }
        }
        
        public string MaxValue
        {
            get => _maxValue;
            set
            {
                if (_maxValue != value)
                {
                    _maxValue = value;
                    OnPropertyChanged(nameof(MaxValue));
                    OnPropertyChanged(nameof(MaxDisplayValue));
                }
            }
        }

        /// <summary>
        /// Display value for Min - shows "(none)" if empty
        /// </summary>
        public string MinDisplayValue => string.IsNullOrWhiteSpace(_minValue) ? "(none)" : _minValue;

        /// <summary>
        /// Display value for Max - shows "(none)" if empty
        /// </summary>
        public string MaxDisplayValue => string.IsNullOrWhiteSpace(_maxValue) ? "(none)" : _maxValue;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
