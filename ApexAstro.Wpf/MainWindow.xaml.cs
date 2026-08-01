using System.Windows;
using System.Linq;
using ApexAstro.Wpf.ViewModels;
using ApexAstro.Wpf.Services;
using ApexAstro.Wpf.Models;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ApexAstro.Wpf
{
    /// <summary>
    /// Main Window class - this is the primary application window that users interact with.
    /// Uses the MVVM (Model-View-ViewModel) pattern where:
    /// - View = MainWindow.xaml (the UI layout)
    /// - ViewModel = MainWindowViewModel (the logic and data)
    /// - Model = Various service classes and data models
    /// </summary>
    public partial class MainWindow : Window
    {
        // Private field to hold reference to the ViewModel
        // The ? means it can be null (nullable reference type)
        private MainWindowViewModel? _viewModel;

        // Private field to hold reference to the ListView column service
        // Used for auto-resizing columns when data changes
        private IListViewColumnService? _listViewColumnService;
        
        // Private field to store command-line file paths (if provided)
        private readonly string[]? _commandLineFilePaths;
        
        // Logging service for tracking user actions and errors
        private readonly ILoggingService _loggingService;
        
        // Application configuration
        private readonly AppConfig _appConfig;
        
        // Background warming task to pre-scan files and trigger antivirus
        private System.Threading.CancellationTokenSource? _warmupCancellation;

        // Playback prefetch cache (keyed by path + render settings)
        private readonly Dictionary<string, System.Windows.Media.Imaging.BitmapSource> _playbackImageCache = new();
        private readonly HashSet<string> _prefetchInProgress = new();
        private readonly object _playbackCacheLock = new();
        private System.Threading.CancellationTokenSource? _playbackPrefetchCancellation;

        // Floating windows for image and histogram
        private FloatingImageWindow? _floatingImageWindow;
        private FloatingHistogramWindow? _floatingHistogramWindow;
        private bool _isFloating = false;

        // Tracks the anchor row for shift-click checkbox range selection
        private int _lastCheckedIndex = -1;

        /// <summary>
        /// Constructor - called when creating a new MainWindow instance.
        /// This sets up all the services and connects the UI to the ViewModel.
        /// </summary>
        /// <param name="filePaths">Optional array of file paths from command-line arguments</param>
        public MainWindow(string[]? filePaths = null)
        {
            _commandLineFilePaths = filePaths;
            _loggingService = App.LoggingService;
            
            // Initialize WPF components from the XAML file
            // This must be called before accessing any UI elements
            InitializeComponent();

            // Load application configuration from settings file
            // AppConfig.Load() reads saved settings or creates defaults
            _appConfig = AppConfig.Load();
            
            // Restore window position and size from saved configuration
            RestoreWindowState(_appConfig);

            // Create all the service instances that the application needs
            // Services handle specific functionality (file management, dialogs, etc.)
            
            // Handles file system operations (loading folders, file info)
            var fileManagementService = new FileManagementService();
            
            // Extracts metadata from filenames (RMS, HFR values, etc.)
            var keywordExtractionService = new KeywordExtractionService();
            
            // Shows folder selection dialog to user
            var folderDialogService = new FolderDialogService();
            
            // Shows general options/settings dialog
            var generalOptionsDialogService = new GeneralOptionsDialogService();
            
            // Shows custom keywords configuration dialog
            var customKeywordsDialogService = new CustomKeywordsDialogService();
            
            // Shows FITS keywords configuration dialog
            var fitsKeywordsDialogService = new FitsKeywordsDialogService();

            // Create colored brushes for styling the ListView columns from theme resources
            var greenBrush = (System.Windows.Application.Current.TryFindResource("ThemeAccentGreen") as System.Windows.Media.Brush) 
                ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 128, 0)); // Fallback
            var blueBrush = (System.Windows.Application.Current.TryFindResource("ThemeAccentBlue") as System.Windows.Media.Brush)
                ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 192)); // Fallback
            
            // Service that manages ListView columns (adding, removing, styling)
            // Needs reference to the actual ListView control from XAML
            // Store as field so we can call auto-resize methods later
            _listViewColumnService = new ListViewColumnService(FileListView, _appConfig, greenBrush, blueBrush);

            // Create the ViewModel with all required services
            // This uses "dependency injection" - passing dependencies to the constructor
            _viewModel = new MainWindowViewModel(
                fileManagementService,
                keywordExtractionService,
                _appConfig,
                folderDialogService,
                generalOptionsDialogService,
                customKeywordsDialogService,
                fitsKeywordsDialogService,
                _listViewColumnService,
                _loggingService
            );

            // Connect the ViewModel to the UI through DataContext
            // This enables data binding - UI elements can bind to ViewModel properties
            this.DataContext = _viewModel;
            
            // Set up histogram control DataContext
            HistogramControl.DataContext = _viewModel.HistogramViewModel;

            // Connect the ListView to the Files collection in the ViewModel
            // ItemsSource tells the ListView where to get its data
            FileListView.ItemsSource = _viewModel.Files;

            // Initialize the ListView columns based on configuration
            _listViewColumnService.UpdateListViewColumns();
            
            // Show initial sort indicator for the default sort column
            _listViewColumnService.UpdateSortIndicators(_viewModel.CurrentSortColumn, _viewModel.CurrentSortDirection);
            
            // Auto-resize columns to fit content after initial setup
            _listViewColumnService.AutoResizeColumns();
            
            // Set initial histogram visibility based on configuration
            SetHistogramVisibility(_appConfig.ShowHistogram);

            // Wire up fit request event
            _viewModel.FitRequested += () => FitImageToScrollViewer();
            _viewModel.ZoomActualRequested += () => CenterImageAfterZoom();
            
            // Wire up files loaded event to auto-resize columns
            _viewModel.FilesLoaded += () => _listViewColumnService?.AutoResizeColumns();
            
            // Wire up auto-stretch changed event to refresh current image
            _viewModel.AutoStretchChanged += () =>
            {
                ClearPlaybackPrefetchCache();
                UpdateImageDisplay();
                QueuePlaybackPrefetch();
            };

            // Wire up load files with progress event
            _viewModel.LoadFilesWithProgressRequested += async (directoryPath) => 
                await LoadFilesWithProgressAsync(directoryPath, isStartup: false);

            // Wire up open files event
            _viewModel.OpenFilesRequested += () => OpenFiles_Click(this, new RoutedEventArgs());

            // Wire up refresh keywords with progress events
            _viewModel.RefreshCustomKeywordsWithProgressRequested += async () => 
                await RefreshCustomKeywordsWithProgressAsync();
            
            _viewModel.RefreshFitsKeywordsWithProgressRequested += async () => 
                await RefreshFitsKeywordsWithProgressAsync();

            // Wire up clear image event to release file handles
            _viewModel.ClearImageRequested += () => DisplayImage.Source = null;

            // Wire up scroll to selected event to keep current file visible in play mode
            _viewModel.ScrollToSelectedRequested += () =>
            {
                if (_viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < _viewModel.Files.Count)
                {
                    FileListView.ScrollIntoView(_viewModel.Files[_viewModel.SelectedIndex]);
                }
            };

            // Wire up enter full screen event to show full screen on the same monitor as main window
            _viewModel.EnterFullScreenRequested += () =>
            {
                _loggingService.LogFullscreenToggle(true);
                var fullScreenWindow = new FullScreenWindow(_viewModel.Files, _viewModel.SelectedIndex, _appConfig)
                {
                    Owner = this
                };
                fullScreenWindow.ShowDialog();
                _loggingService.LogFullscreenToggle(false);
                
                // Update the selected index to the last viewed image in full screen
                _viewModel.SelectedIndex = fullScreenWindow.CurrentIndex;
            };

            // Wire up histogram visibility changed event
            _viewModel.HistogramVisibilityChanged += (show) =>
            {
                SetHistogramVisibility(show);
            };

            // Wire up long operation detection to show progress dialogs
            _loggingService.LongOperationDetected += (sender, e) =>
            {
                var (operation, target) = e;
                ShowOperationProgressDialog(operation, target);
            };

            // Handle startup file loading
            if (_commandLineFilePaths != null && _commandLineFilePaths.Length > 0)
            {
                // User opened file(s) from Windows Explorer
                // Load the entire containing folder and select the clicked file
                Loaded += async (sender, e) =>
                {
                    await LoadFolderAndSelectFileAsync(_commandLineFilePaths[0]);
                    ShowImageCaptureFormatPromptIfNeeded();
                };
            }
            else
            {
                // No files passed from command line - show splash screen first, then open folder dialog
                Loaded += async (sender, e) =>
                {
                    // Show splash screen if enabled
                    ShowSplashScreenIfEnabled();
                    ShowImageCaptureFormatPromptIfNeeded();

                    // Brief delay to allow UI to process
                    await System.Threading.Tasks.Task.Delay(100);
                    
                    // Now show the folder dialog
                    if (_viewModel != null)
                    {
                        _viewModel.OpenFolderDialogCommand.Execute(null);
                    }
                };
            }

            // Wire up file selection to image display
            FileListView.SelectionChanged += FileListView_SelectionChanged;
            FileListView.PreviewKeyDown += FileListView_PreviewKeyDown;
            FileListView.PreviewMouseLeftButtonDown += FileListView_PreviewMouseLeftButtonDown;
            UpdateImageDisplay();

            // Handle pane resize for Fit mode
            ImageScrollViewer.SizeChanged += ImageScrollViewer_SizeChanged;
        }

        private void ShowImageCaptureFormatPromptIfNeeded()
        {
            if (_appConfig.ImageCaptureFormatPromptShown)
                return;

            var dialog = new ImageCaptureFormatDialog
            {
                Owner = this
            };

            dialog.ShowDialog();

            if (dialog.SelectedPreset.HasValue)
            {
                ApplySettingsPreset(dialog.SelectedPreset.Value);
            }

            _appConfig.ImageCaptureFormatPromptShown = true;
            _appConfig.Save();
        }

        private void ApplySettingsPreset(SettingsResetPreset preset)
        {
            switch (preset)
            {
                case SettingsResetPreset.Nina:
                    _appConfig.ShowFullFilename = false;
                    _appConfig.ShowFilenameDateColumn = false;
                    _appConfig.ShowFilenameTimeColumn = true;
                    _appConfig.ShowFilenameFrameColumn = true;
                    _appConfig.FitsKeywords = new List<string> { "EXPOSURE", "FILTER", "CCD-TEMP", "IMAGETYP" };
                    _appConfig.CustomKeywords = new List<string> { "RMS", "HFR", "ECC", "Stars" };
                    _appConfig.CsvKeywords = new List<string>();
                    break;

                case SettingsResetPreset.Asiair:
                    _appConfig.ShowFullFilename = true;
                    _appConfig.ShowFilenameDateColumn = false;
                    _appConfig.ShowFilenameTimeColumn = false;
                    _appConfig.ShowFilenameFrameColumn = false;
                    _appConfig.FitsKeywords = new List<string> { "EXPOSURE", "CCD_TEMP", "IMAGETYP" };
                    _appConfig.CustomKeywords = new List<string>();
                    _appConfig.CsvKeywords = new List<string>();
                    break;

                default:
                    break;
            }

            _listViewColumnService?.UpdateListViewColumns();
            _listViewColumnService?.AutoResizeColumns();
        }

        /// <summary>
        /// Loads the containing folder of a file and selects that file in the list.
        /// Called when user opens a file from Windows Explorer.
        /// </summary>
        private async Task LoadFolderAndSelectFileAsync(string filePath)
        {
            try
            {
                // Get the directory containing the file
                var directory = System.IO.Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(directory))
                    return;

                // Load all files from that directory
                await LoadFilesWithProgressAsync(directory, isStartup: true);

                // Select the clicked file
                if (_viewModel != null)
                {
                    var fileName = System.IO.Path.GetFileName(filePath);
                    var fileIndex = _viewModel.Files.FirstOrDefault(f => f.Name == fileName);
                    if (fileIndex != null)
                    {
                        var index = _viewModel.Files.IndexOf(fileIndex);
                        if (index >= 0)
                        {
                            _viewModel.SelectedIndex = index;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Startup File Loading", $"Failed to load folder and select file from command line arguments", ex);
            }
        }

        /// <summary>
        /// Opens a file or folder path forwarded from a secondary launch.
        /// If a file is provided, loads its directory and selects that file.
        /// If a directory is provided, loads that directory.
        /// </summary>
        public async Task OpenExternalPathAsync(string incomingPath)
        {
            if (string.IsNullOrWhiteSpace(incomingPath))
            {
                return;
            }

            var path = incomingPath.Trim().Trim('"');

            try
            {
                if (System.IO.File.Exists(path))
                {
                    await LoadFolderAndSelectFileAsync(path);
                    return;
                }

                if (System.IO.Directory.Exists(path))
                {
                    await LoadFilesWithProgressAsync(path, isStartup: false);
                    return;
                }

                // If path doesn't exist directly, try interpreting as a file path and use its directory.
                var directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && System.IO.Directory.Exists(directory))
                {
                    await LoadFilesWithProgressAsync(directory, isStartup: false);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Open External Path", $"Failed to open path '{path}'", ex);
            }
        }

        /// <summary>
        /// Loads files from a directory with a progress dialog
        /// </summary>
        private async Task LoadFilesWithProgressAsync(string directoryPath, bool isStartup = false)
        {
            LoadingWindow? loadingWindow = null;
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                _loggingService?.LogInfo($"=== Starting folder load: {System.IO.Path.GetFileName(directoryPath)} ===");
                
                // Show loading dialog on UI thread
                loadingWindow = new LoadingWindow(isStartup ? "Loading images..." : "Loading images in new folder...");
                loadingWindow.Owner = this;
                loadingWindow.Show();
                
                // Allow UI to update
                await Task.Delay(50);
                
                // Reset shift-click anchor whenever a new folder is loaded
                _lastCheckedIndex = -1;

                // Step 1: Load file list (fast - just filenames)
                loadingWindow.UpdateMessage("Scanning directory for image files...");
                var step1Start = totalStopwatch.ElapsedMilliseconds;
                System.Collections.Generic.List<Models.FileItem>? loadedFiles = null;
                
                await Task.Run(() =>
                {
                    if (_viewModel != null)
                    {
                        loadedFiles = _viewModel.LoadFiles(directoryPath);
                    }
                });
                
                var step1Time = totalStopwatch.ElapsedMilliseconds - step1Start;
                _loggingService?.LogInfo($"Phase 1 (File scan): {step1Time}ms for {loadedFiles?.Count ?? 0} files");
                
                // Step 2: Clear old list and show filenames immediately on UI thread
                loadingWindow.UpdateMessage($"Loading {loadedFiles?.Count ?? 0} files...");
                var step2Start = totalStopwatch.ElapsedMilliseconds;
                if (_viewModel != null && loadedFiles != null)
                {
                    _viewModel.UpdateFilesCollection(loadedFiles);
                    
                    // Select first image if available
                    if (_viewModel.Files.Count > 0)
                    {
                        _viewModel.SelectedIndex = 0;
                    }
                    
                    // Auto-resize columns
                    _listViewColumnService?.AutoResizeColumns();
                }
                var step2Time = totalStopwatch.ElapsedMilliseconds - step2Start;
                _loggingService?.LogInfo($"Phase 2 (UI update): {step2Time}ms");
                
                // Step 3: Populate metadata in background (slow - reads file headers)
                var step3Start = totalStopwatch.ElapsedMilliseconds;
                await Task.Run(() =>
                {
                    if (_viewModel != null && loadedFiles != null)
                    {
                        _viewModel.PopulateMetadataAsync(loadedFiles, (current, total) =>
                        {
                            // Marshal progress update to UI thread
                            Dispatcher.Invoke(() =>
                            {
                                loadingWindow.UpdateProgress(current, total);
                            });
                        });
                    }
                });
                var step3Time = totalStopwatch.ElapsedMilliseconds - step3Start;
                _loggingService?.LogInfo($"Phase 3 (Metadata extraction): {step3Time}ms");
                
                // Refresh the UI to show updated metadata
                loadingWindow.UpdateMessage("Finalizing...");
                var step4Start = totalStopwatch.ElapsedMilliseconds;
                if (_listViewColumnService != null)
                {
                    _listViewColumnService.AutoResizeColumns();
                }
                var step4Time = totalStopwatch.ElapsedMilliseconds - step4Start;
                _loggingService?.LogInfo($"Phase 4 (Column resize): {step4Time}ms");
                
                var totalTime = totalStopwatch.ElapsedMilliseconds;
                _loggingService?.LogInfo($"=== Total folder load time: {totalTime}ms ({totalTime / 1000.0:F1}s) ===");
                
                // Start background warming to pre-trigger antivirus scans
                _ = WarmupFilesAsync(loadedFiles);
            }
            finally
            {
                // Always close the loading window
                loadingWindow?.Close();
                
                // Show splash screen after initial file load (if enabled and this is startup)
                if (isStartup)
                {
                    ShowSplashScreenIfEnabled();
                }
            }
        }

        /// <summary>
        /// Shows a progress dialog for long-running file system operations (>3 seconds).
        /// This provides user feedback so they know the application is working on something.
        /// </summary>
        private void ShowOperationProgressDialog(string operation, string target)
        {
            try
            {
                var message = $"{operation}: {target}";
                var loadingWindow = new LoadingWindow(message);
                loadingWindow.Owner = this;
                loadingWindow.Show();

                // Keep the dialog visible for at least 1 second, then close it
                // (In case the operation finishes very quickly after logging)
                var closeTask = Task.Delay(1000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            loadingWindow.Close();
                        }
                        catch
                        {
                            // Window may have already been closed by other means
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                // Silently fail if we can't show the dialog
                _loggingService.LogError("Progress Dialog", $"Failed to show operation progress dialog for {operation}", ex);
            }
        }
        
        /// <summary>
        /// Warms up files in the background by reading them to trigger Windows Defender scans.
        /// This makes subsequent image loads instant since antivirus has already scanned them.
        /// Uses parallel processing for faster warmup.
        /// </summary>
        private async Task WarmupFilesAsync(System.Collections.Generic.List<Models.FileItem>? files)
        {
            if (files == null || files.Count == 0)
                return;
            
            // Cancel any existing warmup
            _warmupCancellation?.Cancel();
            _warmupCancellation = new System.Threading.CancellationTokenSource();
            var token = _warmupCancellation.Token;
            
            await Task.Run(() =>
            {
                _loggingService.LogInfo($"Starting background file warmup for {files.Count} files");
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                int warmedCount = 0;
                object progressLock = new object();
                
                // Filter to only FITS and XISF files (the large ones that trigger scans)
                var filesToWarmup = files.Where(f =>
                {
                    var ext = System.IO.Path.GetExtension(f.Path).ToLowerInvariant();
                    return ext == ".fits" || ext == ".fit" || ext == ".fts" || ext == ".xisf";
                }).ToList();
                
                // Process files in parallel using multiple threads
                // Use more threads since we're I/O bound (waiting on Windows Defender scans)
                System.Threading.Tasks.Parallel.ForEach(filesToWarmup,
                    new System.Threading.Tasks.ParallelOptions 
                    { 
                        MaxDegreeOfParallelism = Math.Min(20, Environment.ProcessorCount * 3),
                        CancellationToken = token
                    },
                    file =>
                    {
                        try
                        {
                            // Read the entire file to trigger Windows Defender scan
                            using (var fileStream = new System.IO.FileStream(file.Path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read, 81920, System.IO.FileOptions.SequentialScan))
                            {
                                byte[] buffer = new byte[81920];
                                while (fileStream.Read(buffer, 0, buffer.Length) > 0)
                                {
                                    if (token.IsCancellationRequested)
                                        break;
                                }
                            }
                            
                            lock (progressLock)
                            {
                                warmedCount++;
                                
                                // Log progress every 10 files
                                if (warmedCount % 10 == 0)
                                {
                                    _loggingService.LogInfo($"Background warmup: {warmedCount}/{filesToWarmup.Count} files ({stopwatch.ElapsedMilliseconds}ms)");
                                }
                            }
                        }
                        catch (System.OperationCanceledException)
                        {
                            // Expected when cancelled
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogError("File Warmup", $"Failed to warm up {file.Name}", ex);
                        }
                    });
                
                if (!token.IsCancellationRequested)
                {
                    _loggingService.LogInfo($"Background warmup completed: {warmedCount} files in {stopwatch.ElapsedMilliseconds}ms");
                }
                else
                {
                    _loggingService.LogInfo($"Background warmup cancelled after {warmedCount} files");
                }
            }, token);
        }

        /// <summary>
        /// Loads specific files (from command-line arguments) with a progress dialog
        /// </summary>
        private async Task LoadSpecificFilesWithProgressAsync(string[] filePaths)
        {
            LoadingWindow? loadingWindow = null;
            
            try
            {
                // Show loading dialog on UI thread
                loadingWindow = new LoadingWindow($"Loading {filePaths.Length} file{(filePaths.Length != 1 ? "s" : "")}...");
                loadingWindow.Owner = this;
                loadingWindow.Show();
                
                // Allow UI to update
                await Task.Delay(50);
                
                // Run file loading on background thread
                System.Collections.Generic.List<Models.FileItem>? loadedFiles = null;
                
                await Task.Run(() =>
                {
                    if (_viewModel != null)
                    {
                        // LoadSpecificFiles will be called on background thread, but progress callback
                        // needs to update UI, so we marshal it to the UI thread
                        loadedFiles = _viewModel.LoadSpecificFiles(filePaths, (current, total) =>
                        {
                            // Marshal progress update to UI thread
                            Dispatcher.Invoke(() =>
                            {
                                loadingWindow.UpdateProgress(current, total);
                            });
                        });
                    }
                });
                
                // Update ObservableCollection on UI thread
                if (_viewModel != null && loadedFiles != null)
                {
                    _viewModel.UpdateSpecificFilesCollection(loadedFiles);
                }
                
                // Select first file so its image is visible
                if (_viewModel != null && _viewModel.Files.Count > 0)
                {
                    _viewModel.SelectedIndex = 0;
                }
                
                // Auto-resize columns after loading files
                _listViewColumnService?.UpdateListViewColumns();
                _listViewColumnService?.AutoResizeColumns();
            }
            finally
            {
                // Always close the loading window
                loadingWindow?.Close();
            }
        }

        /// <summary>
        /// Shows the splash screen if enabled in configuration
        /// </summary>
        private void ShowSplashScreenIfEnabled()
        {
            if (_appConfig.ShowSplashScreen)
            {
                // Create the splash screen window with current setting
                var splash = new SplashWindow(_appConfig.ShowSplashScreen, _appConfig);
                
                // Set the main window as the "owner" - this makes the splash appear on top
                // and centers it relative to the main window
                splash.Owner = this;
                
                // ShowDialog() makes it modal - user must interact with splash before continuing
                // Returns true if user clicked OK, false if they cancelled or closed it
                var result = splash.ShowDialog();
                
                // If user clicked OK, save the "Don't show again" preference
                if (result == true)
                {
                    try
                    {
                        // Update configuration based on checkbox state
                        // If checked, disable splash screen for next time
                        // If unchecked, keep showing it
                        _appConfig.ShowSplashScreen = !splash.DontShowAgain;
                        _appConfig.Save();
                    }
                    catch
                    {
                        // If we can't save config, just ignore it
                        // Don't crash the app over a preference setting
                    }
                }
            }
        }

        /// <summary>
        /// Refreshes custom keywords with a progress dialog
        /// </summary>
        private async Task RefreshCustomKeywordsWithProgressAsync()
        {
            LoadingWindow? loadingWindow = null;
            
            try
            {
                // Show loading dialog on UI thread
                loadingWindow = new LoadingWindow("Refreshing custom keywords...");
                loadingWindow.Owner = this;
                loadingWindow.Show();
                
                // Allow UI to update
                await Task.Delay(50);
                
                // Run refresh on background thread
                await Task.Run(() =>
                {
                    if (_viewModel != null)
                    {
                        // Use Dispatcher to update UI thread
                        Dispatcher.Invoke(() =>
                        {
                            _viewModel.RefreshFileListKeywords((current, total) =>
                            {
                                // Update progress bar on UI thread
                                loadingWindow.UpdateProgress(current, total);
                                
                                // Force UI to update by processing events at background priority
                                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                            });
                        });
                    }
                });
                
                // Update columns and auto-resize after refresh is complete
                _listViewColumnService?.UpdateListViewColumns();
                _listViewColumnService?.AutoResizeColumns();
            }
            finally
            {
                // Always close the loading window
                loadingWindow?.Close();
            }
        }

        /// <summary>
        /// Refreshes FITS keywords with a progress dialog
        /// </summary>
        private async Task RefreshFitsKeywordsWithProgressAsync()
        {
            LoadingWindow? loadingWindow = null;
            
            try
            {
                // Show loading dialog on UI thread
                loadingWindow = new LoadingWindow("Refreshing FITS keywords...");
                loadingWindow.Owner = this;
                loadingWindow.Show();
                
                // Allow UI to update
                await Task.Delay(50);
                
                // Run refresh on background thread
                await Task.Run(() =>
                {
                    if (_viewModel != null)
                    {
                        // Use Dispatcher to update UI thread
                        Dispatcher.Invoke(() =>
                        {
                            _viewModel.RefreshFileListFitsKeywords((current, total) =>
                            {
                                // Update progress bar on UI thread
                                loadingWindow.UpdateProgress(current, total);
                                
                                // Force UI to update by processing events at background priority
                                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                            });
                        });
                    }
                });
                
                // Update columns and auto-resize after refresh is complete
                _listViewColumnService?.UpdateListViewColumns();
                _listViewColumnService?.AutoResizeColumns();
            }
            finally
            {
                // Always close the loading window
                loadingWindow?.Close();
            }
        }

        private void ImageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_viewModel != null && _viewModel.FitMode && DisplayImage.Source != null)
            {
                // Use a timer to avoid too many rapid updates
                _fitTimer?.Stop();
                _fitTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _fitTimer.Tick += (s, args) =>
                {
                    _fitTimer.Stop();
                    FitImageToScrollViewer();
                };
                _fitTimer.Start();
            }
        }

        private System.Windows.Threading.DispatcherTimer? _fitTimer;

        private void FileListView_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Show loading cursor when user selects a file
            this.Cursor = System.Windows.Input.Cursors.Wait;
            
            UpdateImageDisplay();
            if (_viewModel != null && _viewModel.FitMode)
                CenterImageInScrollViewer();

            QueuePlaybackPrefetch();
        }

        /// <summary>
        /// Handles keyboard input in the file list to toggle checkbox selection with spacebar.
        /// </summary>
        /// <summary>
        /// Handles shift+click on the Mark checkbox to select a range of files.
        /// </summary>
        private void FileListView_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_viewModel == null) return;

            // Walk up the visual tree from the clicked element to see if it is (inside) a CheckBox
            var source = e.OriginalSource as System.Windows.DependencyObject;
            bool isOnCheckbox = false;
            var walk = source;
            while (walk != null && !ReferenceEquals(walk, FileListView))
            {
                if (walk is System.Windows.Controls.CheckBox) { isOnCheckbox = true; break; }
                walk = System.Windows.Media.VisualTreeHelper.GetParent(walk);
            }
            if (!isOnCheckbox) return;

            // Find the containing ListViewItem
            walk = source;
            System.Windows.Controls.ListViewItem? lvi = null;
            while (walk != null && !ReferenceEquals(walk, FileListView))
            {
                if (walk is System.Windows.Controls.ListViewItem item) { lvi = item; break; }
                walk = System.Windows.Media.VisualTreeHelper.GetParent(walk);
            }
            if (lvi == null) return;

            int clickedIndex = FileListView.ItemContainerGenerator.IndexFromContainer(lvi);
            if (clickedIndex < 0) return;

            bool shiftHeld = System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftShift)
                          || System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RightShift);

            if (shiftHeld && _lastCheckedIndex >= 0 && _lastCheckedIndex != clickedIndex)
            {
                // Toggle state is the opposite of the clicked item's current state
                bool newState = !_viewModel.Files[clickedIndex].IsSelected;
                int start = Math.Min(_lastCheckedIndex, clickedIndex);
                int end   = Math.Max(_lastCheckedIndex, clickedIndex);
                for (int i = start; i <= end; i++)
                    _viewModel.Files[i].IsSelected = newState;
                // Prevent the CheckBox from also toggling just the one clicked item
                e.Handled = true;
                return;
            }

            // No shift (or no anchor yet) — record this as the new anchor after the click proceeds
            _lastCheckedIndex = clickedIndex;
        }

        private void FileListView_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Space)
            {
                // Get the currently selected item
                if (_viewModel != null && _viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < _viewModel.Files.Count)
                {
                    var selectedFile = _viewModel.Files[_viewModel.SelectedIndex];
                    selectedFile.IsSelected = !selectedFile.IsSelected;
                    
                    // Mark the event as handled so it doesn't cause other actions
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// Updates the image display based on the currently selected file in the ListView.
        /// This method handles the complete process of displaying an image:
        /// 1. Validates that we have a valid selection
        /// 2. Loads and renders the image file (including FITS processing)
        /// 3. Sets up the zoom/fit mode
        /// 4. Handles error cases gracefully
        /// 
        /// This is a key method in the image viewing workflow and is called whenever
        /// the user selects a different file in the list.
        /// </summary>
        private void UpdateImageDisplay()
        {
            System.Diagnostics.Debug.WriteLine($"UpdateImageDisplay called, StretchAggressiveness={_appConfig.StretchAggressiveness}");
            
            // Validate that we have a valid selection
            // Check for: null ViewModel, no selection (-1), or selection out of bounds
            if (_viewModel == null || _viewModel.SelectedIndex < 0 || _viewModel.SelectedIndex >= _viewModel.Files.Count)
            {
                // No valid selection - hide the image and show placeholder text
                DisplayImage.Source = null;                        // Clear any existing image
                ImageScrollViewer.Visibility = Visibility.Collapsed;  // Hide the image viewer
                PlaceholderText.Visibility = Visibility.Visible;      // Show "No image selected" text
                
                // Clear histogram
                _viewModel?.HistogramViewModel.ClearHistogram();
                
                // Restore normal cursor since no image processing is needed
                this.Cursor = System.Windows.Input.Cursors.Arrow;
                return; // Exit early since there's nothing to display
            }

            // Capture current zoom state before loading the new image so it can be restored
            bool previousFitMode = _viewModel.FitMode;
            double previousZoomLevel = _viewModel.ZoomLevel;
            double previousFitScale = _viewModel.FitScale;

            // Get the currently selected file from the ViewModel's Files collection
            var file = _viewModel.Files[_viewModel.SelectedIndex];
            
            _loggingService.LogFileOpened(file.Name);
            
            // Verify the file still exists on disk (user might have moved/deleted it)
            if (System.IO.File.Exists(file.Path))
            {
                try
                {
                    // Use our custom FITS renderer to load and process the image
                    // This handles both FITS files and standard image formats (JPEG, PNG, etc.)
                    var cacheKey = BuildPlaybackCacheKey(file.Path);
                    var image = TryGetCachedPlaybackImage(cacheKey, out var cachedImage)
                        ? cachedImage
                        : FitsImageRenderer.RenderFitsFile(file.Path, _viewModel.AutoStretch, _appConfig.StretchAggressiveness);
                
                    if (image != null)
                    {
                        // Ensure image can be shared safely across threads and cached.
                        if (image.CanFreeze && !image.IsFrozen)
                        {
                            image.Freeze();
                        }

                        CachePlaybackImage(cacheKey, image);

                        // Successfully loaded the image - now display it
                        
                        // Set the image source to our loaded bitmap
                        DisplayImage.Source = image;
                        
                        // Update floating window if it exists
                        if (_isFloating && _floatingImageWindow != null)
                        {
                            _floatingImageWindow.UpdateImage(image);
                        }
                        
                        // Show the image immediately
                        ImageScrollViewer.Visibility = Visibility.Visible;
                        PlaceholderText.Visibility = Visibility.Collapsed;

                        // Force layout update to ensure ScrollViewer is properly sized
                        this.UpdateLayout();
                        
                        // Use a small delay to ensure the UI is fully rendered before calculating fit scale
                        // This helps avoid timing issues where the ScrollViewer might not be sized yet
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (previousFitMode || DisplayImage.Source == null)
                            {
                                // Was in fit mode (or first image): fit the new image to the window
                                _viewModel.FitMode = true;
                                FitImageToScrollViewer();
                            }
                            else
                            {
                                // Was in manual zoom: restore the same zoom ratio relative to the
                                // previous fit scale so the image appears at the same visual size.
                                // If fit scales differ (different image dimensions) we adjust
                                // proportionally so the zoom feels consistent.
                                CalculateAndApplyFitScale();
                                double newFitScale = _viewModel.FitScale;
                                double restoredZoom = (previousFitScale > 0.0001)
                                    ? previousZoomLevel * (newFitScale / previousFitScale)
                                    : previousZoomLevel;
                                restoredZoom = Math.Max(0.01, Math.Min(5.0, restoredZoom));
                                _viewModel.FitMode = false;
                                _viewModel.ZoomLevel = restoredZoom;
                                CenterImageInScrollViewer();
                            }
                            System.Diagnostics.Debug.WriteLine($"Image loaded, fitMode={_viewModel.FitMode}, zoom={_viewModel.ZoomLevel}");
                            
                            // Generate histogram data
                            GenerateHistogramForCurrentImage();
                            
                            // Restore normal cursor after image rendering is complete
                            this.Cursor = System.Windows.Input.Cursors.Arrow;
                            
                            // Notify the ViewModel that image rendering is complete
                            _viewModel?.OnImageRenderingCompleted();

                            // Keep prefetch window centered around the current selection.
                            QueuePlaybackPrefetch();
                        }), System.Windows.Threading.DispatcherPriority.Loaded);
                        
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.LogError("Image Rendering", $"Failed to render {file.Name}", ex);
                    // Fall through to show error state
                }
            }
            else
            {
                _loggingService.LogWarning("File Access", $"File not found: {file.Path}");
            }
            
            // If failed to load
            DisplayImage.Source = null;
            ImageScrollViewer.Visibility = Visibility.Collapsed;
            PlaceholderText.Visibility = Visibility.Visible;
            
            // Clear histogram
            _viewModel.HistogramViewModel.ClearHistogram();
            
            // Restore normal cursor after failed image loading
            this.Cursor = System.Windows.Input.Cursors.Arrow;
        }

        private void GenerateHistogramForCurrentImage()
        {
            if (_viewModel == null || DisplayImage.Source == null)
                return;

            // Get the currently selected file
            if (_viewModel.SelectedIndex < 0 || _viewModel.SelectedIndex >= _viewModel.Files.Count)
                return;

            var currentFile = _viewModel.Files[_viewModel.SelectedIndex];
            if (!System.IO.File.Exists(currentFile.Path))
                return;

            try
            {
                var histogramService = new HistogramService();

                // For FITS and XISF files, generate histogram from raw data (before stretching)
                if (ApexAstro.Core.FitsUtilities.IsFitsFile(currentFile.Path))
                {
                    GenerateHistogramFromFitsFile(currentFile.Path, histogramService);
                }
                else if (ApexAstro.Utils.XisfUtilities.IsXisfFile(currentFile.Path))
                {
                    GenerateHistogramFromXisfFile(currentFile.Path, histogramService);
                }
                else
                {
                    // For standard images, use the rendered bitmap
                    GenerateHistogramFromBitmap(histogramService);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Histogram Generation", $"Failed to generate histogram: {ex.Message}", ex);
                _viewModel.HistogramViewModel.ClearHistogram();
            }
        }

        private void GenerateHistogramFromFitsFile(string filePath, HistogramService histogramService)
        {
            // Read the raw FITS data
            byte[] bytes = System.IO.File.ReadAllBytes(filePath);
            if (!ApexAstro.Core.FitsUtilities.IsFitsData(bytes))
                return;

            var (width, height, pixels) = ApexAstro.Core.FitsParser.ReadImage(bytes);
            
            // Generate histogram from raw pixel data (before any stretching)
            var histogram = histogramService.GenerateRawHistogram(pixels);
            _viewModel?.HistogramViewModel.UpdateHistogram(histogram);
        }

        private void GenerateHistogramFromXisfFile(string filePath, HistogramService histogramService)
        {
            // Read the raw XISF data
            byte[] bytes = System.IO.File.ReadAllBytes(filePath);
            var header = ApexAstro.Utils.XisfParser.ParseMetadata(bytes);

            if (header.ContainsKey("Channels") && (int)header["Channels"] == 3)
            {
                // RGB image - read as RGB
                var (width, height, rgbPixels) = ApexAstro.Utils.XisfParser.ReadImageRgb(bytes);
                
                // Split RGB data into separate channels
                int pixelCount = width * height;
                byte[] redPixels = new byte[pixelCount];
                byte[] greenPixels = new byte[pixelCount];
                byte[] bluePixels = new byte[pixelCount];
                
                for (int i = 0; i < pixelCount; i++)
                {
                    redPixels[i] = rgbPixels[i * 3];
                    greenPixels[i] = rgbPixels[i * 3 + 1];
                    bluePixels[i] = rgbPixels[i * 3 + 2];
                }
                
                var red = histogramService.GenerateRawHistogram(redPixels);
                var green = histogramService.GenerateRawHistogram(greenPixels);
                var blue = histogramService.GenerateRawHistogram(bluePixels);
                _viewModel?.HistogramViewModel.UpdateHistogram(red, green, blue);
            }
            else
            {
                // Grayscale image
                var (width, height, pixels) = ApexAstro.Utils.XisfParser.ReadImage(bytes);
                var histogram = histogramService.GenerateRawHistogram(pixels);
                _viewModel?.HistogramViewModel.UpdateHistogram(histogram);
            }
        }

        private void GenerateHistogramFromBitmap(HistogramService histogramService)
        {
            var bitmap = DisplayImage.Source as System.Windows.Media.Imaging.BitmapSource;
            if (bitmap == null)
                return;

            bool isGrayscale = histogramService.IsGrayscaleImage(bitmap);

            if (isGrayscale)
            {
                var grayHistogram = histogramService.GenerateGrayscaleHistogram(bitmap);
                _viewModel?.HistogramViewModel.UpdateHistogram(grayHistogram);
            }
            else
            {
                var (red, green, blue) = histogramService.GenerateRgbHistogram(bitmap);
                _viewModel?.HistogramViewModel.UpdateHistogram(red, green, blue);
            }
        }

        private void CalculateAndApplyFitScale()
        {
            if (_viewModel == null || DisplayImage.Source == null)
                return;

            var bmp = DisplayImage.Source as System.Windows.Media.Imaging.BitmapSource;
            if (bmp == null)
                return;

            // Get current viewport size - use the parent grid if ScrollViewer isn't sized yet
            double viewportW = ImageScrollViewer.ActualWidth;
            double viewportH = ImageScrollViewer.ActualHeight;
            
            // DEBUG: Check what we're getting for viewport size
            System.Diagnostics.Debug.WriteLine($"ScrollViewer size: {viewportW}x{viewportH}");
            
            // If ScrollViewer isn't sized, try to get from parent container
            if (viewportW < 10 || viewportH < 10)
            {
                var parent = ImageScrollViewer.Parent as FrameworkElement;
                if (parent != null)
                {
                    viewportW = parent.ActualWidth - 16; // Account for margins/borders
                    viewportH = parent.ActualHeight - 16;
                    System.Diagnostics.Debug.WriteLine($"Parent size: {viewportW}x{viewportH}");
                }
            }
            
            // Still no good size? Use reasonable defaults based on window
            if (viewportW < 10 || viewportH < 10)
            {
                viewportW = this.ActualWidth * 0.6; // Approximate main content area
                viewportH = this.ActualHeight * 0.8;
                System.Diagnostics.Debug.WriteLine($"Window-based size: {viewportW}x{viewportH}");
            }

            double imgW = bmp.PixelWidth;
            double imgH = bmp.PixelHeight;
            
            System.Diagnostics.Debug.WriteLine($"Image size: {imgW}x{imgH}");
            
            if (imgW > 0 && imgH > 0 && viewportW > 0 && viewportH > 0)
            {
                // Calculate fit scale with minimal padding (2 pixels on each side)
                double scaleX = (viewportW - 4) / imgW;
                double scaleY = (viewportH - 4) / imgH;
                double fitScale = Math.Min(scaleX, scaleY);
                
                System.Diagnostics.Debug.WriteLine($"Calculated fit scale: {fitScale}");
                
                // Ensure reasonable bounds - but if it's still too small, use a fallback
                fitScale = Math.Max(0.01, Math.Min(5.0, fitScale));
                
                // If fit scale is still very small (less than 1%), something is wrong - use 10% as fallback
                if (fitScale < 0.01)
                {
                    fitScale = 0.1;
                    System.Diagnostics.Debug.WriteLine($"Using fallback fit scale: {fitScale}");
                }
                
                _viewModel.FitScale = fitScale;
                _viewModel.ZoomLevel = fitScale;
                
                System.Diagnostics.Debug.WriteLine($"Final zoom level set to: {_viewModel.ZoomLevel}");
            }
            else
            {
                // Fallback if we can't calculate properly
                System.Diagnostics.Debug.WriteLine("Cannot calculate fit scale - using fallback 0.1");
                _viewModel.FitScale = 0.1;
                _viewModel.ZoomLevel = 0.1;
            }
        }

        // Fit image to ScrollViewer viewport by adjusting ZoomLevel
        private void FitImageToScrollViewer()
        {
            CalculateAndApplyFitScale();
            
            // Reset scroll to origin for fit mode
            ImageScrollViewer.ScrollToHorizontalOffset(0);
            ImageScrollViewer.ScrollToVerticalOffset(0);
        }

        // Mouse pan state tracking for drag-to-pan functionality
        // Nullable Point - null means no panning in progress, non-null stores last mouse position
        private System.Windows.Point? _lastMousePos = null;

        /// <summary>
        /// Event handler for mouse wheel scrolling over the image viewer.
        /// Implements custom zoom behavior that works differently in Fit mode vs Manual zoom mode.
        /// 
        /// Behavior in Fit Mode:
        /// - Mouse wheel up: Exit fit mode and zoom in slightly
        /// - Mouse wheel down: Ignored (stay in fit mode)
        /// 
        /// Behavior in Manual Zoom Mode:
        /// - Mouse wheel up: Zoom in (up to 500% maximum)
        /// - Mouse wheel down: Zoom out (if would go below fit scale, return to fit mode)
        /// 
        /// This provides intuitive zoom behavior where fit mode is the "default" state
        /// and manual zoom is entered/exited naturally through mouse wheel interaction.
        /// </summary>
        /// <param name="sender">The ImageScrollViewer control</param>
        /// <param name="e">Mouse wheel event arguments containing scroll direction and amount</param>
        private void ImageScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Safety check: ensure we have valid ViewModel and image loaded
            if (_viewModel == null || DisplayImage.Source == null) 
            {
                e.Handled = true;  // Mark event as handled to prevent default scrolling
                return;
            }
            
            // Always handle the mouse wheel event to prevent default ScrollViewer behavior
            // We want custom zoom behavior, not document-style scrolling
            e.Handled = true;
            
            if (_viewModel.FitMode)
            {
                // In Fit mode, mouse wheel should zoom in to exit fit mode
                if (e.Delta > 0)
                {
                    _viewModel.FitMode = false;
                    _viewModel.ZoomLevel = _viewModel.FitScale * 1.25;
                    CenterImageAfterZoom();
                }
                // Ignore zoom out in fit mode
                return;
            }
            
            // In zoom mode, use mouse wheel for zooming
            double oldZoom = _viewModel.ZoomLevel;
            if (e.Delta > 0)
            {
                // Zoom in
                _viewModel.ZoomLevel = Math.Min(_viewModel.ZoomLevel * 1.25, 5.0);
            }
            else
            {
                // Zoom out
                double newZoom = _viewModel.ZoomLevel / 1.25;
                
                // If we would zoom smaller than fit scale, go to fit mode instead
                if (newZoom <= _viewModel.FitScale)
                {
                    _viewModel.FitMode = true;
                    FitImageToScrollViewer();
                    return;
                }
                
                _viewModel.ZoomLevel = newZoom;
            }
            
            // Center the image after zoom change
            if (Math.Abs(_viewModel.ZoomLevel - oldZoom) > 0.0001)
            {
                CenterImageAfterZoom();
            }
        }

        // Center image in ScrollViewer after zoom or fit
        private void CenterImageInScrollViewer()
        {
            CenterImageAfterZoom();
        }

        private void CenterImageAfterZoom()
        {
            if (_viewModel == null || DisplayImage.Source == null)
                return;

            if (_viewModel.FitMode)
            {
                // In fit mode, always reset to origin
                ImageScrollViewer.ScrollToHorizontalOffset(0);
                ImageScrollViewer.ScrollToVerticalOffset(0);
                return;
            }

            // In zoom mode, center the image
            if (DisplayImage.Source is System.Windows.Media.Imaging.BitmapSource bmp)
            {
                double imgW = bmp.PixelWidth * _viewModel.ZoomLevel;
                double imgH = bmp.PixelHeight * _viewModel.ZoomLevel;
                double viewportW = ImageScrollViewer.ActualWidth;
                double viewportH = ImageScrollViewer.ActualHeight;
                
                if (viewportW < 10 || viewportH < 10) return;
                
                // Calculate center position
                double offsetX = Math.Max(0, (imgW - viewportW) / 2);
                double offsetY = Math.Max(0, (imgH - viewportH) / 2);
                
                ImageScrollViewer.ScrollToHorizontalOffset(offsetX);
                ImageScrollViewer.ScrollToVerticalOffset(offsetY);
            }
        }
        
        /// <summary>
        /// Event handler for mouse button press on the image.
        /// Initiates drag-to-pan functionality when in manual zoom mode.
        /// 
        /// Drag-to-pan only works in manual zoom mode - in fit mode, the entire image
        /// is visible so panning doesn't make sense.
        /// 
        /// Mouse capture ensures we continue to receive mouse events even if the
        /// cursor moves outside the image bounds during dragging.
        /// </summary>
        /// <param name="sender">The DisplayImage control</param>
        /// <param name="e">Mouse button event arguments</param>
        private void DisplayImage_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Only allow panning in manual zoom mode (not in fit mode)
            if (_viewModel == null || _viewModel.FitMode) return;
            
            // Record the starting mouse position relative to the ScrollViewer
            // This will be used to calculate movement delta in MouseMove
            _lastMousePos = e.GetPosition(ImageScrollViewer);
            
            // Capture mouse input so we receive events even if cursor leaves the image
            DisplayImage.CaptureMouse();
        }

        /// <summary>
        /// Event handler for mouse button release on the image.
        /// Ends the drag-to-pan operation by clearing the tracking state and releasing mouse capture.
        /// 
        /// This method is called when the user releases the left mouse button, regardless of
        /// where the cursor is located (thanks to mouse capture).
        /// </summary>
        /// <param name="sender">The DisplayImage control</param>
        /// <param name="e">Mouse button event arguments</param>
        private void DisplayImage_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Clear the last mouse position to indicate panning has ended
            _lastMousePos = null;
            
            // Release mouse capture so normal mouse handling resumes
            DisplayImage.ReleaseMouseCapture();
        }

        /// <summary>
        /// Event handler for mouse movement over the image.
        /// Implements the actual panning logic by calculating mouse movement delta
        /// and adjusting the ScrollViewer's scroll position accordingly.
        /// 
        /// The panning algorithm:
        /// 1. Calculate how far the mouse has moved since the last position
        /// 2. Adjust the ScrollViewer's scroll offset by the opposite amount
        /// 3. Update the last mouse position for the next movement calculation
        /// 
        /// Moving the mouse right should scroll the content left (revealing more content on the right),
        /// hence the negative delta in the scroll offset calculation.
        /// </summary>
        /// <param name="sender">The DisplayImage control</param>
        /// <param name="e">Mouse movement event arguments</param>
        private void DisplayImage_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Only process mouse movement in manual zoom mode
            if (_viewModel == null || _viewModel.FitMode) return;

            // Only pan if we're tracking a drag operation (left button is pressed and we have a start position)
            if (_lastMousePos.HasValue && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                // Get current mouse position relative to the ScrollViewer
                var pos = e.GetPosition(ImageScrollViewer);
                
                // Calculate how far the mouse has moved since last update
                double dx = pos.X - _lastMousePos.Value.X;  // Horizontal movement
                double dy = pos.Y - _lastMousePos.Value.Y;  // Vertical movement
                
                // Adjust scroll position by the opposite amount (pan in opposite direction of mouse movement)
                ImageScrollViewer.ScrollToHorizontalOffset(ImageScrollViewer.HorizontalOffset - dx);
                ImageScrollViewer.ScrollToVerticalOffset(ImageScrollViewer.VerticalOffset - dy);
                
                // Update last position for next movement calculation
                _lastMousePos = pos;
            }
        }

        /// <summary>
        /// Event handler for Help > Documentation menu item.
        /// Shows the documentation window with README content and help information.
        /// </summary>
        /// <param name="sender">The menu item that was clicked</param>
        /// <param name="e">Event arguments for the click event</param>
        private void Documentation_Click(object sender, RoutedEventArgs e)
        {
            // Create a new documentation window instance
            var documentationWindow = new DocumentationWindow();
            
            // Set this main window as the owner - makes dialog appear on top and centered
            documentationWindow.Owner = this;
            
            // Show as modal dialog - user must close it before returning to main window
            documentationWindow.ShowDialog();
        }

        /// <summary>
        /// Event handler for Open Recent toolbar button.
        /// Shows the context menu with recent folders.
        /// </summary>
        /// <param name="sender">The button that was clicked</param>
        /// <param name="e">Event arguments for the click event</param>
        private void OpenRecentButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.ContextMenu != null)
            {
                // Clear any existing recent folder menu items (keep the first 3: Open Folder, Separator, Header)
                while (button.ContextMenu.Items.Count > 3)
                {
                    button.ContextMenu.Items.RemoveAt(3);
                }
                
                // Add recent folder menu items dynamically
                if (_viewModel != null && _viewModel.RecentFolders.Count > 0)
                {
                    foreach (var folder in _viewModel.RecentFolders)
                    {
                        var menuItem = new System.Windows.Controls.MenuItem
                        {
                            Header = folder,
                            Command = _viewModel.OpenRecentFolderCommand,
                            CommandParameter = folder
                        };
                        button.ContextMenu.Items.Add(menuItem);
                    }
                }
                else
                {
                    // Show a disabled item if no recent folders
                    var noItemsMenuItem = new System.Windows.Controls.MenuItem
                    {
                        Header = "(No recent folders)",
                        IsEnabled = false
                    };
                    button.ContextMenu.Items.Add(noItemsMenuItem);
                }
                
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;
            }
        }

        /// <summary>
        /// Shows the graph window, opening the settings dialog first if needed.
        /// </summary>
        private void ShowGraph_Click(object sender, RoutedEventArgs e)
        {
            var availableCols = GetGraphColumns();

            // If no Y columns saved yet, force the settings dialog first
            bool needSettings = _appConfig.GraphYColumns.Count == 0;

            if (needSettings)
            {
                var dlg = new GraphSettingsDialog(
                    availableCols,
                    _appConfig.GraphXColumn,
                    _appConfig.GraphYColumns,
                    _appConfig.GraphChartType)
                { Owner = this };

                if (dlg.ShowDialog() != true) return;

                _appConfig.GraphXColumn   = dlg.SelectedXColumn;
                _appConfig.GraphYColumns  = dlg.SelectedYColumns;
                _appConfig.GraphChartType = dlg.SelectedChartType;
                _appConfig.Save();
            }

            var graphWindow = new GraphWindow(
                _appConfig,
                () => _viewModel?.Files?.ToList() ?? new List<FileItem>(),
                GetGraphColumns)
            { Owner = this };

            graphWindow.Show(); // modeless
        }

        /// <summary>
        /// Returns the set of columns available for graphing: always Time and Filter,
        /// plus every currently-enabled column from AppConfig.
        /// </summary>
        private IEnumerable<string> GetGraphColumns()
        {
            var cols = new List<string>();

            // Always-available graph dimensions
            cols.Add("Time");
            cols.Add("Filter");

            // Keep common filename columns available for Y-axis usage
            cols.Add("Date");
            cols.Add("Frame");

            if (_appConfig.ShowSizeColumn)   cols.Add("Size");
            if (_appConfig.ShowMedianColumn) { cols.Add("Median"); cols.Add("Mean"); }

            cols.AddRange(_appConfig.CustomKeywords);
            cols.AddRange(_appConfig.FitsKeywords);
            cols.AddRange(_appConfig.CsvKeywords);

            return cols.Distinct();
        }

        /// <summary>
        /// Event handler for Help > About menu item.
        /// Reuses the splash screen window to show application information.
        /// </summary>
        /// <param name="sender">The menu item that was clicked</param>
        /// <param name="e">Event arguments for the click event</param>
        private void About_Click(object sender, RoutedEventArgs e)
        {
            // Reuse the splash screen window as an "About" dialog
            var aboutWindow = new SplashWindow(_appConfig.ShowSplashScreen, _appConfig);
            
            // Set this main window as the owner
            aboutWindow.Owner = this;
            
            // Change the title to indicate this is the About dialog
            aboutWindow.Title = "About ApexAstro";
            
            // Show as modal dialog
            var result = aboutWindow.ShowDialog();
            
            // If user clicked OK, save the "Don't show again" preference
            if (result == true)
            {
                try
                {
                    // Update configuration based on checkbox state
                    _appConfig.ShowSplashScreen = !aboutWindow.DontShowAgain;
                    _appConfig.Save();
                }
                catch
                {
                    // If we can't save config, just ignore it
                }
            }
        }

        /// <summary>
        /// Event handler for View Activity Log menu item
        /// </summary>
        private void ViewLog_Click(object sender, RoutedEventArgs e)
        {
            var logWindow = new LogViewerWindow(_loggingService);
            logWindow.Owner = this;
            logWindow.ShowDialog();
        }

        /// <summary>
        /// Event handler for Help > Feedback menu item.
        /// Opens the GitHub issues page in the default browser.
        /// </summary>
        private void Feedback_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = "https://github.com/kfaubel/ApexAstro/issues";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Unable to open browser: {ex.Message}\n\nPlease visit:\nhttps://github.com/kfaubel/ApexAstro/issues",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Event handler for Float button - floats image and histogram windows
        /// </summary>
        private void FloatButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
                return;

            if (!_isFloating)
            {
                // Capture the current width of the image area before undocking
                double imageAreaWidth = ImageContentGrid.ActualWidth;
                
                // Create and show floating image window
                _floatingImageWindow = new FloatingImageWindow(_viewModel, DockBack, imageAreaWidth);
                _floatingImageWindow.Show();

                // Only create and show floating histogram window if histogram is enabled
                if (_appConfig.ShowHistogram)
                {
                    _floatingHistogramWindow = new FloatingHistogramWindow(_viewModel.HistogramViewModel, DockBack);
                    _floatingHistogramWindow.Show();
                    // Histogram is automatically synced through shared ViewModel
                }

                // Update floating image window with current image
                if (DisplayImage.Source is System.Windows.Media.Imaging.BitmapSource currentImage)
                {
                    _floatingImageWindow.UpdateImage(currentImage);
                }

                // Hide docked image and histogram in main window
                // Collapse the entire right column to give full space to file list
                // Use Pixel-based sizing (not Star) so columns don't participate in resize
                FileListColumn.Width = new GridLength(1, GridUnitType.Star);
                ImageColumn.Width = new GridLength(0, GridUnitType.Pixel);
                ImageColumn.MinWidth = 0;
                ImageColumn.MaxWidth = 0; // Prevent column from expanding during layout/resize
                SplitterColumn.Width = new GridLength(0, GridUnitType.Pixel);
                ImageContentGrid.Visibility = Visibility.Collapsed;
                ColumnSplitter.Visibility = Visibility.Collapsed;
                ColumnSplitter.IsEnabled = false; // Disable splitter to prevent any interaction

                // Show the re-dock button in the toolbar
                RedockButton.Visibility = Visibility.Visible;

                _isFloating = true;
                _loggingService.LogInfo("Floated image and histogram windows");
            }
        }

        /// <summary>
        /// Event handler for Re-dock button - docks the floating windows back into the main window
        /// </summary>
        private void RedockButton_Click(object sender, RoutedEventArgs e)
        {
            DockBack();
        }

        /// <summary>
        /// Dock the floating windows back into the main window
        /// </summary>
        private void DockBack()
        {
            if (!_isFloating)
                return;

            // Set flag first to prevent recursion
            _isFloating = false;

            // Close floating windows (but check if they're still valid)
            var imageWindow = _floatingImageWindow;
            var histogramWindow = _floatingHistogramWindow;
            
            _floatingImageWindow = null;
            _floatingHistogramWindow = null;

            // Close windows if they're not already closing
            if (imageWindow != null)
            {
                try
                {
                    imageWindow.CloseWindow();
                }
                catch { /* Window may already be closing */ }
            }

            if (histogramWindow != null)
            {
                try
                {
                    histogramWindow.CloseWindow();
                }
                catch { /* Window may already be closing */ }
            }

            // Show docked image and histogram in main window
            // Restore the right column to 25% width (1* vs 3* for file list)
            FileListColumn.Width = new GridLength(3, GridUnitType.Star);
            ImageColumn.Width = new GridLength(1, GridUnitType.Star);
            ImageColumn.MinWidth = 200;
            ImageColumn.MaxWidth = double.PositiveInfinity; // Remove max width constraint
            SplitterColumn.Width = GridLength.Auto;
            ImageContentGrid.Visibility = Visibility.Visible;
            ColumnSplitter.Visibility = Visibility.Visible;
            ColumnSplitter.IsEnabled = true; // Re-enable splitter
            
            // Restore histogram visibility based on current setting
            SetHistogramVisibility(_appConfig.ShowHistogram);

            // Hide the re-dock button in the toolbar
            RedockButton.Visibility = Visibility.Collapsed;

            // Refresh the display
            if (_viewModel != null && _viewModel.SelectedIndex >= 0 && _viewModel.SelectedIndex < _viewModel.Files.Count)
            {
                ImageScrollViewer.Visibility = Visibility.Visible;
                PlaceholderText.Visibility = Visibility.Collapsed;
            }
            else
            {
                ImageScrollViewer.Visibility = Visibility.Collapsed;
                PlaceholderText.Text = "Select an image from the file list";
                PlaceholderText.Visibility = Visibility.Visible;
            }

            _loggingService.LogInfo("Docked image and histogram windows back to main window");
        }

        #region File Progress Dialog
        // These methods handle showing a progress dialog while loading files

        /// <summary>
        /// Event handler for Help > Check for Updates menu item.
        /// Manually checks for updates from GitHub releases.
        /// </summary>
        /// <param name="sender">The menu item that was clicked</param>
        /// <param name="e">Event arguments for the click event</param>
        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show working cursor
                this.Cursor = System.Windows.Input.Cursors.Wait;
                
                // Create update service
                using var updateService = new Services.UpdateService(_appConfig.UpdateRepoOwner, _appConfig.UpdateRepoName);
                
                // Check for updates
                var updateInfo = await updateService.CheckForUpdatesAsync();
                
                if (updateInfo != null)
                {
                    // Update available - show dialog
                    var updateDialog = new UpdateDialog(updateInfo, updateService);
                    updateDialog.Owner = this;
                    
                    var result = updateDialog.ShowDialog();
                    
                    // Update config if user changed preferences
                    if (updateDialog.DisableUpdateChecks)
                    {
                        _appConfig.CheckForUpdates = false;
                        _appConfig.Save();
                    }
                }
                else
                {
                    // No update available - inform user
                    System.Windows.MessageBox.Show(
                        "You are running the latest version of ApexAstro.",
                        "No Updates Available",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                // Handle errors gracefully
                _loggingService.LogError("Update Check", "Update check from menu failed", ex);
                System.Windows.MessageBox.Show(
                    $"Unable to check for updates: {ex.Message}",
                    "Update Check Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                // Restore normal cursor
                this.Cursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        /// <summary>
        /// Event handler for File > Exit menu item.
        /// Closes the application.
        /// </summary>
        /// <param name="sender">The menu item that was clicked</param>
        /// <param name="e">Event arguments for the click event</param>
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            // Close the main window, which will trigger the Closing event and save state
            this.Close();
        }

        /// <summary>
        /// Opens a file dialog to select multiple image files
        /// </summary>
        /// <summary>
        /// Event handler for the Auto Select button - opens the Auto Mark dialog
        /// </summary>
        private void AutoSelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
                return;

            // Show the Auto Mark dialog with custom keywords and FITS keywords
            var autoMarkDialog = new AutoMarkDialog(
                _appConfig.CustomKeywords,
                _appConfig.FitsKeywords,
                _appConfig.AutoSelectCriteria)
            {
                Owner = this
            };

            // Handle the Apply event
            autoMarkDialog.ApplyRequested += (dialogSender, criteria) =>
            {
                ApplyAutoMark(criteria);
            };

            var result = autoMarkDialog.ShowDialog();
            if (result == true)
            {
                _appConfig.AutoSelectCriteria = autoMarkDialog.GetCurrentSettings();
                _appConfig.Save();
            }
        }

        /// <summary>
        /// Event handler for the Select All button - selects all files in the list
        /// </summary>
        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.Files == null)
                return;

            foreach (var fileItem in _viewModel.Files)
            {
                fileItem.IsSelected = true;
            }
            _loggingService.LogInfo($"Select All: Selected all {_viewModel.Files.Count} files");
        }

        /// <summary>
        /// Event handler for the Clear All button - clears all selections
        /// </summary>
        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.Files == null)
                return;

            foreach (var fileItem in _viewModel.Files)
            {
                fileItem.IsSelected = false;
            }
            _loggingService.LogInfo("Clear All: Cleared all selections");
        }

        /// <summary>
        /// Applies auto-mark logic: marks files for deletion that don't meet the criteria
        /// </summary>
        private void ApplyAutoMark(List<AutoMarkCriteria> criteriaList)
        {
            if (_viewModel?.Files == null || criteriaList.Count == 0)
                return;

            try
            {
                int markedCount = 0;

                // First, clear all marked boxes
                foreach (var fileItem in _viewModel.Files)
                {
                    fileItem.IsSelected = false;
                }

                // Then, mark files that don't meet the criteria
                foreach (var fileItem in _viewModel.Files)
                {
                    bool shouldMark = false;
                    string? failedKeyword = null;
                    string? failedValue = null;

                    // Check each enabled criteria
                    foreach (var criteria in criteriaList)
                    {
                        string? valueStr = null;

                        // Handle special keywords
                        if (criteria.Key.Equals("Median", StringComparison.OrdinalIgnoreCase))
                        {
                            // Get Median value from FileItem property
                            if (fileItem.Median.HasValue)
                            {
                                valueStr = fileItem.Median.Value.ToString("F4");
                            }
                            // If no median value, valueStr stays null (will be treated as blank)
                        }
                        else
                        {
                            // Try to get value from CustomKeywords first, then FitsKeywords
                            if (!fileItem.CustomKeywords.TryGetValue(criteria.Key, out valueStr))
                            {
                                fileItem.FitsKeywords.TryGetValue(criteria.Key, out valueStr);
                            }
                        }

                        // Check if value passes criteria
                        if (!criteria.IsValueAcceptable(valueStr))
                        {
                            shouldMark = true;
                            failedKeyword = criteria.Key;
                            failedValue = valueStr ?? "(blank)";
                            break;
                        }
                    }

                    // Update the file's selected state
                    if (shouldMark)
                    {
                        fileItem.IsSelected = true;
                        markedCount++;
                        _loggingService.LogInfo($"Auto-mark: Marked '{fileItem.Name}' - {failedKeyword}={failedValue}");
                    }
                }

                _loggingService.LogInfo($"Auto-mark: Marked {markedCount} of {_viewModel.Files.Count} files for deletion based on criteria");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Auto Mark", "Error applying auto-mark criteria", ex);
                System.Windows.MessageBox.Show($"Error applying auto-mark criteria: {ex.Message}", 
                    "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async void OpenFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Image Files",
                Multiselect = true,
                Filter = "All Supported Files|*.fits;*.fit;*.fts;*.xisf;*.jpg;*.jpeg;*.png;*.bmp;*.tiff;*.tif;*.gif;*.webp|" +
                        "FITS Files (*.fits, *.fit, *.fts)|*.fits;*.fit;*.fts|" +
                        "XISF Files (*.xisf)|*.xisf|" +
                        "JPEG Files (*.jpg, *.jpeg)|*.jpg;*.jpeg|" +
                        "PNG Files (*.png)|*.png|" +
                        "BMP Files (*.bmp)|*.bmp|" +
                        "TIFF Files (*.tiff, *.tif)|*.tiff;*.tif|" +
                        "GIF Files (*.gif)|*.gif|" +
                        "WebP Files (*.webp)|*.webp|" +
                        "All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
            {
                // Load the selected files
                await LoadSpecificFilesWithProgressAsync(dialog.FileNames);
            }
        }
        #endregion

        #region Window State Management
        /// <summary>
        /// Restores the window position, size, and state from the saved configuration.
        /// Called during window initialization to restore the user's preferred layout.
        /// </summary>
        /// <param name="config">Application configuration containing saved window state</param>
        private void RestoreWindowState(AppConfig config)
        {
            // Restore window size
            if (config.WindowWidth > 0 && config.WindowHeight > 0)
            {
                this.Width = config.WindowWidth;
                this.Height = config.WindowHeight;
            }

            // Restore window position if valid (not NaN)
            if (!double.IsNaN(config.WindowLeft) && !double.IsNaN(config.WindowTop))
            {
                this.Left = config.WindowLeft;
                this.Top = config.WindowTop;
            }

            // Restore window state (Normal, Minimized, Maximized)
            this.WindowState = (WindowState)config.WindowState;

            // Subscribe to window events to save state when changed
            this.LocationChanged += MainWindow_LocationChanged;
            this.SizeChanged += MainWindow_SizeChanged;
            this.StateChanged += MainWindow_StateChanged;
            this.Closing += MainWindow_Closing;
        }

        /// <summary>
        /// Event handler for when the window position changes.
        /// Saves the new position to configuration.
        /// </summary>
        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            // Only save if window is in normal state (not minimized or maximized)
            if (this.WindowState == WindowState.Normal)
            {
                _appConfig.WindowLeft = this.Left;
                _appConfig.WindowTop = this.Top;
                _appConfig.Save();
            }
        }

        /// <summary>
        /// Event handler for when the window size changes.
        /// Saves the new size to configuration.
        /// </summary>
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Only save if window is in normal state
            if (this.WindowState == WindowState.Normal)
            {
                _appConfig.WindowWidth = this.Width;
                _appConfig.WindowHeight = this.Height;
                _appConfig.Save();
            }
            
            // Update the File column width to be responsive to window width
            _listViewColumnService?.UpdateFileColumnWidth();

            // While floating, keep the file list as a single full-width pane.
            if (_isFloating)
            {
                FileListColumn.Width = new GridLength(1, GridUnitType.Star);
                SplitterColumn.Width = new GridLength(0, GridUnitType.Pixel);
                ImageColumn.Width = new GridLength(0, GridUnitType.Pixel);
                return;
            }
            
            // Adjust splitter position when window resizes (with small delay for layout)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                _listViewColumnService?.AdjustSplitterForOptimalWidth();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// Event handler for when the window state changes (Normal/Minimized/Maximized).
        /// Saves the new state to configuration.
        /// </summary>
        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            _appConfig.WindowState = (int)this.WindowState;
            _appConfig.Save();
        }

        /// <summary>
        /// Event handler for when the window is closing.
        /// Saves the final window state to configuration.
        /// </summary>
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Cancel any ongoing background warmup
            _warmupCancellation?.Cancel();
            _warmupCancellation?.Dispose();
            
            // Close floating windows if they're open
            if (_isFloating)
            {
                _floatingImageWindow?.CloseWindow();
                _floatingHistogramWindow?.CloseWindow();
            }
            
            // Save current window state
            if (this.WindowState == WindowState.Normal)
            {
                _appConfig.WindowLeft = this.Left;
                _appConfig.WindowTop = this.Top;
                _appConfig.WindowWidth = this.Width;
                _appConfig.WindowHeight = this.Height;
            }
            _appConfig.WindowState = (int)this.WindowState;

            _playbackPrefetchCancellation?.Cancel();
            _playbackPrefetchCancellation?.Dispose();
            _playbackPrefetchCancellation = null;

            // Persist the user's latest file-list column ordering.
            _listViewColumnService?.SaveCurrentColumnOrder();
            
            _appConfig.Save();
        }

        private void QueuePlaybackPrefetch()
        {
            if (_viewModel == null || _viewModel.Files.Count == 0 || _viewModel.SelectedIndex < 0)
            {
                return;
            }

            _playbackPrefetchCancellation?.Cancel();
            _playbackPrefetchCancellation?.Dispose();
            _playbackPrefetchCancellation = new System.Threading.CancellationTokenSource();
            var token = _playbackPrefetchCancellation.Token;

            int selectedIndex = _viewModel.SelectedIndex;
            int[] offsets = new[] { -2, -1, 1, 2 };

            foreach (int offset in offsets)
            {
                int index = selectedIndex + offset;
                if (index < 0 || index >= _viewModel.Files.Count)
                {
                    continue;
                }

                string path = _viewModel.Files[index].Path;
                string cacheKey = BuildPlaybackCacheKey(path);

                lock (_playbackCacheLock)
                {
                    if (_playbackImageCache.ContainsKey(cacheKey) || _prefetchInProgress.Contains(cacheKey))
                    {
                        continue;
                    }

                    _prefetchInProgress.Add(cacheKey);
                }

                _ = Task.Run(() => PrefetchPlaybackImage(cacheKey, path, token), token);
            }

            TrimPlaybackCacheAround(selectedIndex);
        }

        private void PrefetchPlaybackImage(string cacheKey, string path, System.Threading.CancellationToken token)
        {
            try
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                var image = FitsImageRenderer.RenderFitsFile(path, _viewModel?.AutoStretch ?? true, _appConfig.StretchAggressiveness);
                if (image != null)
                {
                    if (image.CanFreeze && !image.IsFrozen)
                    {
                        image.Freeze();
                    }

                    CachePlaybackImage(cacheKey, image);
                }
            }
            catch
            {
                // Prefetch failures should not interrupt interactive viewing.
            }
            finally
            {
                lock (_playbackCacheLock)
                {
                    _prefetchInProgress.Remove(cacheKey);
                }
            }
        }

        private bool TryGetCachedPlaybackImage(string cacheKey, out System.Windows.Media.Imaging.BitmapSource? image)
        {
            lock (_playbackCacheLock)
            {
                if (_playbackImageCache.TryGetValue(cacheKey, out var cached))
                {
                    image = cached;
                    return true;
                }
            }

            image = null;
            return false;
        }

        private void CachePlaybackImage(string cacheKey, System.Windows.Media.Imaging.BitmapSource image)
        {
            lock (_playbackCacheLock)
            {
                _playbackImageCache[cacheKey] = image;
            }
        }

        private void TrimPlaybackCacheAround(int selectedIndex)
        {
            if (_viewModel == null || _viewModel.Files.Count == 0 || selectedIndex < 0)
            {
                return;
            }

            var keepKeys = new HashSet<string>();
            for (int i = selectedIndex - 2; i <= selectedIndex + 2; i++)
            {
                if (i >= 0 && i < _viewModel.Files.Count)
                {
                    keepKeys.Add(BuildPlaybackCacheKey(_viewModel.Files[i].Path));
                }
            }

            lock (_playbackCacheLock)
            {
                var toRemove = _playbackImageCache.Keys.Where(k => !keepKeys.Contains(k)).ToList();
                foreach (var key in toRemove)
                {
                    _playbackImageCache.Remove(key);
                }
            }
        }

        private string BuildPlaybackCacheKey(string path)
        {
            bool autoStretch = _viewModel?.AutoStretch ?? true;
            return $"{path}|{autoStretch}|{_appConfig.StretchAggressiveness}";
        }

        private void ClearPlaybackPrefetchCache()
        {
            _playbackPrefetchCancellation?.Cancel();

            lock (_playbackCacheLock)
            {
                _playbackImageCache.Clear();
                _prefetchInProgress.Clear();
            }
        }
        #endregion

        /// <summary>
        /// Sets the visibility of the histogram panel and its splitter.
        /// When hidden, the histogram row collapses to save space.
        /// </summary>
        /// <param name="visible">True to show the histogram, false to hide it</param>
        private void SetHistogramVisibility(bool visible)
        {
            if (visible)
            {
                // Show histogram splitter and control
                HistogramSplitter.Visibility = Visibility.Visible;
                HistogramControl.Visibility = Visibility.Visible;
                // Restore row heights
                HistogramSplitterRow.Height = GridLength.Auto;
                HistogramRow.Height = new GridLength(200, GridUnitType.Pixel);
            }
            else
            {
                // Hide histogram splitter and control
                HistogramSplitter.Visibility = Visibility.Collapsed;
                HistogramControl.Visibility = Visibility.Collapsed;
                // Collapse rows completely to prevent grey areas
                HistogramSplitterRow.Height = new GridLength(0, GridUnitType.Pixel);
                HistogramRow.Height = new GridLength(0, GridUnitType.Pixel);
            }
        }
    }
}