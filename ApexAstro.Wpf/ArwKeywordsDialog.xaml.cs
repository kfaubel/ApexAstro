using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace ApexAstro.Wpf
{
    /// <summary>
    /// Dialog for selecting which ARW metadata fields to display in the file list.
    /// </summary>
    public partial class ArwKeywordsDialog : Window
    {
        public ObservableCollection<ArwKeywordItem> KeywordItems { get; } = new ObservableCollection<ArwKeywordItem>();

        private static readonly List<(string Keyword, string Description)> StandardArwKeywords = new()
        {
            ("Make", "Camera manufacturer"),
            ("Model", "Camera model"),
            ("LensModel", "Lens model"),
            ("FocalLength", "Focal length"),
            ("FNumber", "Lens aperture (f-number)"),
            ("ISO", "ISO sensitivity"),
            ("ExposureTime", "Exposure duration"),
            ("DateTimeOriginal", "Original capture date and time"),
            ("WhiteBalance", "White balance mode"),
            ("Orientation", "Image orientation"),
            ("ColorSpace", "Color space"),
            ("ExposureCompensation", "Exposure compensation value"),
            ("Width", "Image width in pixels"),
            ("Height", "Image height in pixels"),
            ("BitDepth", "Bits per sample"),
            ("BlackLevel", "Sensor black level if available"),
            ("WhiteLevel", "Sensor white level if available")
        };

        public ArwKeywordsDialog()
        {
            InitializeComponent();
            InitializeKeywords();
            KeywordCheckBoxes.ItemsSource = KeywordItems;
        }

        public ArwKeywordsDialog(IEnumerable<string> selectedKeywords) : this()
        {
            var selected = new HashSet<string>(selectedKeywords, System.StringComparer.OrdinalIgnoreCase);
            foreach (var item in KeywordItems)
            {
                item.IsSelected = selected.Contains(item.Keyword);
            }
        }

        private void InitializeKeywords()
        {
            foreach (var (keyword, description) in StandardArwKeywords)
            {
                KeywordItems.Add(new ArwKeywordItem(keyword, description));
            }
        }

        public List<string> GetSelectedKeywords()
        {
            return KeywordItems.Where(item => item.IsSelected).Select(item => item.Keyword).ToList();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in KeywordItems)
            {
                item.IsSelected = false;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class ArwKeywordItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Keyword { get; }
        public string Description { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public ArwKeywordItem(string keyword, string description)
        {
            Keyword = keyword;
            Description = description;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
