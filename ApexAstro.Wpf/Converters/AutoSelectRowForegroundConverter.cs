using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ApexAstro.Wpf.Models;

namespace ApexAstro.Wpf.Converters
{
    public sealed class AutoSelectKeywordForegroundContext
    {
        public string Keyword { get; init; } = string.Empty;
        public IEnumerable<AutoSelectCriteriaSetting> Criteria { get; init; } = Enumerable.Empty<AutoSelectCriteriaSetting>();
    }

    public class AutoSelectRowForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not FileItem fileItem)
            {
                return GetGreenBrush();
            }

            if (parameter is AutoSelectKeywordForegroundContext keywordContext &&
                !string.IsNullOrWhiteSpace(keywordContext.Keyword))
            {
                return IsKeywordOutOfRange(fileItem, keywordContext)
                    ? System.Windows.Media.Brushes.Red
                    : GetGreenBrush();
            }

            var criteria = parameter as IEnumerable<AutoSelectCriteriaSetting> ?? Enumerable.Empty<AutoSelectCriteriaSetting>();
            return WouldBeSelected(fileItem, criteria)
                ? System.Windows.Media.Brushes.Red
                : GetGreenBrush();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static bool WouldBeSelected(FileItem fileItem, IEnumerable<AutoSelectCriteriaSetting> criteria)
        {
            foreach (var rule in criteria.Where(r => r != null && r.IsEnabled && !string.IsNullOrWhiteSpace(r.Key)))
            {
                string? currentValue = GetValueForRule(fileItem, rule.Key);
                if (!IsValueAcceptable(rule, currentValue))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsKeywordOutOfRange(FileItem fileItem, AutoSelectKeywordForegroundContext context)
        {
            var matchingRule = context.Criteria.FirstOrDefault(r =>
                r != null &&
                r.IsEnabled &&
                !string.IsNullOrWhiteSpace(r.Key) &&
                r.Key.Equals(context.Keyword, StringComparison.OrdinalIgnoreCase));

            if (matchingRule is null)
            {
                return false;
            }

            string? currentValue = GetValueForRule(fileItem, context.Keyword);
            return !IsValueAcceptable(matchingRule, currentValue);
        }

        private static string? GetValueForRule(FileItem fileItem, string key)
        {
            if (string.Equals(key, "Median", StringComparison.OrdinalIgnoreCase))
            {
                return fileItem.Median.HasValue
                    ? fileItem.Median.Value.ToString("G", CultureInfo.InvariantCulture)
                    : null;
            }

            if (fileItem.CustomKeywords.TryGetValue(key, out var customValue))
            {
                return customValue;
            }

            if (fileItem.FitsKeywords.TryGetValue(key, out var fitsValue))
            {
                return fitsValue;
            }

            if (fileItem.CsvKeywords.TryGetValue(key, out var csvValue))
            {
                return csvValue;
            }

            return null;
        }

        private static bool IsValueAcceptable(AutoSelectCriteriaSetting rule, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Equals("NaN", StringComparison.OrdinalIgnoreCase))
            {
                return rule.AllowBlank;
            }

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericValue))
            {
                if (double.IsNaN(numericValue))
                {
                    return rule.AllowBlank;
                }

                if (!string.IsNullOrWhiteSpace(rule.MinValue) &&
                    double.TryParse(rule.MinValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var minValue) &&
                    numericValue < minValue)
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(rule.MaxValue) &&
                    double.TryParse(rule.MaxValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxValue) &&
                    numericValue > maxValue)
                {
                    return false;
                }

                return true;
            }

            if (!string.IsNullOrWhiteSpace(rule.AllowedValues))
            {
                var allowed = rule.AllowedValues
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                return allowed.Any(v => v.Equals(value, StringComparison.OrdinalIgnoreCase));
            }

            return true;
        }

        private static System.Windows.Media.Brush GetGreenBrush()
        {
            if (System.Windows.Application.Current is null)
            {
                return System.Windows.Media.Brushes.Green;
            }

            return System.Windows.Application.Current.TryFindResource("ThemeAccentGreen") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Green;
        }
    }
}
