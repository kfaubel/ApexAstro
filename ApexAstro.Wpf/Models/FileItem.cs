using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace ApexAstro.Wpf.Models
{
    public class FileItem : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private Dictionary<string, string> _customKeywords = new Dictionary<string, string>();
        private Dictionary<string, string> _fitsKeywords = new Dictionary<string, string>();
        private Dictionary<string, string> _csvKeywords = new Dictionary<string, string>();
        private Dictionary<string, string> _arwKeywords = new Dictionary<string, string>();
        private bool _isSelected = false;
        private double? _median;
        private double? _mean;
        private string _nameWithoutExtension = string.Empty;
        private string _filenameDate = string.Empty;
        private string _filenameTime = string.Empty;
        private string _FilenameFrame = string.Empty;
        private double? _perFrameSnr;
        private string _perFrameSnrGrade = string.Empty;
        private System.Windows.Media.Brush _perFrameSnrBrush = System.Windows.Media.Brushes.Transparent;
        private bool _metadataReady = true;

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    UpdateDerivedFilenameValues();
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        public string NameWithoutExtension
        {
            get => _nameWithoutExtension;
            private set
            {
                if (_nameWithoutExtension != value)
                {
                    _nameWithoutExtension = value;
                    OnPropertyChanged(nameof(NameWithoutExtension));
                }
            }
        }

        public string FilenameDate
        {
            get => _filenameDate;
            private set
            {
                if (_filenameDate != value)
                {
                    _filenameDate = value;
                    OnPropertyChanged(nameof(FilenameDate));
                }
            }
        }

        public string FilenameTime
        {
            get => _filenameTime;
            private set
            {
                if (_filenameTime != value)
                {
                    _filenameTime = value;
                    OnPropertyChanged(nameof(FilenameTime));
                }
            }
        }

        public string FilenameFrame
        {
            get => _FilenameFrame;
            private set
            {
                if (_FilenameFrame != value)
                {
                    _FilenameFrame = value;
                    OnPropertyChanged(nameof(FilenameFrame));
                }
            }
        }

        public long Size { get; set; }
        public double? Median 
        { 
            get => _median;
            set
            {
                if (_median != value)
                {
                    _median = value;
                    OnPropertyChanged(nameof(Median));
                }
            }
        }
        public double? Mean
        {
            get => _mean;
            set
            {
                if (_mean != value)
                {
                    _mean = value;
                    OnPropertyChanged(nameof(Mean));
                }
            }
        }
        public string Path { get; set; } = string.Empty;

        public double? PerFrameSnr
        {
            get => _perFrameSnr;
            set
            {
                if (_perFrameSnr != value)
                {
                    _perFrameSnr = value;
                    OnPropertyChanged(nameof(PerFrameSnr));
                    OnPropertyChanged(nameof(PerFrameSnrDisplay));
                }
            }
        }

        public string PerFrameSnrDisplay => PerFrameSnr.HasValue ? PerFrameSnr.Value.ToString("F2") : string.Empty;

        public string PerFrameSnrGrade
        {
            get => _perFrameSnrGrade;
            set
            {
                if (_perFrameSnrGrade != value)
                {
                    _perFrameSnrGrade = value;
                    OnPropertyChanged(nameof(PerFrameSnrGrade));
                }
            }
        }

        public System.Windows.Media.Brush PerFrameSnrBrush
        {
            get => _perFrameSnrBrush;
            set
            {
                if (_perFrameSnrBrush != value)
                {
                    _perFrameSnrBrush = value;
                    OnPropertyChanged(nameof(PerFrameSnrBrush));
                }
            }
        }
        
        public bool IsSelected 
        { 
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                    
                    // Log the mark/unmark action
                    try
                    {
                        var loggingService = App.LoggingService;
                        loggingService?.LogFileMarked(Name, value);
                    }
                    catch
                    {
                        // Silently fail if logging service is not available
                    }
                }
            }
        }
        
        public Dictionary<string, string> CustomKeywords 
        { 
            get => _customKeywords;
            set
            {
                _customKeywords = value;
                OnPropertyChanged(nameof(CustomKeywords));
                
                // Notify for each custom keyword property that might be bound
                foreach (var key in value.Keys)
                {
                    OnPropertyChanged(key);
                }
            }
        }

        public Dictionary<string, string> FitsKeywords 
        { 
            get => _fitsKeywords;
            set
            {
                _fitsKeywords = value;
                OnPropertyChanged(nameof(FitsKeywords));
                
                // Notify for each FITS keyword property that might be bound
                foreach (var key in value.Keys)
                {
                    OnPropertyChanged(key);
                }
            }
        }

        public Dictionary<string, string> CsvKeywords
        {
            get => _csvKeywords;
            set
            {
                _csvKeywords = value;
                OnPropertyChanged(nameof(CsvKeywords));

                foreach (var key in value.Keys)
                    OnPropertyChanged(key);
            }
        }

        public Dictionary<string, string> ArwKeywords
        {
            get => _arwKeywords;
            set
            {
                _arwKeywords = value;
                OnPropertyChanged(nameof(ArwKeywords));

                foreach (var key in value.Keys)
                    OnPropertyChanged(key);
            }
        }

        public bool MetadataReady
        {
            get => _metadataReady;
            set
            {
                if (_metadataReady != value)
                {
                    _metadataReady = value;
                    OnPropertyChanged(nameof(MetadataReady));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateDerivedFilenameValues()
        {
            NameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(_name) ?? string.Empty;
            FilenameDate = ApexAstro.Wpf.FilenameParser.ExtractDateToken(_name);
            FilenameTime = ApexAstro.Wpf.FilenameParser.ExtractTimeToken(_name);
            FilenameFrame = ApexAstro.Wpf.FilenameParser.ExtractFrameToken(_name);
        }
    }
}