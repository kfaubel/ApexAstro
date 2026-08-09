using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace ApexAstro.Wpf
{
    /// <summary>
    /// Dialog for auto-marking files for deletion based on metadata criteria.
    /// Files that do NOT meet the enabled criteria will be marked.
    /// </summary>
    public partial class AutoMarkDialog : Window
    {
        /// <summary>
        /// Event raised when Apply is clicked with the criteria
        /// </summary>
        public event EventHandler<List<AutoMarkCriteria>>? ApplyRequested;

        private List<AutoMarkCriteriaViewModel> _criteria;
        private readonly Dictionary<string, AutoSelectCriteriaSetting> _savedCriteriaByKey;

        /// <summary>
        /// Creates the dialog with the specified keywords to check
        /// </summary>
        /// <param name="customKeywords">Custom keywords extracted from filenames</param>
        /// <param name="fitsKeywords">FITS header keywords</param>
        public AutoMarkDialog(
            IEnumerable<string> customKeywords,
            IEnumerable<string> fitsKeywords,
            IEnumerable<AutoSelectCriteriaSetting>? savedCriteria = null)
        {
            InitializeComponent();

            _savedCriteriaByKey = (savedCriteria ?? Enumerable.Empty<AutoSelectCriteriaSetting>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Key))
                .GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

            _criteria = new List<AutoMarkCriteriaViewModel>();

            // Add Median as a special keyword at the top
            var medianVm = new AutoMarkCriteriaViewModel { Key = "Median", IsCustomKeyword = false };
            medianVm.IsEnabled = false;  // Default to unchecked
            medianVm.AllowBlank = true;
            medianVm.MinValue = "";
            medianVm.MaxValue = "";
            ApplySavedValues(medianVm);
            _criteria.Add(medianVm);

            // Add custom keywords
            foreach (var keyword in customKeywords)
            {
                var vm = new AutoMarkCriteriaViewModel { Key = keyword, IsCustomKeyword = true };
                SetDefaultValues(vm);
                ApplySavedValues(vm);
                _criteria.Add(vm);
            }

            // Add FITS keywords
            foreach (var keyword in fitsKeywords)
            {
                // Skip if already added as custom keyword
                if (_criteria.Any(c => c.Key.Equals(keyword, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var vm = new AutoMarkCriteriaViewModel { Key = keyword, IsCustomKeyword = false };
                SetDefaultValues(vm);
                ApplySavedValues(vm);
                _criteria.Add(vm);
            }

            // Bind to the UI
            KeywordsItemsControl.ItemsSource = _criteria;
        }

        /// <summary>
        /// Sets default values for known keywords
        /// </summary>
        private void SetDefaultValues(AutoMarkCriteriaViewModel vm)
        {
            switch (vm.Key.ToUpperInvariant())
            {
                case "RMS":
                    vm.IsEnabled = true;
                    vm.MinValue = "0.01";
                    vm.MaxValue = "1.2";
                    vm.AllowBlank = false;
                    break;
                case "STARS":
                    vm.IsEnabled = false;
                    vm.MinValue = "";
                    vm.MaxValue = "";
                    vm.AllowBlank = true;
                    break;
                case "ECC":
                    vm.IsEnabled = true;
                    vm.MinValue = "0";
                    vm.MaxValue = "0.65";
                    vm.AllowBlank = true;  // ECC can be NaN
                    break;
                case "HFR":
                    vm.IsEnabled = true;
                    vm.MinValue = "0";
                    vm.MaxValue = "2.0";
                    vm.AllowBlank = false;
                    break;
                case "FWHM":
                    vm.IsEnabled = true;
                    vm.MinValue = "";
                    vm.MaxValue = "3.0";
                    vm.AllowBlank = false;
                    break;
                case "FILTER":
                    vm.IsEnabled = false;  // Usually want to allow all filters
                    vm.AllowBlank = true;
                    vm.AllowedValues = "";
                    break;
                default:
                    vm.IsEnabled = false;
                    vm.AllowBlank = true;
                    break;
            }
        }

        private void ApplySavedValues(AutoMarkCriteriaViewModel vm)
        {
            if (!_savedCriteriaByKey.TryGetValue(vm.Key, out var saved))
            {
                return;
            }

            vm.IsEnabled = saved.IsEnabled;
            vm.MinValue = saved.MinValue ?? string.Empty;
            vm.MaxValue = saved.MaxValue ?? string.Empty;
            vm.AllowBlank = saved.AllowBlank;
            vm.AllowedValues = saved.AllowedValues ?? string.Empty;
        }

        public List<AutoSelectCriteriaSetting> GetCurrentSettings()
        {
            return _criteria
                .Select(c => new AutoSelectCriteriaSetting
                {
                    Key = c.Key,
                    IsEnabled = c.IsEnabled,
                    MinValue = c.MinValue ?? string.Empty,
                    MaxValue = c.MaxValue ?? string.Empty,
                    AllowBlank = c.AllowBlank,
                    AllowedValues = c.AllowedValues ?? string.Empty,
                })
                .ToList();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var enabledCriteria = new List<AutoMarkCriteria>();

                foreach (var vm in _criteria.Where(c => c.IsEnabled))
                {
                    var criteria = new AutoMarkCriteria
                    {
                        Key = vm.Key,
                        AllowBlank = vm.AllowBlank,
                        AllowedValues = ParseAllowedValues(vm.AllowedValues)
                    };

                    // Parse min value
                    if (!string.IsNullOrWhiteSpace(vm.MinValue))
                    {
                        if (double.TryParse(vm.MinValue, out var minVal))
                        {
                            criteria.MinValue = minVal;
                        }
                        else
                        {
                        System.Windows.MessageBox.Show($"Invalid minimum value for {vm.Key}: '{vm.MinValue}' is not a number.",
                            "Invalid Input", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                            return;
                        }
                    }

                    // Parse max value
                    if (!string.IsNullOrWhiteSpace(vm.MaxValue))
                    {
                        if (double.TryParse(vm.MaxValue, out var maxVal))
                        {
                            criteria.MaxValue = maxVal;
                        }
                        else
                        {
                        System.Windows.MessageBox.Show($"Invalid maximum value for {vm.Key}: '{vm.MaxValue}' is not a number.",
                            "Invalid Input", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                            return;
                        }
                    }

                    enabledCriteria.Add(criteria);
                }

                if (enabledCriteria.Count == 0)
                {
                System.Windows.MessageBox.Show("Please enable at least one criteria to apply.",
                    "No Criteria", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }

                // Raise the event with the criteria
                ApplyRequested?.Invoke(this, enabledCriteria);

                // Close the dialog
                this.DialogResult = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error processing criteria: {ex.Message}",
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Parses comma-separated allowed values into a list
        /// </summary>
        private List<string> ParseAllowedValues(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<string>();

            return input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(v => v.Trim())
                       .Where(v => !string.IsNullOrEmpty(v))
                       .ToList();
        }
    }

    /// <summary>
    /// Criteria for auto-marking files
    /// </summary>
    public class AutoMarkCriteria
    {
        public string Key { get; set; } = "";
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        public bool AllowBlank { get; set; } = true;
        public List<string> AllowedValues { get; set; } = new List<string>();

        /// <summary>
        /// Checks if a value passes this criteria (returns true if acceptable)
        /// </summary>
        public bool IsValueAcceptable(string? value)
        {
            // Handle blank/missing values
            if (string.IsNullOrWhiteSpace(value) || value == "NaN")
            {
                return AllowBlank;
            }

            // Try to parse as numeric
            if (double.TryParse(value, out var numericValue))
            {
                // Check NaN
                if (double.IsNaN(numericValue))
                {
                    return AllowBlank;
                }

                // Check min
                if (MinValue.HasValue && numericValue < MinValue.Value)
                {
                    return false;
                }

                // Check max
                if (MaxValue.HasValue && numericValue > MaxValue.Value)
                {
                    return false;
                }

                return true;
            }

            // Non-numeric value - check allowed values list
            if (AllowedValues.Count > 0)
            {
                // Check if value is in allowed list (case-insensitive)
                return AllowedValues.Any(av => av.Equals(value, StringComparison.OrdinalIgnoreCase));
            }

            // No allowed values specified for non-numeric - accept it
            return true;
        }
    }

    /// <summary>
    /// ViewModel for a criteria entry in the dialog
    /// </summary>
    public class AutoMarkCriteriaViewModel : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string _minValue = "";
        private string _maxValue = "";
        private bool _allowBlank = true;
        private string _allowedValues = "";

        public string Key { get; set; } = "";
        public bool IsCustomKeyword { get; set; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));
                }
            }
        }

        public string MinValue
        {
            get => _minValue;
            set
            {
                if (_minValue != value)
                {
                    _minValue = value;
                    OnPropertyChanged(nameof(MinValue));
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
                }
            }
        }

        public bool AllowBlank
        {
            get => _allowBlank;
            set
            {
                if (_allowBlank != value)
                {
                    _allowBlank = value;
                    OnPropertyChanged(nameof(AllowBlank));
                }
            }
        }

        public string AllowedValues
        {
            get => _allowedValues;
            set
            {
                if (_allowedValues != value)
                {
                    _allowedValues = value;
                    OnPropertyChanged(nameof(AllowedValues));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
