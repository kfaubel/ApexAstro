using System.Windows;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ApexAstro.Wpf.Services;

namespace ApexAstro.Wpf
{
    /// <summary>
    /// Main Application class - this is the entry point for the WPF application.
    /// Inherits from System.Windows.Application which provides the basic application framework.
    /// The "partial" keyword means this class is split across multiple files (App.xaml and App.xaml.cs).
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private const string ActivateMessage = "__ACTIVATE__";

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        private static Mutex? _mutex;
        private const string MutexName = "ApexAstro_SingleInstance_Mutex";
        private const string PipeName = "ApexAstro_SingleInstance_Pipe";
        private CancellationTokenSource? _ipcCancellation;
        private Task? _ipcListenerTask;
        
        // Singleton logging service available throughout the application
        public static ILoggingService LoggingService { get; private set; } = new LoggingService();

        /// <summary>
        /// Constructor - sets up global exception handling
        /// </summary>
        public App()
        {
            // Handle unhandled exceptions
            this.DispatcherUnhandledException += (sender, e) =>
            {
                try
                {
                    LoggingService.LogError("Unhandled Exception", e.Exception.Message, e.Exception);
                    System.Windows.MessageBox.Show(
                        $"Unhandled Exception:\n\nMessage: {e.Exception.Message}\n\nStack Trace: {e.Exception.StackTrace}",
                        "ApexAstro - Unhandled Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }
                e.Handled = true;
            };
        }

        /// <summary>
        /// OnStartup is called when the application starts up.
        /// This is where we initialize the main window and handle the splash screen.
        /// Override means we're replacing the base class implementation with our own.
        /// </summary>
        /// <param name="e">Startup arguments passed to the application</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            // Check if another instance is already running
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                // Another instance is already running
                bool forwarded = e.Args.Length > 0
                    ? TrySendPathToRunningInstance(e.Args[0])
                    : TrySendPathToRunningInstance(ActivateMessage);

                if (!forwarded)
                {
                    System.Windows.MessageBox.Show(
                        "ApexAstro is already running. Please use the running instance to open files.\n\n" +
                        "Note: To open multiple files, select them all in Windows Explorer before right-clicking 'Open with'.",
                        "ApexAstro Already Running",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                
                Current.Shutdown();
                return;
            }

            try
            {
                // Always call the base class method first - this initializes the WPF framework
                base.OnStartup(e);
                
                LoggingService.LogInfo("Application starting");
                
                // Initialize the theme service before creating any windows
                ThemeService.Initialize();
                
                // Extract file paths from command-line arguments (if any)
                // When user opens files with our app from Explorer, paths are passed as arguments
                string[]? filePaths = e.Args.Length > 0 ? e.Args : null;
                
                // Debug: Log the arguments received
                if (filePaths != null && filePaths.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Command-line arguments received: {filePaths.Length} files");
                    for (int i = 0; i < filePaths.Length; i++)
                    {
                        System.Diagnostics.Debug.WriteLine($"  [{i}]: {filePaths[i]}");
                    }
                }
                
                // Create the main application window first
                // The window will apply saved position/size settings from config automatically
                // Pass file paths if provided so it can load only those files
                var win = new MainWindow(filePaths);
                
                // Set this as the application's main window - WPF uses this to know when to exit
                this.MainWindow = win;
                
                // Make the window visible to the user
                win.Show();

                // Start listening for file/folder paths from subsequent launches.
                StartIpcListener(win);

                // Load application configuration to check splash screen preference
                var config = AppConfig.Load();
                
                // Check for updates asynchronously (non-blocking)
                _ = CheckForUpdatesAsync(config, win);
            }
            catch (Exception ex)
            {
                // If anything goes wrong during startup, show an error and exit gracefully
                // $"{ex}" is string interpolation - puts the exception details in the message
                LoggingService.LogError("Application Startup", ex.Message, ex);
                try
                {
                    System.Windows.MessageBox.Show(
                        $"Startup Exception:\n\nMessage: {ex.Message}\n\nStack Trace: {ex.StackTrace}\n\nInner Exception: {ex.InnerException?.Message ?? "None"}", 
                        "ApexAstro - App Startup Error", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Error);
                }
                catch
                {
                    // If even MessageBox fails, write to console and try to create a log file
                    Console.WriteLine($"Fatal startup error: {ex}");
                    try
                    {
                        var logFile = Path.Combine(Path.GetTempPath(), "ApexAstro_Error.log");
                        File.WriteAllText(logFile, $"ApexAstro Startup Error: {DateTime.Now}\n\n{ex}");
                        Console.WriteLine($"Error details written to: {logFile}");
                    }
                    catch { }
                }
                Environment.Exit(1); // Exit with error code 1
            }
        }

        /// <summary>
        /// Clean up mutex on exit
        /// </summary>
        /// <param name="e"></param>
        protected override void OnExit(ExitEventArgs e)
        {
            LoggingService.LogInfo("Application exiting");

            if (_ipcCancellation != null)
            {
                _ipcCancellation.Cancel();
                _ipcCancellation.Dispose();
                _ipcCancellation = null;
            }
            
            // Safely release the mutex - it may not have been acquired on this thread
            // This can happen when the app is closed from different threads
            try
            {
                if (_mutex != null)
                {
                    try
                    {
                        _mutex.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                        // Mutex was not acquired by this thread, or already released
                        // This is acceptable during shutdown
                    }
                    _mutex.Dispose();
                }
            }
            catch (Exception ex)
            {
                // Log any unexpected errors but don't crash during shutdown
                System.Diagnostics.Debug.WriteLine($"Error releasing mutex: {ex.Message}");
            }
            
            base.OnExit(e);
        }

        private bool TrySendPathToRunningInstance(string message)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(500);
                using var writer = new StreamWriter(client, Encoding.UTF8, 1024, leaveOpen: false);
                writer.WriteLine(message);
                writer.Flush();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void StartIpcListener(MainWindow mainWindow)
        {
            _ipcCancellation = new CancellationTokenSource();
            var token = _ipcCancellation.Token;

            _ipcListenerTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);

                        await server.WaitForConnectionAsync(token);

                        using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
                        var incomingPath = await reader.ReadLineAsync();

                        if (!string.IsNullOrWhiteSpace(incomingPath))
                        {
                            await Dispatcher.InvokeAsync(async () =>
                            {
                                if (MainWindow is MainWindow runningWindow)
                                {
                                    BringWindowToForeground(runningWindow);

                                    if (!string.Equals(incomingPath, ActivateMessage, StringComparison.Ordinal))
                                    {
                                        await runningWindow.OpenExternalPathAsync(incomingPath);
                                    }

                                    BringWindowToForeground(runningWindow);
                                }
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError("IPC Listener", "Failed to process forwarded path", ex);
                    }
                }
            }, token);
        }

        private static void BringWindowToForeground(Window window)
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            if (!window.IsVisible)
            {
                window.Show();
            }

            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
            window.Focus();

            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                ShowWindowAsync(handle, SW_RESTORE);
                SetForegroundWindow(handle);
            }
        }

        /// <summary>
        /// Asynchronously check for updates from GitHub releases
        /// </summary>
        private async Task CheckForUpdatesAsync(AppConfig config, Window mainWindow)
        {
            try
            {
                // Only check if enabled and not checked recently (within 24 hours)
                if (!config.CheckForUpdates) return;
                
                var now = DateTime.Now;
                if (config.LastUpdateCheck.HasValue && 
                    now - config.LastUpdateCheck.Value < TimeSpan.FromHours(24))
                {
                    return; // Already checked recently
                }

                // Create update service with repository information
                using var updateService = new UpdateService(config.UpdateRepoOwner, config.UpdateRepoName);
                
                // Check for updates (this is async and won't block startup)
                var updateInfo = await updateService.CheckForUpdatesAsync();
                
                if (updateInfo != null)
                {
                        // Update found - show dialog on UI thread
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var updateDialog = new UpdateDialog(updateInfo, updateService);
                        updateDialog.Owner = mainWindow;
                        
                        var result = updateDialog.ShowDialog();
                        
                        // Update config based on user's choice
                        if (updateDialog.DisableUpdateChecks)
                        {
                            config.CheckForUpdates = false;
                        }
                        
                        // Record that we checked for updates
                        config.LastUpdateCheck = now;
                        config.Save();
                    });
                }
                else
                {
                    // No update available - just record the check time
                    config.LastUpdateCheck = now;
                    config.Save();
                }
            }
            catch (Exception ex)
            {
                // Don't let update check failures crash the app
                LoggingService.LogError("Update Check", "Failed to check for updates", ex);
                System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
            }
        }
    }
}