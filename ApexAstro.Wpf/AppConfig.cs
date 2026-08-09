using System.Text.Json;
using System.IO;

namespace ApexAstro.Wpf
{
    /// <summary>
    /// Theme mode enumeration for selecting light, dark, or auto theme
    /// </summary>
    public enum ThemeMode
    {
        /// <summary>
        /// Automatically detect theme from OS settings
        /// </summary>
        Auto = 0,
        
        /// <summary>
        /// Force light theme
        /// </summary>
        Light = 1,
        
        /// <summary>
        /// Force dark theme
        /// </summary>
        Dark = 2
    }

    /// <summary>
    /// Median display mode enumeration for choosing how to display median values
    /// </summary>
    public enum MedianDisplayMode
    {
        /// <summary>
        /// Display as normalized value (0.0-1.0)
        /// </summary>
        Normalized = 0,
        
        /// <summary>
        /// Display as 16-bit range (0-65535)
        /// </summary>
        SixteenBit = 1
    }

    /// <summary>
    /// Application configuration class that stores user preferences and settings.
    /// This class can be serialized to/from JSON to persist settings between application runs.
    /// Uses properties with automatic getters/setters for easy JSON serialization.
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// List of custom keywords to extract from filenames (e.g., "RMS", "HFR", "Stars").
        /// Auto-property with default value of empty list.
        /// </summary>
        public List<string> CustomKeywords { get; set; } = new List<string>();
        
        /// <summary>
        /// List of FITS header keywords to display as columns (e.g., "OBJECT", "EXPTIME", "FILTER").
        /// These are metadata fields within FITS files.
        /// </summary>
        public List<string> FitsKeywords { get; set; } = new List<string>();
        
        /// <summary>
        /// List of ImageMetaData.csv columns to display as columns (e.g., "HFR", "FWHM", "GuidingRMS").
        /// These are fields from the ImageMetaData.csv file in the current image folder.
        /// </summary>
        public List<string> CsvKeywords { get; set; } = new List<string>();
        
        /// <summary>
        /// Whether to show the file size column in the file list.
        /// Default is false to keep the interface cleaner.
        /// </summary>
        public bool ShowSizeColumn { get; set; } = false;

        /// <summary>
        /// Whether to show the full filename (with extension) in the File column.
        /// When false, the File column shows the filename without extension.
        /// </summary>
        public bool ShowFullFilename { get; set; } = true;

        /// <summary>
        /// Whether to show the date token parsed from the filename as a dedicated column.
        /// Example: 2026-07-08
        /// </summary>
        public bool ShowFilenameDateColumn { get; set; } = false;

        /// <summary>
        /// Whether to show the time token parsed from the filename as a dedicated column.
        /// Supports HH-mm-ss and HH:mm:ss token formats.
        /// </summary>
        public bool ShowFilenameTimeColumn { get; set; } = false;

        /// <summary>
        /// Whether to show the trailing frame number token parsed from the filename.
        /// Example: 0306
        /// </summary>
        public bool ShowFilenameFrameColumn { get; set; } = false;
        
        /// <summary>
        /// Whether to show the median column in the file list.
        /// Default is false.
        /// </summary>
        public bool ShowMedianColumn { get; set; } = false;
        
        /// <summary>
        /// Display mode for median values (Normalized or 16-bit range).
        /// Default is SixteenBit (0-65535).
        /// </summary>
        public MedianDisplayMode MedianDisplayMode { get; set; } = MedianDisplayMode.SixteenBit;
        
        /// <summary>
        /// Whether to show the histogram panel.
        /// Default is false to keep the interface cleaner.
        /// </summary>
        public bool ShowHistogram { get; set; } = false;
        
        /// <summary>
        /// Whether to scan XISF files for FITS keywords.
        /// Default is false because XISF scanning can be slow for large files.
        /// When enabled, FITS keywords will be extracted from XISF file headers.
        /// </summary>
        public bool ScanXisfForFitsKeywords { get; set; } = false;
        
        // Window state properties - these remember the window position and size
        
        /// <summary>
        /// X position of the main window on screen. NaN means use default positioning.
        /// </summary>
        public double WindowLeft { get; set; } = double.NaN;
        
        /// <summary>
        /// Y position of the main window on screen. NaN means use default positioning.
        /// </summary>
        public double WindowTop { get; set; } = double.NaN;
        
        /// <summary>
        /// Width of the main window in pixels.
        /// </summary>
        public double WindowWidth { get; set; } = 1200;
        
        /// <summary>
        /// Height of the main window in pixels.
        /// </summary>
        public double WindowHeight { get; set; } = 720;
        
        /// <summary>
        /// Window state as integer: 0 = Normal, 1 = Minimized, 2 = Maximized.
        /// Using int instead of enum for easier JSON serialization.
        /// </summary>
        public int WindowState { get; set; } = 0;
        
        /// <summary>
        /// Last directory used in the "Move Files" dialog.
        /// Remembers where user last moved files to for convenience.
        /// </summary>
        public string? LastMoveDirectory { get; set; } = null;
        
        /// <summary>
        /// Last directory opened by the user.
        /// Used to default the folder dialog to the last used location.
        /// </summary>
        public string? LastOpenDirectory { get; set; } = null;
        
        /// <summary>
        /// List of recently opened folders (up to 10, most recent first).
        /// Used for the "Open Recent" menu.
        /// </summary>
        public List<string> RecentFolders { get; set; } = new List<string>();
        
        /// <summary>
        /// Whether to show the splash screen on application startup.
        /// Default is true (show splash screen). Set to false to skip it.
        /// </summary>
        public bool ShowSplashScreen { get; set; } = true;

        /// <summary>
        /// Tracks whether the first-run image capture format prompt has already been shown.
        /// Prevents the startup prompt from appearing on every launch.
        /// </summary>
        public bool ImageCaptureFormatPromptShown { get; set; } = false;

        /// <summary>
        /// Whether to automatically check for updates on startup.
        /// Default is true (check for updates). Set to false to disable.
        /// </summary>
        public bool CheckForUpdates { get; set; } = true;

        /// <summary>
        /// Last time an update check was performed.
        /// Used to avoid checking too frequently (e.g., once per day).
        /// </summary>
        public DateTime? LastUpdateCheck { get; set; } = null;

        /// <summary>
        /// GitHub repository owner for update checks.
        /// Change this to your GitHub username when publishing.
        /// </summary>
        public string UpdateRepoOwner { get; set; } = "kfaubel";

        /// <summary>
        /// GitHub repository name for update checks.
        /// Change this to your repository name when publishing.
        /// </summary>
        public string UpdateRepoName { get; set; } = "ApexAstro";

        /// <summary>
        /// Whether to automatically stretch FITS and XISF images to enhance visibility.
        /// Default is true (apply stretching). When disabled, shows raw pixel values.
        /// </summary>
        public bool AutoStretch { get; set; } = true;

        /// <summary>
        /// Theme mode selection (Auto, Light, or Dark).
        /// Default is Auto, which follows the OS theme preference.
        /// </summary>
        public ThemeMode Theme { get; set; } = ThemeMode.Auto;

        /// <summary>
        /// Whether to show the full screen help dialog when entering full screen mode.
        /// Default is true (show help). Set to false to skip the help dialog.
        /// </summary>
        public bool ShowFullScreenHelp { get; set; } = true;

        /// <summary>
        /// Time in seconds to pause between images when playing through the list.
        /// Default is 0.5 second. Supported values: 0.25, 0.5, 1.0, 1.5, 2.0, 4.0, 8.0
        /// </summary>
        public double PlayPauseInterval { get; set; } = 0.5;

        /// <summary>
        /// Aggressiveness of the auto-stretch algorithm (0-10).
        /// 0 = very gentle (preserve all detail), 5 = balanced (default), 10 = aggressive (maximum contrast)
        /// Affects how much the histogram is clipped and how bright the midtones are pushed.
        /// </summary>
        public int StretchAggressiveness { get; set; } = 5;

        /// <summary>
        /// Number of sampled pixels per filter used for temporal SNR computation.
        /// Lower values reduce memory pressure and runtime, higher values can improve stability.
        /// </summary>
        public int TemporalSnrSamplePixelCount { get; set; } = 10000;

        /// <summary>
        /// Saved visual order of file-list columns by header text.
        /// This is used to restore user column reordering between sessions.
        /// </summary>
        public List<string> FileListColumnOrder { get; set; } = new List<string>();

        /// <summary>
        /// True only when a user has explicitly reordered columns away from the default layout.
        /// When false, default column ordering rules are used and saved order is ignored.
        /// </summary>
        public bool HasCustomColumnOrder { get; set; } = false;

        /// <summary>
        /// Graph settings: name of the column used as the X axis.
        /// </summary>
        public string GraphXColumn { get; set; } = "Date";

        /// <summary>
        /// Graph settings: list of column names used as Y axis series.
        /// </summary>
        public List<string> GraphYColumns { get; set; } = new List<string>();

        /// <summary>
        /// Graph settings: chart type — "Scatter" or "Line".
        /// </summary>
        public string GraphChartType { get; set; } = "Scatter";

        /// <summary>
        /// Graph window width in pixels.
        /// Persisted between sessions.
        /// </summary>
        public double GraphWindowWidth { get; set; } = 1200;

        /// <summary>
        /// Graph window height in pixels.
        /// Persisted between sessions.
        /// </summary>
        public double GraphWindowHeight { get; set; } = 820;

        /// <summary>
        /// Graph window left position on screen.
        /// NaN means use owner-centered default.
        /// </summary>
        public double GraphWindowLeft { get; set; } = double.NaN;

        /// <summary>
        /// Graph window top position on screen.
        /// NaN means use owner-centered default.
        /// </summary>
        public double GraphWindowTop { get; set; } = double.NaN;

        /// <summary>
        /// Saved Auto Select criteria values and enabled flags.
        /// Restored when opening the Auto Select dialog.
        /// </summary>
        public List<AutoSelectCriteriaSetting> AutoSelectCriteria { get; set; } = new List<AutoSelectCriteriaSetting>();

        /// <summary>
        /// Static field containing the full path where the configuration file is stored.
        /// Uses the user's AppData folder which is standard for application settings on Windows.
        /// Example path: C:\Users\[username]\AppData\Roaming\ApexAstro\app-config.json
        /// </summary>
        private static readonly string ConfigFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ApexAstro",
            "app-config.json");

        /// <summary>
        /// Static method to load configuration from file or create default configuration.
        /// Static means you can call it without creating an AppConfig instance first.
        /// This is the main way to get an AppConfig object at application startup.
        /// </summary>
        /// <returns>AppConfig object with loaded settings, or default settings if loading fails</returns>
        public static AppConfig Load()
        {
            try
            {
                // Check if the configuration file exists
                if (File.Exists(ConfigFilePath))
                {
                    // Read the entire file as a string
                    var json = File.ReadAllText(ConfigFilePath);
                    
                    var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
                    {
                        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
                    });
                    return config ?? new AppConfig();
                }
            }
            catch (Exception ex)
            {
                // If anything goes wrong (file corruption, permissions, etc.),
                // don't crash the app - just use default settings
                App.LoggingService?.LogError("Config Load", "Failed to load configuration file, using defaults", ex);
            }
            
            // File doesn't exist or loading failed - return default configuration
            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigFilePath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    // NaN is used for unset window positions; AllowNamedFloatingPointLiterals is required
                    // because System.Text.Json rejects NaN by default and would silently abort Save().
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
                });
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                // If config saving fails, continue silently but log it
                App.LoggingService?.LogError("Config Save", "Failed to save configuration file", ex);
            }
        }
    }

    public class AutoSelectCriteriaSetting
    {
        public string Key { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string MinValue { get; set; } = string.Empty;
        public string MaxValue { get; set; } = string.Empty;
        public bool AllowBlank { get; set; } = true;
        public string AllowedValues { get; set; } = string.Empty;
    }

    public static class FilenameParser
    {
        public static Dictionary<string, string> ExtractKeywordValues(string filename, IEnumerable<string> keywords)
        {
            var result = new Dictionary<string, string>();
            
            if (string.IsNullOrEmpty(filename))
                return result;

            // Remove file extension and split on underscores
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
            var tokens = nameWithoutExtension.Split('_');

            // Find each keyword and extract the following token as its value
            foreach (var keyword in keywords)
            {
                for (int i = 0; i < tokens.Length - 1; i++) // -1 because we need a following token
                {
                    if (tokens[i].Equals(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        result[keyword] = tokens[i + 1];
                        break; // Take the first occurrence
                    }
                }
            }

            return result;
        }

        public static string ExtractDateToken(string filename)
        {
            var tokens = GetFilenameTokens(filename);
            foreach (var token in tokens)
            {
                if (IsDateToken(token))
                {
                    return token;
                }
            }

            if (TryExtractCompactDateTime(tokens, out var compactDate, out _))
            {
                return compactDate;
            }

            return string.Empty;
        }

        public static string ExtractTimeToken(string filename)
        {
            var tokens = GetFilenameTokens(filename);
            for (int i = 0; i < tokens.Length - 1; i++)
            {
                if (IsDateToken(tokens[i]) && IsTimeToken(tokens[i + 1]))
                {
                    return NormalizeTimeToken(tokens[i + 1]);
                }
            }

            if (TryExtractCompactDateTime(tokens, out _, out var compactTime))
            {
                return compactTime;
            }

            return string.Empty;
        }

        public static string ExtractFrameToken(string filename)
        {
            var tokens = GetFilenameTokens(filename);
            if (tokens.Length == 0)
            {
                return string.Empty;
            }

            var last = tokens[tokens.Length - 1];
            return IsAllDigits(last) ? last : string.Empty;
        }

        private static string[] GetFilenameTokens(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return Array.Empty<string>();
            }

            var nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
            if (string.IsNullOrWhiteSpace(nameWithoutExtension))
            {
                return Array.Empty<string>();
            }

            return nameWithoutExtension.Split('_');
        }

        private static bool IsDateToken(string token)
        {
            return token.Length == 10
                && char.IsDigit(token[0])
                && char.IsDigit(token[1])
                && char.IsDigit(token[2])
                && char.IsDigit(token[3])
                && token[4] == '-'
                && char.IsDigit(token[5])
                && char.IsDigit(token[6])
                && token[7] == '-'
                && char.IsDigit(token[8])
                && char.IsDigit(token[9]);
        }

        private static bool IsTimeToken(string token)
        {
            return token.Length == 8
                && char.IsDigit(token[0])
                && char.IsDigit(token[1])
                && (token[2] == '-' || token[2] == ':')
                && char.IsDigit(token[3])
                && char.IsDigit(token[4])
                && (token[5] == '-' || token[5] == ':')
                && char.IsDigit(token[6])
                && char.IsDigit(token[7]);
        }

        private static bool IsAllDigits(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsDigit(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryExtractCompactDateTime(string[] tokens, out string dateToken, out string timeToken)
        {
            dateToken = string.Empty;
            timeToken = string.Empty;

            if (tokens.Length == 0)
            {
                return false;
            }

            foreach (var token in tokens)
            {
                if (IsCompactDateTimeToken(token, out dateToken, out timeToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCompactDateTimeToken(string token, out string dateToken, out string timeToken)
        {
            dateToken = string.Empty;
            timeToken = string.Empty;

            if (token.Length != 15)
            {
                return false;
            }

            if (!(char.IsDigit(token[0])
                && char.IsDigit(token[1])
                && char.IsDigit(token[2])
                && char.IsDigit(token[3])
                && char.IsDigit(token[4])
                && char.IsDigit(token[5])
                && char.IsDigit(token[6])
                && char.IsDigit(token[7])
                && token[8] == '-'
                && char.IsDigit(token[9])
                && char.IsDigit(token[10])
                && char.IsDigit(token[11])
                && char.IsDigit(token[12])
                && char.IsDigit(token[13])
                && char.IsDigit(token[14])))
            {
                return false;
            }

            dateToken = $"{token.Substring(0, 4)}-{token.Substring(4, 2)}-{token.Substring(6, 2)}";
            timeToken = $"{token.Substring(9, 2)}:{token.Substring(11, 2)}:{token.Substring(13, 2)}";
            return true;
        }

        private static string NormalizeTimeToken(string token)
        {
            if (token.Length != 8)
            {
                return token;
            }

            return string.Concat(token[0], token[1], ':', token[3], token[4], ':', token[6], token[7]);
        }
    }
}