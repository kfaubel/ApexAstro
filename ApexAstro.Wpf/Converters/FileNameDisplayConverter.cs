using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace ApexAstro.Wpf.Converters
{
    /// <summary>
    /// Converts a file path into the display name used by the file list.
    /// ARW files always show the full file name; other files follow the user's full-name preference.
    /// </summary>
    public class FileNameDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string filePath || string.IsNullOrWhiteSpace(filePath))
                return string.Empty;

            bool showFullFilename = parameter is bool flag && flag;
            string extension = Path.GetExtension(filePath);

            if (extension.Equals(".arw", StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(filePath);

            return showFullFilename ? Path.GetFileName(filePath) : Path.GetFileNameWithoutExtension(filePath);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
