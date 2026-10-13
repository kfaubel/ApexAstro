
// This is the main ViewModel for the application, following the MVVM (Model-View-ViewModel) architectural pattern
// In MVVM:
// - Model: Represents data and business logic (FileItem, AppConfig, etc.)
// - View: The user interface (MainWindow.xaml)
// - ViewModel: The bridge between View and Model, handling UI logic and data binding

using ApexAstro.Wpf.Models;
using ApexAstro.Wpf.Services;
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Globalization;
using System.Text.Json;

namespace ApexAstro.Wpf.ViewModels
{
    /// <summary>
    /// MainWindowViewModel handles all the UI logic and data for the main application window.
    /// It implements INotifyPropertyChanged to support WPF data binding - when properties change,
    /// the UI automatically updates to reflect those changes.
    /// 
    /// Key MVVM Concepts Demonstrated:
    /// - Property Change Notification: Properties notify the UI when they change
    /// - Command Pattern: User actions are handled through RelayCommand objects
    /// - Data Binding: Properties are bound to UI elements in the XAML
    /// - Dependency Injection: Services are injected through the constructor
    /// - Separation of Concerns: UI logic is separate from business logic
    /// </summary>
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        #region Private Fields
        // Private fields store the actual data. These are the "backing fields" for properties.
        // By convention, private fields start with underscore (_) and use camelCase naming.
        private double _zoomLevel = 1.0;       // Current zoom level for image display
        private double _fitScale = 1.0;        // Scale factor when fitting image to window
        private string _currentDirectory = string.Empty;  // Currently opened folder path
        private HistogramViewModel _histogramViewModel = new HistogramViewModel();  // Histogram view model
        private bool _isDirectoryReviewed;
        private bool _isLoadingDirectoryReviewedState;
        private const string ReviewedMarkerFileName = ".apexastro.reviewed.json";
        private const string LegacyReviewedMarkerFileName = ".aprxastro.reviewed.json";
        private const string LegacyReviewedMarkerFileNameV1 = ".apexastro.reviewed";
        private string _temporalSnrStatusText = string.Empty;
        #endregion

        #region Image Display Properties
        // These properties control how images are displayed and zoomed.
        // The get/set pattern with OnPropertyChanged is crucial for WPF data binding.
        
        /// <summary>
        /// Scale factor used when fitting the image to the window.
        /// This is calculated automatically based on image and window dimensions.
        /// </summary>
        public double FitScale
        {
            get => _fitScale;
            set
            {
                // Only update if the value actually changed (avoid unnecessary UI updates)
                if (Math.Abs(_fitScale - value) > 0.0001)
                {
                    _fitScale = value;
                    // Notify the UI that this property changed so bound controls can update
                    OnPropertyChanged(nameof(FitScale));
                }
            }
        }

        private bool _autoStretch = true;  // Default to enabled
        
        /// <summary>
        /// Controls whether FITS and XISF images should be automatically stretched to enhance visibility.
        /// When enabled, applies histogram stretching and gamma correction to astronomical images.
        /// When disabled, shows raw pixel values without enhancement.
        /// </summary>
        public bool AutoStretch
        {
            get => _autoStretch;
            set
            {
                if (_autoStretch != value)
                {
                    _autoStretch = value;
                    // Save to configuration
                    _appConfig.AutoStretch = value;
                    _appConfig.Save();
                    OnPropertyChanged(nameof(AutoStretch));
                    // Trigger image refresh when stretching mode changes
                    AutoStretchChanged?.Invoke();
                }
            }
        }

        /// <summary>
        /// Event that notifies the View when auto-stretch setting changes.
        /// The View can use this to refresh the current image with the new setting.
        /// </summary>
        public event Action? AutoStretchChanged;
        
        /// <summary>
        /// Gets the histogram view model for displaying image histograms
        /// </summary>
        public HistogramViewModel HistogramViewModel => _histogramViewModel;

        private int _stretchAggressiveness;

        /// <summary>
        /// Controls the aggressiveness of the auto-stretch algorithm (0-10).
        /// 0 = Off (no stretching), higher values = more aggressive stretching.
        /// </summary>
        public int StretchAggressiveness
        {
            get => _stretchAggressiveness;
            set
            {
                // Clamp value between 0 and 10
                var clampedValue = Math.Max(0, Math.Min(10, value));
                if (_stretchAggressiveness != clampedValue)
                {
                    _stretchAggressiveness = clampedValue;
                    _appConfig.StretchAggressiveness = clampedValue;
                    // Update AutoStretch based on value
                    AutoStretch = clampedValue > 0;
                    _appConfig.Save();
                    OnPropertyChanged(nameof(StretchAggressiveness));
                    OnPropertyChanged(nameof(StretchDisplayText));
                    // Trigger image refresh when stretching changes
                    AutoStretchChanged?.Invoke();
                }
            }
        }

        /// <summary>
        /// Display text for stretch value. Shows "Off" when 0, otherwise the numeric value.
        /// </summary>
        public string StretchDisplayText => StretchAggressiveness == 0 ? "Off" : StretchAggressiveness.ToString();

        /// <summary>
        /// Command to increase stretch aggressiveness
        /// </summary>
        public RelayCommand IncreaseStretchCommand { get; }

        /// <summary>
        /// Command to decrease stretch aggressiveness
        /// </summary>
        public RelayCommand DecreaseStretchCommand { get; }

        /// <summary>
        /// Current zoom level for the image (1.0 = 100%, 2.0 = 200%, etc.)
        /// When this changes, we also notify ZoomDisplayText since it depends on ZoomLevel.
        /// </summary>
        public double ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                if (Math.Abs(_zoomLevel - value) > 0.0001)
                {
                    _zoomLevel = value;
                    OnPropertyChanged(nameof(ZoomLevel));
                    // Also notify dependent properties that use ZoomLevel in their calculations
                    OnPropertyChanged(nameof(ZoomDisplayText));
                }
            }
        }

        private bool _fitMode = true;  // Whether image should fit to window or use manual zoom
        
        /// <summary>
        /// Determines whether the image should automatically fit to the window (true)
        /// or use manual zoom levels (false). This affects scrollbar visibility.
        /// </summary>
        public bool FitMode
        {
            get => _fitMode;
            set
            {
                if (_fitMode != value)
                {
                    _fitMode = value;
                    OnPropertyChanged(nameof(FitMode));
                    // ScrollBarVisibility depends on FitMode, so notify it too
                    OnPropertyChanged(nameof(ScrollBarVisibility));
                }
            }
        }

        /// <summary>
        /// Computed property that formats zoom level for display in the UI.
        /// Uses string interpolation and percentage formatting (:P0 means percentage with 0 decimal places).
        /// This demonstrates how ViewModels can provide formatted data for the View.
        /// </summary>
        public string ZoomDisplayText => FitMode ? $"Fit ({ZoomLevel:P0})" : $"Zoom: {ZoomLevel:P0}";

        /// <summary>
        /// Computed property that determines whether scrollbars should be visible.
        /// In fit mode, scrollbars are hidden because the image always fits the window.
        /// In manual zoom mode, scrollbars appear automatically when needed.
        /// </summary>
        public System.Windows.Controls.ScrollBarVisibility ScrollBarVisibility => 
            FitMode ? System.Windows.Controls.ScrollBarVisibility.Hidden : System.Windows.Controls.ScrollBarVisibility.Auto;

        // Commands are the MVVM way of handling user actions (button clicks, menu selections, etc.)
        // Each command encapsulates an action and can specify when it's enabled/disabled
        public RelayCommand ZoomInCommand { get; }           // Increase zoom level
        public RelayCommand ZoomOutCommand { get; }          // Decrease zoom level  
        public RelayCommand FitToWindowCommand { get; }      // Fit image to window
        public RelayCommand ResetZoomCommand { get; }        // Reset to 100% zoom
        public RelayCommand ZoomActualCommand { get; }       // Set to actual size (1:1 pixels)
        #endregion
        #region File Management Methods
        /// <summary>
        /// Loads all files from the specified directory and populates the Files collection.
        /// This method demonstrates the Service Layer pattern - instead of doing file I/O directly,
        /// we delegate to specialized services that handle the specific tasks.
        /// </summary>
        /// <param name="directoryPath">Path to the directory containing files to load</param>
        public System.Collections.Generic.List<Models.FileItem> LoadFiles(string directoryPath, Action<int, int>? progressCallback = null)
        {
            _loggingService.LogFolderOpen(directoryPath);
            
            // Add to recent folders
            AddToRecentFolders(directoryPath);
            _appConfig.Save();
            
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // Reset filtered view mode - we're loading a full directory
            IsFilteredView = false;
            
            // Set the current directory for display in title and status bar
            CurrentDirectory = directoryPath;
            
            // Load ImageMetaData.csv from the folder (fast I/O, done synchronously)
            _csvMetadataService.Load(directoryPath);
            
            // Load the AstroData.csv stats cache for this folder
            _astroDataCacheService.Load(directoryPath);
            
            // Use the file management service to get file information
            var loadDirStart = totalStopwatch.ElapsedMilliseconds;
            var fileItems = _fileManagementService.LoadFilesFromDirectory(directoryPath);
            _loggingService.LogInfo($"Directory scan took {totalStopwatch.ElapsedMilliseconds - loadDirStart}ms for {fileItems.Count} files");

            foreach (var fileItem in fileItems)
            {
                fileItem.MetadataReady = false;
            }
            
            // Return file items immediately without metadata - metadata will be populated separately
            return fileItems.OrderBy(f => f.Name).ToList();
        }
        
        /// <summary>
        /// Populates metadata for files in the background, updating the UI progressively
        /// </summary>
        public void PopulateMetadataAsync(System.Collections.Generic.List<Models.FileItem> fileItems, Action<int, int>? progressCallback = null)
        {
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
            int total = fileItems.Count;
            
            // Use thread-safe counter for progress tracking
            int processedCount = 0;
            object progressLock = new object();
            
            // Track timing per file type
            var fitsCount = 0;
            var xisfCount = 0;
            var otherCount = 0;
            
            // Track timing statistics
            var fileTimes = new System.Collections.Concurrent.ConcurrentBag<(string fileName, long elapsedMs)>();
            
            foreach (var fileItem in fileItems)
            {
                fileItem.MetadataReady = false;
            }

            // Process files in parallel for better performance
            var metadataStart = totalStopwatch.ElapsedMilliseconds;
            System.Threading.Tasks.Parallel.ForEach(fileItems, 
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                fileItem =>
                {
                    var fileStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        // Track file types
                        var ext = System.IO.Path.GetExtension(fileItem.Name).ToLowerInvariant();
                        if (ext == ".fits" || ext == ".fit" || ext == ".fts")
                            System.Threading.Interlocked.Increment(ref fitsCount);
                        else if (ext == ".xisf")
                            System.Threading.Interlocked.Increment(ref xisfCount);
                        else
                            System.Threading.Interlocked.Increment(ref otherCount);
                        
                        // Check AstroData.csv cache for median/mean before doing expensive full-file read
                        bool calculateStats = _appConfig.ShowMedianColumn;
                        if (_appConfig.ShowMedianColumn)
                        {
                            try
                            {
                                var lastWrite = System.IO.File.GetLastWriteTimeUtc(fileItem.Path);
                                if (_astroDataCacheService.TryGetCached(fileItem.Name, lastWrite,
                                        out var cachedMedian, out var cachedMean))
                                {
                                    fileItem.Median = cachedMedian;
                                    fileItem.Mean = cachedMean;
                                    calculateStats = false;
                                }
                            }
                            catch { /* file stat failed; fall through to compute */ }
                        }

                        // Extract both custom and FITS keywords for each file
                        _keywordExtractionService.PopulateKeywords(fileItem, _appConfig.CustomKeywords, _appConfig.FitsKeywords, !_appConfig.ScanXisfForFitsKeywords, calculateStats, _appConfig.ArwKeywords);
                        
                        // Populate CSV keywords from ImageMetaData.csv (if any columns are configured)
                        if (_appConfig.CsvKeywords.Count > 0)
                            fileItem.CsvKeywords = _csvMetadataService.GetValues(fileItem.Name, _appConfig.CsvKeywords);
                        
                        fileStopwatch.Stop();
                        var elapsed = fileStopwatch.ElapsedMilliseconds;
                        
                        // Track timing for slow files
                        if (elapsed > 100) // Files taking more than 100ms
                        {
                            fileTimes.Add((fileItem.Name, elapsed));
                        }
                        
                        // Update progress in a thread-safe manner
                        lock (progressLock)
                        {
                            processedCount++;
                            // Report progress every 5 files to reduce overhead
                            if (processedCount % 5 == 0 || processedCount == total)
                            {
                                progressCallback?.Invoke(processedCount, total);
                                // Log progress occasionally
                                if (processedCount % 20 == 0 || processedCount == total)
                                {
                                    _loggingService.LogMetadataReading(fileItem.Name, total, processedCount);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        fileStopwatch.Stop();
                        _loggingService.LogError("Metadata Extraction", $"Failed to process {fileItem.Name}: {ex.Message}", ex);
                    }
                });

            foreach (var fileItem in fileItems)
            {
                fileItem.MetadataReady = true;
            }
            
            var metadataTime = totalStopwatch.ElapsedMilliseconds - metadataStart;
            var avgTimePerFile = total > 0 ? metadataTime / (double)total : 0;
            _loggingService.LogInfo($"Metadata extraction: {metadataTime}ms total, {avgTimePerFile:F1}ms avg per file ({fitsCount} FITS, {xisfCount} XISF, {otherCount} other)");
            
            // Log slowest files if any were slow
            if (!fileTimes.IsEmpty)
            {
                var slowFiles = fileTimes.OrderByDescending(f => f.elapsedMs).Take(5).ToList();
                _loggingService.LogInfo($"Slowest files: {string.Join(", ", slowFiles.Select(f => $"{f.fileName} ({f.elapsedMs}ms)"))}");
            }
            
            // Check if median calculation is enabled but medians are missing
            // This can happen if the setting was just enabled or if calculation failed
            if (_appConfig.ShowMedianColumn)
            {
                var filesWithoutMedian = fileItems.Where(f => !f.Median.HasValue).ToList();
                if (filesWithoutMedian.Count > 0)
                {
                    var medianStart = totalStopwatch.ElapsedMilliseconds;
                    _loggingService.LogInfo($"Recalculating medians for {filesWithoutMedian.Count} files that are missing median values...");
                    
                    // Calculate missing medians
                    System.Threading.Tasks.Parallel.ForEach(
                        filesWithoutMedian,
                        new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                        fileItem =>
                        {
                            try
                            {
                                var (median, mean) = _keywordExtractionService.CalculateStatsForFile(fileItem.Path);
                                fileItem.Median = median;
                                fileItem.Mean = mean;
                            }
                            catch (Exception ex)
                            {
                                _loggingService.LogError("Median Calculation", $"Failed to calculate median for {fileItem.Name}: {ex.Message}", ex);
                            }
                        });
                    
                    var medianTime = totalStopwatch.ElapsedMilliseconds - medianStart;
                    var avgMedianTime = filesWithoutMedian.Count > 0 ? medianTime / (double)filesWithoutMedian.Count : 0;
                    _loggingService.LogInfo($"Median recalculation: {medianTime}ms total, {avgMedianTime:F1}ms avg per file");
                }

                // Persist computed stats to AstroData.csv so the next folder open can skip recomputation
                _astroDataCacheService.Save(CurrentDirectory, fileItems);
            }

            LogHfrMedianAndMadByFilter(fileItems);
        }
        
        /// <summary>
        /// Updates the Files collection with the loaded items. Must be called on UI thread.
        /// </summary>
        /// <param name="fileItems">The processed file items to add</param>
        public void UpdateFilesCollection(System.Collections.Generic.List<Models.FileItem> fileItems)
        {
            // Clear existing files from the observable collection
            // First, unhook event handlers from existing items
            foreach (var fileItem in Files)
            {
                fileItem.PropertyChanged -= FileItem_PropertyChanged;
            }
            
            // ObservableCollection automatically notifies the UI when items are added/removed
            Files.Clear();
            
            // Add all processed items to the observable collection
            foreach (var fileItem in fileItems)
            {
                // Listen for IsSelected property changes to update HasSelectedFiles
                fileItem.PropertyChanged += FileItem_PropertyChanged;
                Files.Add(fileItem);
            }

            // Set default sort by filename
            SortByColumn("File");
            
            // Notify that the file count and selection status has changed
            OnPropertyChanged(nameof(FileSelectionStatusText));
            OnPropertyChanged(nameof(StatusBarText));
            OnPropertyChanged(nameof(CanComputeSnr));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            
            // Notify the View that files have been loaded so it can update the UI
            FilesLoaded?.Invoke();
        }

        /// <summary>
        /// Loads only the specified files (used when opening files from Windows Explorer).
        /// This method loads specific files instead of scanning an entire directory.
        /// </summary>
        /// <param name="filePaths">Array of file paths to load</param>
        /// <param name="progressCallback">Optional progress callback (current, total)</param>
        public System.Collections.Generic.List<Models.FileItem> LoadSpecificFiles(string[] filePaths, Action<int, int>? progressCallback = null)
        {
            // Set filtered view mode and determine directory from first file
            IsFilteredView = true;
            
            // Debug: Log the files being processed
            System.Diagnostics.Debug.WriteLine($"LoadSpecificFiles called with {filePaths.Length} files");
            foreach (var path in filePaths)
            {
                System.Diagnostics.Debug.WriteLine($"  File: {path}");
            }
            
            if (filePaths.Length > 0)
            {
                CurrentDirectory = System.IO.Path.GetDirectoryName(filePaths[0]) ?? "";
                // Load ImageMetaData.csv from the folder
                _csvMetadataService.Load(CurrentDirectory);
                // Load the AstroData.csv stats cache for this folder
                _astroDataCacheService.Load(CurrentDirectory);
            }
            
            int total = filePaths.Length;
            
            // Use thread-safe counter for progress tracking
            int processedCount = 0;
            object progressLock = new object();
            
            // Process files in parallel for better performance
            var processedItems = new System.Collections.Concurrent.ConcurrentBag<Models.FileItem>();
            
            System.Threading.Tasks.Parallel.ForEach(filePaths,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                filePath =>
                {
                    if (System.IO.File.Exists(filePath))
                    {
                        var fileItem = _fileManagementService.GetFileInfo(filePath);
                        
                        // Check AstroData.csv cache for median/mean before expensive full-file read
                        bool calculateStats = _appConfig.ShowMedianColumn;
                        if (_appConfig.ShowMedianColumn)
                        {
                            try
                            {
                                var lastWrite = System.IO.File.GetLastWriteTimeUtc(filePath);
                                if (_astroDataCacheService.TryGetCached(fileItem.Name, lastWrite,
                                        out var cachedMedian, out var cachedMean))
                                {
                                    fileItem.Median = cachedMedian;
                                    fileItem.Mean = cachedMean;
                                    calculateStats = false;
                                }
                            }
                            catch { /* file stat failed; fall through to compute */ }
                        }

                        // Extract both custom and FITS keywords for the file
                        _keywordExtractionService.PopulateKeywords(fileItem, _appConfig.CustomKeywords, _appConfig.FitsKeywords, skipXisf: false, calculateMedian: calculateStats, arwKeywords: _appConfig.ArwKeywords);
                        
                        // Populate CSV keywords
                        if (_appConfig.CsvKeywords.Count > 0)
                            fileItem.CsvKeywords = _csvMetadataService.GetValues(fileItem.Name, _appConfig.CsvKeywords);
                        
                        // Add to thread-safe collection
                        processedItems.Add(fileItem);
                    }
                    
                    // Update progress in a thread-safe manner
                    lock (progressLock)
                    {
                        processedCount++;
                        // Report progress every 5 files to reduce overhead
                        if (processedCount % 5 == 0 || processedCount == total)
                        {
                            progressCallback?.Invoke(processedCount, total);
                        }
                    }
                });
            
            // Return sorted list - caller will add to ObservableCollection on UI thread
            var result = processedItems.OrderBy(f => f.Name).ToList();
            System.Diagnostics.Debug.WriteLine($"LoadSpecificFiles returning {result.Count} items");

            // Persist computed stats to AstroData.csv
            if (_appConfig.ShowMedianColumn && !string.IsNullOrEmpty(CurrentDirectory))
                _astroDataCacheService.Save(CurrentDirectory, result);

            LogHfrMedianAndMadByFilter(result);

            return result;
        }

        private void LogHfrMedianAndMadByFilter(System.Collections.Generic.List<Models.FileItem> fileItems)
        {
            var hfrByFilter = fileItems
                .Select(fileItem =>
                {
                    if (!TryGetHfrValue(fileItem, out var hfrValue))
                    {
                        return (hasValue: false, filter: string.Empty, hfr: 0.0);
                    }

                    var filter = ResolveFilterForHfrStats(fileItem);
                    if (string.IsNullOrWhiteSpace(filter))
                    {
                        return (hasValue: false, filter: string.Empty, hfr: 0.0);
                    }

                    return (hasValue: true, filter, hfr: hfrValue);
                })
                .Where(x => x.hasValue)
                .GroupBy(x => x.filter, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var filterGroup in hfrByFilter)
            {
                var hfrValues = filterGroup.Select(x => x.hfr).ToList();
                if (hfrValues.Count == 0)
                {
                    continue;
                }

                var median = ComputeMedian(hfrValues);
                var mad = ComputeMedian(hfrValues.Select(value => Math.Abs(value - median)));
                _loggingService.LogInfo($"Filter: {filterGroup.Key}, HFR Median: {median:F2}, MAD: {mad:F2}");
            }
        }

        private static bool TryGetHfrValue(FileItem fileItem, out double hfrValue)
        {
            hfrValue = 0;

            string? rawHfr = null;
            if (TryGetDictionaryValue(fileItem.CustomKeywords, "HFR", out var customHfr))
            {
                rawHfr = customHfr;
            }
            else if (TryGetDictionaryValue(fileItem.CsvKeywords, "HFR", out var csvHfr))
            {
                rawHfr = csvHfr;
            }
            else if (TryGetDictionaryValue(fileItem.FitsKeywords, "HFR", out var fitsHfr))
            {
                rawHfr = fitsHfr;
            }

            return double.TryParse(rawHfr, NumberStyles.Float, CultureInfo.InvariantCulture, out hfrValue)
                   && double.IsFinite(hfrValue);
        }

        private static string ResolveFilterForHfrStats(FileItem fileItem)
        {
            if (TryGetDictionaryValue(fileItem.FitsKeywords, "FILTER", out var rawFitsFilter))
            {
                var normalizedFitsFilter = NormalizeFilterValue(rawFitsFilter);
                if (!string.IsNullOrWhiteSpace(normalizedFitsFilter))
                {
                    return normalizedFitsFilter;
                }
            }

            return NormalizeFilterValue(ParseFilterFromFilename(fileItem.Name));
        }

        private static bool TryGetDictionaryValue(Dictionary<string, string> dictionary, string key, out string value)
        {
            foreach (var kvp in dictionary)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = kvp.Value;
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }

        private static string ParseFilterFromFilename(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return string.Empty;
            }

            var nameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(filename);
            if (string.IsNullOrWhiteSpace(nameWithoutExtension))
            {
                return string.Empty;
            }

            var tokens = nameWithoutExtension.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length > 2 ? tokens[2] : string.Empty;
        }

        private static string NormalizeFilterValue(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var compact = raw.Trim().ToUpperInvariant().Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
            return compact switch
            {
                "L" or "LUM" or "LUMINANCE" => "L",
                "R" or "RED" => "R",
                "G" or "GREEN" => "G",
                "B" or "BLUE" => "B",
                "S" or "SII" or "S2" => "S",
                "H" or "HA" or "HALPHA" or "HALPHA7" => "H",
                "O" or "OIII" or "O3" => "O",
                _ => compact.Length == 1 && "LRGBSHO".Contains(compact, StringComparison.Ordinal) ? compact : string.Empty
            };
        }

        private static double ComputeMedian(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            if (sorted.Count == 0)
            {
                return 0;
            }

            var middle = sorted.Count / 2;
            return sorted.Count % 2 == 0
                ? (sorted[middle - 1] + sorted[middle]) / 2.0
                : sorted[middle];
        }
        
        /// <summary>
        /// Updates the Files collection with specific files. Must be called on UI thread.
        /// </summary>
        /// <param name="fileItems">The processed file items to add</param>
        public void UpdateSpecificFilesCollection(System.Collections.Generic.List<Models.FileItem> fileItems)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateSpecificFilesCollection called with {fileItems.Count} items");
            
            // Clear existing files from the observable collection
            // First, unhook event handlers from existing items
            foreach (var fileItem in Files)
            {
                fileItem.PropertyChanged -= FileItem_PropertyChanged;
            }
            
            Files.Clear();
            
            // Add all processed items to the observable collection
            foreach (var fileItem in fileItems)
            {
                System.Diagnostics.Debug.WriteLine($"  Adding file: {fileItem.Name}");
                // Listen for IsSelected property changes to update HasSelectedFiles
                fileItem.PropertyChanged += FileItem_PropertyChanged;
                Files.Add(fileItem);
            }
            
            System.Diagnostics.Debug.WriteLine($"Files collection now has {Files.Count} items");
            
            // Set default sort by filename
            SortByColumn("File");
            
            // Notify that the file count and selection status has changed
            OnPropertyChanged(nameof(FileSelectionStatusText));
            OnPropertyChanged(nameof(StatusBarText));
            OnPropertyChanged(nameof(CanComputeSnr));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            
            // Notify the View that files have been loaded so it can update the UI
            FilesLoaded?.Invoke();
        }

        /// <summary>
        /// Refreshes the custom keywords for all files in the current list.
        /// Called when custom keyword configuration changes.
        /// </summary>
        public void RefreshFileListKeywords(Action<int, int>? progressCallback = null)
        {
            int total = Files.Count;
            int current = 0;
            
            foreach (var fileItem in Files)
            {
                fileItem.CustomKeywords = _keywordExtractionService.ExtractCustomKeywordsFromFilename(fileItem.Name, _appConfig.CustomKeywords);
                
                current++;
                progressCallback?.Invoke(current, total);
            }
        }

        /// <summary>
        /// Refreshes the FITS keywords for all files in the current list.
        /// Called when FITS keyword configuration changes.
        /// </summary>
        public void RefreshFileListFitsKeywords(Action<int, int>? progressCallback = null)
        {
            int total = Files.Count;
            int current = 0;
            
            foreach (var fileItem in Files)
            {
                fileItem.FitsKeywords = _keywordExtractionService.ExtractFitsKeywords(fileItem.Path, _appConfig.FitsKeywords);
                
                current++;
                progressCallback?.Invoke(current, total);
            }
        }

        /// <summary>
        /// Refreshes the CSV keywords for all files in the current list.
        /// Called when CSV keyword configuration changes.
        /// </summary>
        public void RefreshFileListCsvKeywords()
        {
            foreach (var fileItem in Files)
                fileItem.CsvKeywords = _csvMetadataService.GetValues(fileItem.Name, _appConfig.CsvKeywords);
        }

        /// <summary>
        /// Refreshes ARW metadata keywords for all files in the current list.
        /// Called when ARW metadata configuration changes.
        /// </summary>
        public void RefreshFileListArwKeywords()
        {
            foreach (var fileItem in Files)
                fileItem.ArwKeywords = _keywordExtractionService.ExtractArwKeywords(fileItem.Path, _appConfig.ArwKeywords);
        }
        #endregion
        #region Navigation and Playback Properties
        // These fields support image navigation and automatic playback functionality
        private int _selectedIndex = -1;       // Index of currently selected file (-1 = none selected)
        private bool _isPlaying;               // Whether automatic playback is active
        private System.Timers.Timer? _playTimer; // Timer for automatic playback (nullable because it's created on demand)
        private bool _awaitingImageRender;     // Whether we're waiting for image rendering to complete before starting next timer

        /// <summary>
        /// Index of the currently selected file in the Files collection.
        /// -1 indicates no file is selected. This property is bound to the ListView's SelectedIndex.
        /// </summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_selectedIndex != value)
                {
                    _selectedIndex = value;
                    OnPropertyChanged(nameof(SelectedIndex));
                }
            }
        }

        /// <summary>
        /// Indicates whether automatic playback (slideshow mode) is currently active.
        /// This property can be bound to UI elements to show/hide play/pause buttons.
        /// </summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged(nameof(IsPlaying));
                }
            }
        }

        /// <summary>
        /// Indicates whether any files are currently selected (checked) in the file list.
        /// Used to enable/disable the Move Selected button.
        /// </summary>
        public bool HasSelectedFiles => Files.Any(f => f.IsSelected);

        /// <summary>
        /// Gets the file selection status text to display (e.g., "10 of 200 files selected").
        /// </summary>
        public string FileSelectionStatusText
        {
            get
            {
                int selectedCount = Files.Count(f => f.IsSelected);
                int totalCount = Files.Count;
                
                if (selectedCount == 0)
                {
                    return $"{totalCount} file{(totalCount != 1 ? "s" : "")}";
                }
                else
                {
                    return $"{selectedCount} of {totalCount} file{(totalCount != 1 ? "s" : "")} selected";
                }
            }
        }

        /// <summary>
        /// Gets or sets the currently opened directory path.
        /// This is used for the window title and status bar display.
        /// </summary>
        public string CurrentDirectory
        {
            get => _currentDirectory;
            set
            {
                if (_currentDirectory != value)
                {
                    _currentDirectory = value;
                    OnPropertyChanged(nameof(CurrentDirectory));
                    OnPropertyChanged(nameof(CanMarkDirectoryReviewed));
                    OnPropertyChanged(nameof(CanComputeSnr));
                    OnPropertyChanged(nameof(DirectoryReviewedStatusText));
                    OnPropertyChanged(nameof(WindowTitle));
                    LoadDirectoryReviewedState();
                    OnPropertyChanged(nameof(StatusBarText));
                    OnPropertyChanged(nameof(TemporalSnrStatusText));
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// Indicates whether the current directory has been reviewed/cleaned up.
        /// This value is persisted to a marker file in the directory itself.
        /// </summary>
        public bool IsDirectoryReviewed
        {
            get => _isDirectoryReviewed;
            set
            {
                if (_isDirectoryReviewed != value)
                {
                    _isDirectoryReviewed = value;
                    OnPropertyChanged(nameof(IsDirectoryReviewed));
                    OnPropertyChanged(nameof(DirectoryReviewedStatusText));
                    SaveDirectoryReviewedState();
                }
            }
        }

        /// <summary>
        /// Returns true when there is an open directory where review state can be persisted.
        /// </summary>
        public bool CanMarkDirectoryReviewed =>
            !string.IsNullOrWhiteSpace(CurrentDirectory) && System.IO.Directory.Exists(CurrentDirectory);

        /// <summary>
        /// Returns true when temporal SNR can be computed for the current folder.
        /// </summary>
        public bool CanComputeSnr =>
            !string.IsNullOrWhiteSpace(CurrentDirectory)
            && System.IO.Directory.Exists(CurrentDirectory)
            && FileCount > 0;

        /// <summary>
        /// Status text shown in the status bar after temporal SNR work completes.
        /// </summary>
        public string TemporalSnrStatusText
        {
            get => _temporalSnrStatusText;
            set
            {
                if (_temporalSnrStatusText != value)
                {
                    _temporalSnrStatusText = value;
                    OnPropertyChanged(nameof(TemporalSnrStatusText));
                }
            }
        }

        /// <summary>
        /// User-facing review state text for the status bar.
        /// </summary>
        public string DirectoryReviewedStatusText
        {
            get
            {
                return IsDirectoryReviewed ? "Reviewed" : "Not reviewed";
            }
        }

        /// <summary>
        /// Indicates whether we're showing a filtered view (specific files from command-line)
        /// instead of all files from a directory.
        /// </summary>
        public bool IsFilteredView { get; set; }

        /// <summary>
        /// Computed property for the window title showing application name and current folder.
        /// </summary>
        public string WindowTitle
        {
            get
            {
                if (string.IsNullOrEmpty(CurrentDirectory))
                {
                    return "ApexAstro";
                }
                
                // Show just the folder name in the title
                var folderName = System.IO.Path.GetFileName(CurrentDirectory);
                return $"ApexAstro - {folderName}";
            }
        }

        /// <summary>
        /// Computed property for the status bar showing full path and file statistics.
        /// </summary>
        public string StatusBarText
        {
            get
            {
                if (string.IsNullOrEmpty(CurrentDirectory))
                {
                    return "No folder opened";
                }
                
                int selectedCount = Files.Count(f => f.IsSelected);
                int totalCount = Files.Count;
                
                string selectionText = selectedCount > 0 ? $" | {selectedCount} selected" : "";
                
                // Add filtered view indicator
                string filteredViewText = IsFilteredView ? $" (showing {totalCount} selected file{(totalCount != 1 ? "s" : "")})" : "";
                
                return $"📁 {CurrentDirectory} | {totalCount} file{(totalCount != 1 ? "s" : "")}{selectionText}{filteredViewText}";
            }
        }

        /// <summary>
        /// Formats file size in human-readable format (bytes, KB, MB, GB).
        /// </summary>
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "bytes", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            
            return $"{len:0.##} {sizes[order]}";
        }

        private string GetReviewedMarkerFilePath(string directoryPath)
        {
            return System.IO.Path.Combine(directoryPath, ReviewedMarkerFileName);
        }

        private string GetLegacyReviewedMarkerFilePath(string directoryPath)
        {
            return System.IO.Path.Combine(directoryPath, LegacyReviewedMarkerFileName);
        }

        private string GetLegacyReviewedMarkerFilePathV1(string directoryPath)
        {
            return System.IO.Path.Combine(directoryPath, LegacyReviewedMarkerFileNameV1);
        }

        private System.Collections.Generic.List<string> GetReviewedDirectories()
        {
            if (string.IsNullOrWhiteSpace(CurrentDirectory) || !System.IO.Directory.Exists(CurrentDirectory))
                return new System.Collections.Generic.List<string>();

            var dataRoot = ResolveDataDirectory(CurrentDirectory);
            if (!string.IsNullOrEmpty(dataRoot))
            {
                return System.IO.Directory.EnumerateDirectories(dataRoot)
                    .Where(IsNightDirectory)
                    .SelectMany(nightPath => System.IO.Directory.EnumerateDirectories(nightPath)
                        .Where(lightPath => string.Equals(System.IO.Path.GetFileName(lightPath), "LIGHT", StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return new System.Collections.Generic.List<string> { CurrentDirectory };
        }

        private static string? ResolveDataDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory))
                return null;

            if (string.Equals(System.IO.Path.GetFileName(directory), "Data", StringComparison.OrdinalIgnoreCase))
                return directory;

            return System.IO.Directory.EnumerateDirectories(directory, "*", System.IO.SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => string.Equals(System.IO.Path.GetFileName(path), "Data", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsNightDirectory(string directoryPath)
        {
            var name = System.IO.Path.GetFileName(directoryPath);
            if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("NIGHT_", StringComparison.OrdinalIgnoreCase))
                return false;

            var datePart = name.Substring("NIGHT_".Length);
            return DateTime.TryParseExact(datePart, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
        }

        private void LoadDirectoryReviewedState()
        {
            bool reviewed = false;
            var reviewDirectories = GetReviewedDirectories();

            if (reviewDirectories.Count > 0)
            {
                reviewed = reviewDirectories.All(directoryPath =>
                {
                    string markerPath = GetReviewedMarkerFilePath(directoryPath);
                    if (System.IO.File.Exists(markerPath))
                    {
                        return TryReadReviewedMarker(markerPath, out _);
                    }

                    string legacyMarkerPath = GetLegacyReviewedMarkerFilePath(directoryPath);
                    if (System.IO.File.Exists(legacyMarkerPath))
                    {
                        try
                        {
                            WriteReviewedMarker(markerPath);
                            System.IO.File.Delete(legacyMarkerPath);
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogError("Directory Review State", $"Failed to migrate legacy review marker in {directoryPath}", ex);
                        }

                        return true;
                    }

                    string legacyMarkerPathV1 = GetLegacyReviewedMarkerFilePathV1(directoryPath);
                    if (System.IO.File.Exists(legacyMarkerPathV1))
                    {
                        try
                        {
                            WriteReviewedMarker(markerPath);
                            System.IO.File.Delete(legacyMarkerPathV1);
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogError("Directory Review State", $"Failed to migrate legacy review marker in {directoryPath}", ex);
                        }

                        return true;
                    }

                    return false;
                });
            }

            _isLoadingDirectoryReviewedState = true;
            try
            {
                _isDirectoryReviewed = reviewed;
                OnPropertyChanged(nameof(IsDirectoryReviewed));
                OnPropertyChanged(nameof(DirectoryReviewedStatusText));
            }
            finally
            {
                _isLoadingDirectoryReviewedState = false;
            }
        }

        private void SaveDirectoryReviewedState()
        {
            if (_isLoadingDirectoryReviewedState)
                return;

            if (!CanMarkDirectoryReviewed)
                return;

            var reviewDirectories = GetReviewedDirectories();
            if (reviewDirectories.Count == 0)
                return;

            try
            {
                foreach (var directoryPath in reviewDirectories)
                {
                    string markerPath = GetReviewedMarkerFilePath(directoryPath);
                    if (IsDirectoryReviewed)
                    {
                        WriteReviewedMarker(markerPath);
                    }
                    else if (System.IO.File.Exists(markerPath))
                    {
                        System.IO.File.Delete(markerPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Directory Review State", $"Failed to persist review marker in {CurrentDirectory}", ex);
            }
        }

        private static void WriteReviewedMarker(string markerPath)
        {
            var marker = new ReviewedMarkerFile
            {
                Reviewed = true,
                ReviewedUtc = DateTime.UtcNow
            };

            string json = JsonSerializer.Serialize(marker, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            System.IO.File.WriteAllText(markerPath, json);
        }

        private static bool TryReadReviewedMarker(string markerPath, out ReviewedMarkerFile? marker)
        {
            marker = null;

            try
            {
                string json = System.IO.File.ReadAllText(markerPath);
                marker = JsonSerializer.Deserialize<ReviewedMarkerFile>(json);
                return marker?.Reviewed == true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class ReviewedMarkerFile
        {
            public bool Reviewed { get; set; }
            public DateTime ReviewedUtc { get; set; }
        }

        // Navigation commands - these handle moving through the file list
        public RelayCommand GotoFirstCommand { get; }     // Jump to first file
        public RelayCommand GotoPreviousCommand { get; }  // Go to previous file (wraps around)
        public RelayCommand PlayPauseCommand { get; }     // Toggle automatic playback
        public RelayCommand GotoNextCommand { get; }      // Go to next file (wraps around)
        public RelayCommand GotoLastCommand { get; }      // Jump to last file
        #endregion

        #region Collections and Events
        /// <summary>
        /// Collection of files currently loaded in the application.
        /// ObservableCollection is a special WPF collection that automatically notifies the UI
        /// when items are added, removed, or changed. This enables automatic UI updates
        /// when the file list changes. The collection is bound to the ListView in the UI.
        /// </summary>
        public ObservableCollection<FileItem> Files { get; } = new ObservableCollection<FileItem>();
        
        /// <summary>
        /// Current sort column for tracking sort state
        /// </summary>
        private string _currentSortColumn = "Name";
        
        /// <summary>
        /// Public property for the currently sorted column (read-only for View binding)
        /// </summary>
        public string? CurrentSortColumn => _currentSortColumn;
        
        /// <summary>
        /// Current sort direction for tracking sort state
        /// </summary>
        private ListSortDirection _currentSortDirection = ListSortDirection.Ascending;
        
        /// <summary>
        /// Public property for the current sort direction (read-only for View binding)
        /// </summary>
        public ListSortDirection? CurrentSortDirection => _currentSortDirection;
        
        /// <summary>
        /// Gets the count of files for command CanExecute evaluation
        /// </summary>
        public int FileCount => Files.Count;
        
        /// <summary>
        /// Sorts the file collection by the specified column.
        /// Automatically detects numeric vs text sorting and handles sort direction toggling.
        /// </summary>
        /// <param name="columnName">The property name or header text to sort by</param>
        public void SortByColumn(string columnName)
        {
            if (string.IsNullOrEmpty(columnName) || Files.Count == 0) return;
            
            // Toggle sort direction if clicking the same column
            if (_currentSortColumn == columnName)
            {
                _currentSortDirection = _currentSortDirection == ListSortDirection.Ascending 
                    ? ListSortDirection.Descending 
                    : ListSortDirection.Ascending;
            }
            else
            {
                _currentSortColumn = columnName;
                _currentSortDirection = ListSortDirection.Ascending;
            }

            ApplyCurrentSort();
        }

        private void ApplyCurrentSort()
        {
            if (string.IsNullOrEmpty(_currentSortColumn) || Files.Count == 0)
            {
                return;
            }

            var sortedFiles = SortFilesByColumn(Files.ToList(), _currentSortColumn, _currentSortDirection);

            // Clear and repopulate the ObservableCollection to maintain the same references
            // but in sorted order. This preserves selection and other UI state.
            for (int i = 0; i < sortedFiles.Count; i++)
            {
                var currentIndex = Files.IndexOf(sortedFiles[i]);
                if (currentIndex != i && currentIndex != -1)
                {
                    Files.Move(currentIndex, i);
                }
            }
        }
        
        private static List<FileItem> PrioritizeMarkedFiles(List<FileItem> files)
        {
            if (files.Count == 0 || !files.Any(f => f.IsSelected))
            {
                return files;
            }

            return files
                .OrderBy(f => f.IsSelected ? 0 : 1)
                .ToList();
        }

        /// <summary>
        /// Sorts a list of FileItem objects by the specified column
        /// </summary>
        private List<FileItem> SortFilesByColumn(List<FileItem> files, string columnName, ListSortDirection direction)
        {
            var orderedFiles = columnName switch
            {
                "File" => direction == ListSortDirection.Ascending 
                    ? files.OrderBy(f => GetDisplayFileName(f), StringComparer.OrdinalIgnoreCase).ToList()
                    : files.OrderByDescending(f => GetDisplayFileName(f), StringComparer.OrdinalIgnoreCase).ToList(),

                "Date" => direction == ListSortDirection.Ascending
                    ? files.OrderBy(f => f.FilenameDate, StringComparer.OrdinalIgnoreCase).ToList()
                    : files.OrderByDescending(f => f.FilenameDate, StringComparer.OrdinalIgnoreCase).ToList(),

                "Time" => direction == ListSortDirection.Ascending
                    ? files.OrderBy(f => f.FilenameTime, StringComparer.OrdinalIgnoreCase).ToList()
                    : files.OrderByDescending(f => f.FilenameTime, StringComparer.OrdinalIgnoreCase).ToList(),

                "Frame" => direction == ListSortDirection.Ascending
                    ? files.OrderBy(f => int.TryParse(f.FilenameFrame, out int frame) ? frame : int.MaxValue).ToList()
                    : files.OrderByDescending(f => int.TryParse(f.FilenameFrame, out int frame) ? frame : int.MinValue).ToList(),
                    
                "Size" => direction == ListSortDirection.Ascending 
                    ? files.OrderBy(f => f.Size).ToList()
                    : files.OrderByDescending(f => f.Size).ToList(),
                    
                "Median" => direction == ListSortDirection.Ascending 
                    ? files.OrderBy(f => f.Median ?? double.MaxValue).ToList()
                    : files.OrderByDescending(f => f.Median ?? double.MinValue).ToList(),
                    
                "Mean" => direction == ListSortDirection.Ascending 
                    ? files.OrderBy(f => f.Mean ?? double.MaxValue).ToList()
                    : files.OrderByDescending(f => f.Mean ?? double.MinValue).ToList(),

                "Mark" => direction == ListSortDirection.Ascending 
                    ? files.OrderBy(f => f.IsSelected).ThenBy(f => GetDisplayFileName(f), StringComparer.OrdinalIgnoreCase).ToList()
                    : files.OrderByDescending(f => f.IsSelected).ThenByDescending(f => GetDisplayFileName(f), StringComparer.OrdinalIgnoreCase).ToList(),
                    
                _ when _appConfig.CustomKeywords.Contains(columnName) =>
                    SortByDictionaryValue(files, f => f.CustomKeywords, columnName, direction),
                    
                _ when _appConfig.CsvKeywords.Contains(columnName) =>
                    SortByDictionaryValue(files, f => f.CsvKeywords, columnName, direction),

                _ when _appConfig.FitsKeywords.Contains(columnName) =>
                    SortByDictionaryValue(files, f => f.FitsKeywords, columnName, direction),

                _ when _appConfig.ArwKeywords.Contains(columnName) =>
                    SortByDictionaryValue(files, f => f.ArwKeywords, columnName, direction),
                    
                _ => files // No sorting for unknown columns
            };

            return PrioritizeMarkedFiles(orderedFiles);
        }
        
        /// <summary>
        /// Sorts files by a dictionary value, with smart numeric/text detection
        /// </summary>
        private List<FileItem> SortByDictionaryValue(List<FileItem> files, 
            Func<FileItem, Dictionary<string, string>> getDictionary, 
            string key, 
            ListSortDirection direction)
        {
            // Get all non-null values to determine if they're numeric
            var values = files
                .Select(f => getDictionary(f).TryGetValue(key, out string? value) ? value : null)
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();
                
            bool isNumeric = values.Count > 0 && values.All(v => double.TryParse(v, out _));
            
            if (isNumeric)
            {
                // Numeric sorting
                return direction == ListSortDirection.Ascending
                    ? files.OrderBy(f => 
                        {
                            var dict = getDictionary(f);
                            if (dict.TryGetValue(key, out string? value) && double.TryParse(value, out double num))
                                return num;
                            return double.MaxValue; // Put non-numeric values at the end
                        }).ToList()
                    : files.OrderByDescending(f => 
                        {
                            var dict = getDictionary(f);
                            if (dict.TryGetValue(key, out string? value) && double.TryParse(value, out double num))
                                return num;
                            return double.MinValue; // Put non-numeric values at the end
                        }).ToList();
            }
            else
            {
                // Text sorting
                return direction == ListSortDirection.Ascending
                    ? files.OrderBy(f => 
                        {
                            var dict = getDictionary(f);
                            return dict.TryGetValue(key, out string? value) ? value ?? "" : "";
                        }, StringComparer.OrdinalIgnoreCase).ToList()
                    : files.OrderByDescending(f => 
                        {
                            var dict = getDictionary(f);
                            return dict.TryGetValue(key, out string? value) ? value ?? "" : "";
                        }, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        /// <summary>
        /// Event required by INotifyPropertyChanged interface. This is how WPF knows
        /// when properties change so it can update bound UI elements automatically.
        /// The ? means this event can be null (no subscribers).
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;
        #endregion

        #region Dependency Injection Fields
        // These are the services injected through the constructor. They handle specific responsibilities:
        // This demonstrates the Dependency Injection pattern - instead of creating dependencies directly,
        // they are provided (injected) from outside, making the code more testable and flexible.
        
        private readonly FileManagementService _fileManagementService;           // Handles file system operations
        private readonly KeywordExtractionService _keywordExtractionService;     // Extracts metadata from files
        private readonly AppConfig _appConfig;                                   // Application configuration settings
        private readonly IFolderDialogService _folderDialogService;             // Shows folder selection dialogs
        private readonly IGeneralOptionsDialogService _generalOptionsDialogService; // Shows general options dialog
        private readonly ICustomKeywordsDialogService _customKeywordsDialogService; // Shows custom keywords dialog
        private readonly IFitsKeywordsDialogService _fitsKeywordsDialogService;     // Shows FITS keywords dialog
        private readonly IListViewColumnService _listViewColumnService;         // Manages ListView column visibility
        private readonly ILoggingService _loggingService;                       // Logging service for tracking actions and errors
        private readonly CsvMetadataService _csvMetadataService;               // Loads ImageMetaData.csv from the current folder
        private readonly AstroDataCacheService _astroDataCacheService;          // Caches median/mean values in AstroData.csv
        #endregion
        #region Main Application Commands
        // These commands handle the primary application functionality
        // Commands encapsulate user actions and can be bound to buttons, menu items, etc.
        public RelayCommand LoadFilesCommand { get; }                    // Load files from a directory
        public RelayCommand RefreshCustomKeywordsCommand { get; }        // Refresh custom keyword extraction
        public RelayCommand RefreshFitsKeywordsCommand { get; }          // Refresh FITS keyword extraction
        public RelayCommand OpenFolderDialogCommand { get; }             // Show folder selection dialog
        public RelayCommand OpenRecentFolderCommand { get; }             // Open a recent folder
        public RelayCommand OpenFilesCommand { get; }                    // Open specific files dialog
        public RelayCommand ShowGeneralOptionsDialogCommand { get; }     // Show general application options
        public RelayCommand ShowCustomKeywordsDialogCommand { get; }     // Show custom keywords configuration
        public RelayCommand ShowFitsKeywordsDialogCommand { get; }       // Show FITS keywords configuration
        public RelayCommand ShowFileMetadataCommand { get; }             // Show file metadata dialog
        public RelayCommand MoveSelectedCommand { get; }                 // Move selected files
        public RelayCommand EnterFullScreenCommand { get; }              // Enter full screen mode
        public RelayCommand ComputeSnrCommand { get; }                   // Compute temporal SNR for current folder
        #endregion
        #region Constructor
        /// <summary>
        /// Constructor that receives all dependencies through Dependency Injection.
        /// This is a core MVVM pattern - the ViewModel doesn't create its own dependencies,
        /// they are provided by a DI container (configured in App.xaml.cs).
        /// 
        /// Benefits of this approach:
        /// - Testability: Dependencies can be mocked for unit testing
        /// - Flexibility: Different implementations can be injected
        /// - Loose Coupling: ViewModel doesn't depend on concrete classes
        /// </summary>
        /// <param name="fileManagementService">Service for file system operations</param>
        /// <param name="keywordExtractionService">Service for extracting metadata from files</param>
        /// <param name="appConfig">Application configuration object</param>
        /// <param name="folderDialogService">Service for showing folder selection dialogs</param>
        /// <param name="generalOptionsDialogService">Service for general options dialog (nullable)</param>
        /// <param name="customKeywordsDialogService">Service for custom keywords dialog (nullable)</param>
        /// <param name="fitsKeywordsDialogService">Service for FITS keywords dialog (nullable)</param>
        /// <param name="listViewColumnService">Service for managing ListView columns</param>
        public MainWindowViewModel(
            FileManagementService fileManagementService,
            KeywordExtractionService keywordExtractionService,
            AppConfig appConfig,
            IFolderDialogService folderDialogService,
            IGeneralOptionsDialogService? generalOptionsDialogService,
            ICustomKeywordsDialogService? customKeywordsDialogService,
            IFitsKeywordsDialogService? fitsKeywordsDialogService,
            IListViewColumnService listViewColumnService,
            ILoggingService loggingService)
        {
            // Store injected dependencies for later use
            _fileManagementService = fileManagementService;
            _keywordExtractionService = keywordExtractionService;
            _appConfig = appConfig;
            _folderDialogService = folderDialogService;
            _loggingService = loggingService;
            _csvMetadataService = new CsvMetadataService();
            _astroDataCacheService = new AstroDataCacheService();
            
            // Initialize AutoStretch from configuration
            _autoStretch = _appConfig.AutoStretch;
            _stretchAggressiveness = _appConfig.StretchAggressiveness;
            
            // Use null-coalescing operator (??) to provide default implementations if none injected
            // This provides fallback behavior while still supporting dependency injection
            _generalOptionsDialogService = generalOptionsDialogService ?? new GeneralOptionsDialogService();
            _customKeywordsDialogService = customKeywordsDialogService ?? new CustomKeywordsDialogService();
            _fitsKeywordsDialogService = fitsKeywordsDialogService ?? new FitsKeywordsDialogService();
            _listViewColumnService = listViewColumnService;

            // Initialize commands using RelayCommand
            // Commands use lambda expressions (=>) for concise action definitions
            // The underscore (_) parameter means "we don't use this parameter"
            
            // This command accepts a string parameter (directory path) and loads files from it
            LoadFilesCommand = new RelayCommand(param =>
            {
                // Pattern matching: check if param is a string and not empty
                if (param is string dir && !string.IsNullOrEmpty(dir))
                {
                    LoadFiles(dir);
                }
            });

            // Simple commands that call methods without parameters
            RefreshCustomKeywordsCommand = new RelayCommand(_ => RefreshFileListKeywords());
            RefreshFitsKeywordsCommand = new RelayCommand(_ => RefreshFileListFitsKeywords());
            OpenFolderDialogCommand = new RelayCommand(_ => OpenFolderAndLoadFiles());
            OpenRecentFolderCommand = new RelayCommand(param => OpenRecentFolder(param));
            OpenFilesCommand = new RelayCommand(_ => OpenFilesRequested?.Invoke());
            ShowGeneralOptionsDialogCommand = new RelayCommand(_ => ShowGeneralOptionsDialog());
            ShowCustomKeywordsDialogCommand = new RelayCommand(_ => ShowCustomKeywordsDialog());
            ShowFitsKeywordsDialogCommand = new RelayCommand(_ => ShowFitsKeywordsDialog());
            ShowFileMetadataCommand = new RelayCommand(param => ShowFileMetadata(param));
            MoveSelectedCommand = new RelayCommand(_ => MoveSelectedFiles(), _ => HasSelectedFiles);
            EnterFullScreenCommand = new RelayCommand(_ => EnterFullScreen(), _ => SelectedIndex >= 0 && SelectedIndex < Files.Count);

            // Navigation commands with CanExecute predicates
            // The second parameter to RelayCommand is a "CanExecute" function that determines
            // when the command should be enabled. These are disabled when no files are loaded.
            GotoFirstCommand = new RelayCommand(_ => GotoFirst(), _ => FileCount > 0);
            GotoPreviousCommand = new RelayCommand(_ => GotoPrevious(), _ => FileCount > 0);
            PlayPauseCommand = new RelayCommand(_ => TogglePlayPause(), _ => FileCount > 0);
            GotoNextCommand = new RelayCommand(_ => GotoNext(), _ => FileCount > 0);
            GotoLastCommand = new RelayCommand(_ => GotoLast(), _ => FileCount > 0);
            ComputeSnrCommand = new RelayCommand(_ => ComputeSnrRequested?.Invoke(), _ => CanComputeSnr);
            
            // Zoom commands with inline logic using Math.Min/Max to constrain values
            ZoomInCommand = new RelayCommand(_ => ZoomLevel = Math.Min(ZoomLevel * 1.25, 5.0));  // Max 500%
            ZoomOutCommand = new RelayCommand(_ => ZoomLevel = Math.Max(ZoomLevel / 1.25, 0.1)); // Min 10%
            FitToWindowCommand = new RelayCommand(_ => FitToWindow());
            ResetZoomCommand = new RelayCommand(_ => ZoomLevel = 1.0);  // Reset to 100%
            ZoomActualCommand = new RelayCommand(_ => SetZoomActual());

            // Stretch commands
            IncreaseStretchCommand = new RelayCommand(_ => StretchAggressiveness++, _ => StretchAggressiveness < 10);
            DecreaseStretchCommand = new RelayCommand(_ => StretchAggressiveness--, _ => StretchAggressiveness > 0);
        }
        #endregion

        #region Zoom Control Methods
        /// <summary>
        /// Switches to fit-to-window mode and requests the UI to recalculate the fit.
        /// This demonstrates the ViewModel-View communication pattern using events.
        /// The ViewModel sets the state, then raises an event for the View to handle.
        /// </summary>
        private void FitToWindow()
        {
            FitMode = true;
            OnPropertyChanged(nameof(ZoomDisplayText));  // Update display text
            // Request the View (code-behind) to perform the actual fitting calculation
            // This is necessary because fitting requires knowledge of the actual UI dimensions
            FitRequested?.Invoke();
        }

        /// <summary>
        /// Event that notifies the View when a fit-to-window operation is requested.
        /// The View subscribes to this event and handles the actual dimension calculations.
        /// This follows the MVVM pattern of keeping UI-specific logic in the View.
        /// </summary>
        public event Action? FitRequested;

        /// <summary>
        /// Sets zoom to actual size (1:1 pixel ratio) and requests the UI to center the image.
        /// This switches from fit mode to manual zoom mode.
        /// </summary>
        private void SetZoomActual()
        {
            FitMode = false;
            ZoomLevel = 1.0;  // 100% zoom
            OnPropertyChanged(nameof(ZoomDisplayText));
            // Request the View to center the image at actual size
            ZoomActualRequested?.Invoke();
        }

        /// <summary>
        /// Event that notifies the View when actual size zoom is requested.
        /// The View handles centering the image in the viewport.
        /// </summary>
        public event Action? ZoomActualRequested;

        /// <summary>
        /// Event that notifies the View when files have been loaded from a directory.
        /// The View can use this to perform UI updates like auto-resizing columns.
        /// </summary>
        public event Action? FilesLoaded;

        /// <summary>
        /// Event that requests the View to load files with a progress dialog.
        /// The string parameter is the directory path to load.
        /// </summary>
        public event Action<string>? LoadFilesWithProgressRequested;

        /// <summary>
        /// Event that requests the View to show the open files dialog.
        /// </summary>
        public event Action? OpenFilesRequested;

        /// <summary>
        /// Event that requests the View to refresh custom keywords with a progress dialog.
        /// </summary>
        public event Action? RefreshCustomKeywordsWithProgressRequested;

        /// <summary>
        /// Event that requests the View to refresh FITS keywords with a progress dialog.
        /// </summary>
        public event Action? RefreshFitsKeywordsWithProgressRequested;

        /// <summary>
        /// Event that requests the View to calculate median/mean values with a progress dialog.
        /// </summary>
        public event Action? CalculateMediansWithProgressRequested;

        /// <summary>
        /// Event that requests the View to clear the currently displayed image.
        /// Used before moving/deleting files to release file handles.
        /// </summary>
        public event Action? ClearImageRequested;

        /// <summary>
        /// Event that requests the View to scroll the selected item into view in the file list.
        /// Used in play mode to keep the currently displayed file visible.
        /// </summary>
        public event Action? ScrollToSelectedRequested;

        /// <summary>
        /// Event that requests the View to enter full screen mode.
        /// The View handles this to create the FullScreenWindow with proper owner positioning.
        /// </summary>
        public event Action? EnterFullScreenRequested;

        /// <summary>
        /// Event that requests the View to compute temporal SNR for the current folder.
        /// </summary>
        public event Action? ComputeSnrRequested;

        /// <summary>
        /// Event that notifies the View when histogram visibility should change.
        /// The View handles this to show/hide the histogram panel.
        /// </summary>
        public event Action<bool>? HistogramVisibilityChanged;

        /// <summary>
        /// Called by the View when an image has finished rendering.
        /// Used for play mode to start the delay after image rendering is complete.
        /// </summary>
        public void OnImageRenderingCompleted()
        {
            if (IsPlaying && _awaitingImageRender)
            {
                _awaitingImageRender = false;
                StartPlayTimer();
            }
        }
        #endregion

        private void GotoFirst()
        {
            if (FileCount > 0)
                SelectedIndex = 0;
        }

        private void GotoPrevious()
        {
            if (FileCount == 0) return;
            if (SelectedIndex <= 0)
                SelectedIndex = FileCount - 1;
            else
                SelectedIndex--;
        }

        private void GotoNext()
        {
            if (FileCount == 0) return;
            if (SelectedIndex >= FileCount - 1)
                SelectedIndex = 0;
            else
                SelectedIndex++;
                
            // If we're in play mode, mark that we're waiting for image rendering to complete
            // and request the view to scroll the selected item into view
            if (IsPlaying)
            {
                _awaitingImageRender = true;
                // Request the view to scroll to the currently selected file
                ScrollToSelectedRequested?.Invoke();
            }
        }

        private void GotoLast()
        {
            if (FileCount > 0)
                SelectedIndex = FileCount - 1;
        }

        private void TogglePlayPause()
        {
            if (IsPlaying)
            {
                StopPlay();
            }
            else
            {
                StartPlay();
            }
        }

        private void StartPlay()
        {
            IsPlaying = true;
            // Start by immediately going to the next image
            // The timer will be started after the image finishes rendering
            GotoNext();
        }

        private void StartPlayTimer()
        {
            if (_playTimer == null)
            {
                _playTimer = new System.Timers.Timer
                {
                    AutoReset = false
                };
                _playTimer.Elapsed += (s, e) =>
                {
                    _playTimer.Stop(); // Stop the timer - it will be restarted after next image renders
                    App.Current.Dispatcher.Invoke(() => GotoNext());
                };
            }

            // Honor user-configured play interval (.25, .5, 1.0, etc.).
            // Keep a small floor to avoid pathological very-low values.
            _playTimer.Interval = Math.Max(50, _appConfig.PlayPauseInterval * 1000.0);
            _playTimer.Start();
        }

        private void StopPlay()
        {
            _playTimer?.Stop();
            IsPlaying = false;
            _awaitingImageRender = false;
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OpenFolderAndLoadFiles()
        {
            string initialDirectory = !string.IsNullOrEmpty(_appConfig.LastOpenDirectory) && System.IO.Directory.Exists(_appConfig.LastOpenDirectory)
                ? _appConfig.LastOpenDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var selectedFolder = _folderDialogService.ShowFolderDialog(initialDirectory);
            if (!string.IsNullOrEmpty(selectedFolder))
            {
                string resolvedFolder = ResolveFolderForImageLoad(selectedFolder);
                _appConfig.LastOpenDirectory = resolvedFolder;
                AddToRecentFolders(resolvedFolder);
                _appConfig.Save();
                
                // Request the View to load files with a progress dialog
                LoadFilesWithProgressRequested?.Invoke(resolvedFolder);
            }
        }
        
        /// <summary>
        /// Opens a folder from the recent folders list
        /// </summary>
        private void OpenRecentFolder(object? parameter)
        {
            if (parameter is string folderPath && System.IO.Directory.Exists(folderPath))
            {
                string resolvedFolder = ResolveFolderForImageLoad(folderPath);
                _appConfig.LastOpenDirectory = resolvedFolder;
                AddToRecentFolders(resolvedFolder);
                _appConfig.Save();
                
                // Request the View to load files with a progress dialog
                LoadFilesWithProgressRequested?.Invoke(resolvedFolder);
            }
        }

        private static string ResolveFolderForImageLoad(string folderPath)
        {
            return FileManagementService.ResolveDirectoryForLoad(folderPath);
        }

        private static bool ContainsSupportedFiles(string directory)
        {
            return FileManagementService.ResolveDirectoryForLoad(directory) == directory;
        }
        
        /// <summary>
        /// Adds a folder to the recent folders list, maintaining max 10 items with most recent first
        /// </summary>
        private void AddToRecentFolders(string folderPath)
        {
            // Remove if already exists (to move to top)
            _appConfig.RecentFolders.Remove(folderPath);
            
            // Add to beginning of list
            _appConfig.RecentFolders.Insert(0, folderPath);
            
            // Keep only last 10
            while (_appConfig.RecentFolders.Count > 10)
            {
                _appConfig.RecentFolders.RemoveAt(_appConfig.RecentFolders.Count - 1);
            }
            
            OnPropertyChanged(nameof(RecentFolders));
        }
        
        /// <summary>
        /// Gets the list of recent folders for binding to the menu
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<string> RecentFolders
        {
            get
            {
                // Filter out folders that no longer exist
                var existingFolders = _appConfig.RecentFolders.Where(f => System.IO.Directory.Exists(f)).ToList();
                
                // Update config if some folders were removed
                if (existingFolders.Count != _appConfig.RecentFolders.Count)
                {
                    _appConfig.RecentFolders = existingFolders;
                    _appConfig.Save();
                }
                
                return new System.Collections.ObjectModel.ObservableCollection<string>(existingFolders);
            }
        }
        private void ShowGeneralOptionsDialog()
        {
            var result = _generalOptionsDialogService.ShowGeneralOptionsDialog(
                _appConfig.ShowSizeColumn,
                _appConfig.ShowFullFilename,
                _appConfig.ShowFilenameDateColumn,
                _appConfig.ShowFilenameTimeColumn,
                _appConfig.ShowFilenameFrameColumn,
                _appConfig.ShowMedianColumn,
                _appConfig.Theme, 
                _appConfig.ShowFullScreenHelp,
                _appConfig.PlayPauseInterval,
                _appConfig.ScanXisfForFitsKeywords,
                _appConfig.MedianDisplayMode,
                _appConfig.ShowHistogram,
                _appConfig.HideUnusedMetadataColumns,
                _appConfig.FitsKeywords,
                _appConfig.CustomKeywords,
                _appConfig.CsvKeywords,
                _appConfig.ArwKeywords);
                
            if (result.showSizeColumn.HasValue)
            {
                _appConfig.ShowSizeColumn = result.showSizeColumn.Value;
                _appConfig.ShowFullFilename = result.showFullFilename ?? _appConfig.ShowFullFilename;
                _appConfig.ShowFilenameDateColumn = result.showFilenameDateColumn ?? _appConfig.ShowFilenameDateColumn;
                _appConfig.ShowFilenameTimeColumn = result.showFilenameTimeColumn ?? _appConfig.ShowFilenameTimeColumn;
                _appConfig.ShowFilenameFrameColumn = result.ShowFilenameFrameColumn ?? _appConfig.ShowFilenameFrameColumn;
                
                // Track if median column was just enabled
                bool shouldCalculateMedians = false;
                if (result.showMedianColumn.HasValue)
                {
                    bool wasEnabled = _appConfig.ShowMedianColumn;
                    _appConfig.ShowMedianColumn = result.showMedianColumn.Value;
                    
                    // If median column was just enabled, calculate medians for existing files
                    if (!wasEnabled && result.showMedianColumn.Value && Files.Count > 0)
                    {
                        shouldCalculateMedians = true;
                    }
                }

                if (result.theme.HasValue)
                {
                    _appConfig.Theme = result.theme.Value;
                    // Apply the new theme
                    ThemeService.SetThemeMode(result.theme.Value);
                }
                
                if (result.showFullScreenHelp.HasValue)
                {
                    _appConfig.ShowFullScreenHelp = result.showFullScreenHelp.Value;
                }
                
                if (result.playPauseInterval.HasValue)
                {
                    _appConfig.PlayPauseInterval = result.playPauseInterval.Value;
                }
                
                if (result.medianDisplayMode.HasValue)
                {
                    _appConfig.MedianDisplayMode = result.medianDisplayMode.Value;
                    // Trigger column refresh to update displayed values
                    _listViewColumnService?.UpdateListViewColumns();
                }
                
                if (result.showHistogram.HasValue)
                {
                    _appConfig.ShowHistogram = result.showHistogram.Value;
                    // Notify MainWindow to update histogram visibility
                    HistogramVisibilityChanged?.Invoke(_appConfig.ShowHistogram);
                }

                if (result.hideUnusedMetadataColumns.HasValue)
                {
                    _appConfig.HideUnusedMetadataColumns = result.hideUnusedMetadataColumns.Value;
                }
                
                if (result.scanXisfForFitsKeywords.HasValue)
                {
                    _appConfig.ScanXisfForFitsKeywords = result.scanXisfForFitsKeywords.Value;
                }
                
                // Handle keyword changes from sub-dialogs
                if (result.fitsKeywords != null)
                {
                    _appConfig.FitsKeywords = result.fitsKeywords;
                }
                
                if (result.customKeywords != null)
                {
                    _appConfig.CustomKeywords = result.customKeywords;
                    // Custom keywords come from filenames — re-extract immediately (instant)
                    RefreshFileListKeywords();
                }

                if (result.csvKeywords != null)
                {
                    _appConfig.CsvKeywords = result.csvKeywords;
                    // CSV data is already in memory — re-populate immediately (instant)
                    RefreshFileListCsvKeywords();
                }

                if (result.arwKeywords != null)
                {
                    _appConfig.ArwKeywords = result.arwKeywords;
                    // ARW metadata is extracted from ARW files only.
                    RefreshFileListArwKeywords();
                }
                
                _appConfig.Save();
                _listViewColumnService?.UpdateListViewColumns();
                // Auto-resize columns after configuration change
                _listViewColumnService?.AutoResizeColumns();

                // FITS keyword re-read requires reading file headers — trigger the progress-based
                // rescan after columns are already rebuilt so the values populate correctly.
                if (result.fitsKeywords != null)
                {
                    RefreshFitsKeywordsWithProgressRequested?.Invoke();
                }
                
                // Calculate medians for existing files if the option was just enabled
                if (shouldCalculateMedians)
                {
                    CalculateMediansWithProgressRequested?.Invoke();
                }
            }
        }

        public void CalculateMediansForAllFiles(Action<int, int>? progressCallback = null)
        {
            if (Files.Count == 0)
                return;

            var filesToProcess = Files.ToList();
            int total = filesToProcess.Count;
            int processedCount = 0;
            object progressLock = new object();

            _loggingService.LogInfo($"Calculating medians for {total} files...");

            // Calculate medians in parallel for better performance
            System.Threading.Tasks.Parallel.ForEach(
                filesToProcess,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                fileItem =>
                {
                    try
                    {
                        // Only calculate if not already calculated
                        if (!fileItem.Median.HasValue)
                        {
                            var (median, mean) = _keywordExtractionService.CalculateStatsForFile(fileItem.Path);
                            fileItem.Median = median;
                            fileItem.Mean = mean;
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.LogError("Median Calculation", $"Failed to calculate median for {fileItem.Name}: {ex.Message}", ex);
                    }
                    finally
                    {
                        lock (progressLock)
                        {
                            processedCount++;
                            if (processedCount % 5 == 0 || processedCount == total)
                            {
                                progressCallback?.Invoke(processedCount, total);
                            }
                        }
                    }
                });

            _loggingService.LogInfo($"Median calculation complete");

            // Persist updated stats to AstroData.csv
            if (!string.IsNullOrEmpty(CurrentDirectory))
                _astroDataCacheService.Save(CurrentDirectory, filesToProcess);
        }

        private void EnterFullScreen()
        {
            if (SelectedIndex < 0 || SelectedIndex >= Files.Count)
                return;

            // Raise event for View to handle - this allows the View to set the proper Owner
            EnterFullScreenRequested?.Invoke();
        }

        private void ShowCustomKeywordsDialog()
        {
            var result = _customKeywordsDialogService.ShowCustomKeywordsDialog(_appConfig.CustomKeywords);
            if (result != null)
            {
                _appConfig.CustomKeywords = result;
                _appConfig.Save();
                
                // Request the View to refresh with progress dialog
                // Column updates happen in the async handler after refresh completes
                RefreshCustomKeywordsWithProgressRequested?.Invoke();
            }
        }

        private void ShowFitsKeywordsDialog()
        {
            var result = _fitsKeywordsDialogService.ShowFitsKeywordsDialog(_appConfig.FitsKeywords);
            if (result != null)
            {
                _appConfig.FitsKeywords = result;
                _appConfig.Save();
                
                // Request the View to refresh with progress dialog
                // Column updates happen in the async handler after refresh completes
                RefreshFitsKeywordsWithProgressRequested?.Invoke();
            }
        }

        private void ShowFileMetadata(object? parameter)
        {
            if (parameter is FileItem fileItem)
            {
                // Show hourglass cursor while loading metadata dialog
                var originalCursor = System.Windows.Input.Mouse.OverrideCursor;
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                
                try
                {
                    // If XISF scanning was disabled and this is an XISF file, populate keywords now
                    if (!_appConfig.ScanXisfForFitsKeywords && 
                        (fileItem.Path.EndsWith(".xisf", StringComparison.OrdinalIgnoreCase)) &&
                        _appConfig.FitsKeywords.Any() &&
                        (fileItem.FitsKeywords == null || fileItem.FitsKeywords.Count == 0))
                    {
                        // Load FITS keywords from XISF file on-demand
                        _keywordExtractionService.PopulateKeywords(fileItem, Enumerable.Empty<string>(), _appConfig.FitsKeywords, skipXisf: false, calculateMedian: false, arwKeywords: _appConfig.ArwKeywords);
                    }
                    
                    var dialog = new FileMetadataDialog(fileItem);
                    
                    // Find the main window to set as owner
                    var mainWindow = System.Windows.Application.Current.MainWindow;
                    if (mainWindow != null)
                    {
                        dialog.Owner = mainWindow;
                    }
                    
                    // Restore cursor before showing dialog
                    System.Windows.Input.Mouse.OverrideCursor = originalCursor;
                    
                    dialog.ShowDialog();
                }
                catch
                {
                    // Ensure cursor is restored even if an error occurs
                    System.Windows.Input.Mouse.OverrideCursor = originalCursor;
                    throw;
                }
            }
        }

        /// <summary>
        /// Handles the Move Selected Files command by showing the move dialog and moving the selected files.
        /// </summary>
        private void MoveSelectedFiles()
        {
            var selectedFiles = Files.Where(f => f.IsSelected).ToList();
            if (!selectedFiles.Any())
                return;

            // Show the move files dialog
            var dialog = new MoveFilesDialog(selectedFiles.Select(f => f.Name).ToList());
            
            // Set default directory to the same directory as the first selected file
            if (selectedFiles.Count > 0)
            {
                var firstFileDir = System.IO.Path.GetDirectoryName(selectedFiles[0].Path);
                if (!string.IsNullOrEmpty(firstFileDir))
                {
                    dialog.SetDefaultDirectory(firstFileDir);
                }
            }

            // Find the main window to set as owner
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null)
            {
                dialog.Owner = mainWindow;
            }

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Check if the currently displayed image is one of the files being moved
                    // If so, clear the image to release the file handle before moving
                    if (SelectedIndex >= 0 && SelectedIndex < Files.Count)
                    {
                        var currentFile = Files[SelectedIndex];
                        if (selectedFiles.Contains(currentFile))
                        {
                            // Request the View to clear the displayed image
                            // This releases any file handles held by the image control
                            ClearImageRequested?.Invoke();
                        }
                    }
                    
                    _fileManagementService.MoveSelectedFiles(selectedFiles, dialog.TargetDirectory, dialog.MoveToTrash);
                    
                    // Remove moved files from the list
                    for (int i = Files.Count - 1; i >= 0; i--)
                    {
                        if (Files[i].IsSelected)
                        {
                            Files.RemoveAt(i);
                        }
                    }
                    
                    // Refresh the HasSelectedFiles property to update button state
                    OnPropertyChanged(nameof(HasSelectedFiles));
                    
                    // Refresh the StatusBarText to reset the selection count
                    OnPropertyChanged(nameof(StatusBarText));
                    
                    // Show a silent, auto-dismissing success notification that fades out after 3 seconds
                    ShowAutoClosingMessage($"Successfully moved {selectedFiles.Count} file(s).", "Move Complete");
                }
                catch (Exception ex)
                {
                    // No sound (None instead of Error) per user preference
                    System.Windows.MessageBox.Show($"Error moving files: {ex.Message}", 
                        "Move Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.None);
                }
            }
        }

        public void DeleteSelectedFilesToRecycleBin()
        {
            var selectedFiles = Files.Where(f => f.IsSelected).ToList();
            if (!selectedFiles.Any())
                return;

            try
            {
                if (SelectedIndex >= 0 && SelectedIndex < Files.Count)
                {
                    var currentFile = Files[SelectedIndex];
                    if (selectedFiles.Contains(currentFile))
                    {
                        ClearImageRequested?.Invoke();
                    }
                }

                _fileManagementService.MoveSelectedFiles(selectedFiles, string.Empty, true);

                for (int i = Files.Count - 1; i >= 0; i--)
                {
                    if (Files[i].IsSelected)
                    {
                        Files.RemoveAt(i);
                    }
                }

                OnPropertyChanged(nameof(HasSelectedFiles));
                OnPropertyChanged(nameof(FileSelectionStatusText));
                OnPropertyChanged(nameof(StatusBarText));

                ShowAutoClosingMessage($"Successfully deleted {selectedFiles.Count} file(s).", "Delete Complete");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error deleting files: {ex.Message}",
                    "Delete Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.None);
            }
        }

        /// <summary>
        /// Shows a silent, auto-dismissing message window that fades out after the specified duration.
        /// </summary>
        private static void ShowAutoClosingMessage(string message, string title, int displayMs = 1000, int fadeDurationMs = 600)
        {
            var window = new System.Windows.Window
            {
                Title = title,
                Width = 380,
                SizeToContent = System.Windows.SizeToContent.Height,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                WindowStyle = System.Windows.WindowStyle.ToolWindow,
                ShowInTaskbar = false,
                Topmost = true
            };

            window.Content = new System.Windows.Controls.TextBlock
            {
                Text = message,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new System.Windows.Thickness(20),
                FontSize = 14
            };

            // Fade-out animation starts (displayMs - fadeDurationMs) in and completes at displayMs
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0,
                new System.Windows.Duration(TimeSpan.FromMilliseconds(fadeDurationMs)))
            {
                BeginTime = TimeSpan.FromMilliseconds(displayMs - fadeDurationMs)
            };
            fadeOut.Completed += (s, e) => window.Close();

            window.Loaded += (s, e) => window.BeginAnimation(System.Windows.UIElement.OpacityProperty, fadeOut);

            window.ShowDialog();
        }

        /// <summary>
        /// Handles PropertyChanged events from FileItem objects to update HasSelectedFiles when IsSelected changes.
        /// </summary>
        private void FileItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileItem.IsSelected))
            {
                OnPropertyChanged(nameof(HasSelectedFiles));
                OnPropertyChanged(nameof(FileSelectionStatusText));
                OnPropertyChanged(nameof(StatusBarText));

                if (!string.IsNullOrEmpty(_currentSortColumn) && Files.Count > 0)
                {
                    // Defer the sort so it does not run while the auto-mark loop or any other
                    // enumeration is still iterating the same ObservableCollection.
                    if (System.Windows.Application.Current is not null)
                    {
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Background,
                            new Action(ApplyCurrentSort));
                    }
                    else
                    {
                        ApplyCurrentSort();
                    }
                }
            }
        }

        private string GetDisplayFileName(FileItem fileItem)
        {
            if (!string.IsNullOrWhiteSpace(fileItem.Path) &&
                System.IO.Path.GetExtension(fileItem.Path).Equals(".arw", StringComparison.OrdinalIgnoreCase))
            {
                return fileItem.Name;
            }

            return _appConfig.ShowFullFilename ? fileItem.Name : fileItem.NameWithoutExtension;
        }
    }
    
    /// <summary>
    /// Custom comparer for sorting dictionary-based columns (Custom Keywords and FITS Keywords)
    /// </summary>
    public class DictionarySortComparer : IComparer
    {
        private readonly string _columnName;
        private readonly ListSortDirection _direction;
        private readonly AppConfig _appConfig;
        
        public DictionarySortComparer(string columnName, ListSortDirection direction, AppConfig appConfig)
        {
            _columnName = columnName;
            _direction = direction;
            _appConfig = appConfig;
        }
        
        public int Compare(object? x, object? y)
        {
            if (x is not FileItem fileX || y is not FileItem fileY)
                return 0;
                
            string? valueX = GetColumnValue(fileX);
            string? valueY = GetColumnValue(fileY);
            
            // Handle nulls - put them at the end
            if (string.IsNullOrEmpty(valueX) && string.IsNullOrEmpty(valueY)) return 0;
            if (string.IsNullOrEmpty(valueX)) return _direction == ListSortDirection.Ascending ? 1 : -1;
            if (string.IsNullOrEmpty(valueY)) return _direction == ListSortDirection.Ascending ? -1 : 1;
            
            // Try numeric comparison first
            if (double.TryParse(valueX, out double numX) && double.TryParse(valueY, out double numY))
            {
                int result = numX.CompareTo(numY);
                return _direction == ListSortDirection.Ascending ? result : -result;
            }
            
            // Fall back to string comparison
            int stringResult = string.Compare(valueX, valueY, StringComparison.OrdinalIgnoreCase);
            return _direction == ListSortDirection.Ascending ? stringResult : -stringResult;
        }
        
        private string? GetColumnValue(FileItem fileItem)
        {
            if (_appConfig.CustomKeywords.Contains(_columnName))
            {
                return fileItem.CustomKeywords.TryGetValue(_columnName, out string? customValue) ? customValue : null;
            }
            
            if (_appConfig.FitsKeywords.Contains(_columnName))
            {
                return fileItem.FitsKeywords.TryGetValue(_columnName, out string? fitsValue) ? fitsValue : null;
            }
            
            return null;
        }
    }
}
