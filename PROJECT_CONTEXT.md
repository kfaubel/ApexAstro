# PROJECT_CONTEXT.md

## Solution overview
Astronomical image viewer for Windows.  
Target framework: .NET 8 WPF (`net8.0-windows`).  
Version: 1.8.2 (AssemblyVersion 1.8.2.0).

## Projects

| Project | Description |
|---|---|
| `ApexAstro.Wpf` | Main WPF application (MVVM). Entry point. |
| `ApexAstro.Core` | Pure-logic FITS parsing and utilities. No WPF dependency. |
| `ApexAstro.Utils` | Pure-logic XISF parsing and utilities. No WPF dependency. |
| `ApexAstro.Tests` | xUnit test suite for all three libraries. |

## Key NuGet packages (ApexAstro.Wpf)

| Package | Purpose |
|---|---|
| `OxyPlot.Wpf` | Graph/plot windows |
| `MetadataExtractor` | EXIF metadata for standard images |
| `Magick.NET-Q8-AnyCPU` | Sony ARW (and other camera RAW) decoding |
| `Microsoft.VisualBasic` | Recycle-bin file deletion |
| `System.Text.Json` | JSON serialization for `AppConfig` |

## MVVM structure (ApexAstro.Wpf)

### Model
- `Models/FileItem.cs` — `INotifyPropertyChanged` record for a single image file; holds `Name`, `Path`, `Size`, `Median`, `Mean`, `CustomKeywords`, `FitsKeywords`, `CsvKeywords`, `IsSelected`.

### ViewModel
- `ViewModels/MainWindowViewModel.mvvm.cs` — primary ViewModel; owns `ObservableCollection<FileItem> Files`, navigation/play commands, zoom/stretch state, and all `Action` events that request UI operations from the View.
- `ViewModels/HistogramViewModel.cs` — data for the histogram control (gray/RGB channel arrays, linear vs. log scale).
- `ViewModels/RelayCommand.cs` — `ICommand` implementation used throughout.

### View (Windows & Dialogs)
| File | Purpose |
|---|---|
| `MainWindow.xaml/.cs` | Main window; wires ViewModel events to UI, handles image display, ListView selection, column splitter, floating windows |
| `FullScreenWindow.xaml/.cs` | Full-screen image slideshow |
| `FloatingImageWindow.xaml/.cs` | Detachable image pane |
| `FloatingHistogramWindow.xaml/.cs` | Detachable histogram pane |
| `FileMetadataDialog.xaml/.cs` | Per-file metadata viewer (FITS headers, XISF props, EXIF, custom keywords, image properties) |
| `GraphWindow.xaml/.cs` | OxyPlot scatter/line graphs of metadata columns |
| `AutoMarkDialog.xaml/.cs` | Bulk mark-for-deletion by keyword criteria |
| `GeneralOptionsDialog.xaml/.cs` | App preferences (columns, theme, histogram, median, etc.) |
| `CustomKeywordsDialog.xaml/.cs` | Configure filename-parsed keyword columns |
| `FitsKeywordsDialog.xaml/.cs` | Configure FITS header keyword columns |
| `CsvKeywordsDialog.xaml/.cs` | Configure CSV metadata keyword columns |
| `MoveFilesDialog.xaml/.cs` | Move or recycle-bin selected files |
| `SplashWindow.xaml/.cs` | Startup splash / About dialog |
| `UpdateDialog.xaml/.cs` | Show available GitHub release update |
| `LogViewerWindow.xaml/.cs` | View in-memory application log |
| `DocumentationWindow.xaml/.cs` | Embedded Markdown help viewer |
| `HistogramControl.xaml/.cs` | Reusable histogram WPF control |

### Services (ApexAstro.Wpf/Services)

| Service | Interface | Responsibility |
|---|---|---|
| `FileManagementService` | — | Directory scan, file move/trash, unique filename generation. Supported extensions: `.fits/.fit/.fts`, `.xisf`, `.arw`, `.jpg/.jpeg`, `.png`, `.bmp`, `.tiff/.tif`, `.gif`, `.webp`. |
| `KeywordExtractionService` | — | Extracts filename custom keywords (via `FilenameParser`), FITS/XISF header keywords, median/mean pixel stats. Dispatches to `FitsParser`, `XisfParser`, or `FitsImageRenderer` depending on extension. |
| `AstroDataCacheService` | — | Persists computed `Median`/`Mean` values to `AstroData.csv` in the open folder; uses file last-write timestamp to avoid recomputation. |
| `CsvMetadataService` | — | Reads `ImageMetaData.csv` from the open folder and maps rows to `FileItem.CsvKeywords`. |
| `ListViewColumnService` | `IListViewColumnService` | Adds/removes/styles ListView columns driven by `AppConfig`; auto-resizes. |
| `ThemeService` (static) | — | Applies light/dark WPF resource dictionaries; listens to OS accent-colour changes. |
| `LoggingService` | `ILoggingService` | In-memory log with `LongOperationDetected` event (fires when any operation exceeds 3 s). Singleton on `App`. |
| `UpdateService` | `IDisposable` | Polls GitHub Releases API for newer version. |
| `HistogramService` | — | Generates `int[256]` histograms from raw byte arrays or `BitmapSource`. |
| `ConfigurationService` | — | Thin helper around `AppConfig.Save/Load`. |
| `OperationProgressManager` | — | Tracks long-running operations and triggers `ILoggingService.LongOperationDetected`. |
| `FolderDialogService` | `IFolderDialogService` | Wraps WPF folder-picker dialog. |
| `GeneralOptionsDialogService` | `IGeneralOptionsDialogService` | Shows `GeneralOptionsDialog` and returns result tuple. |
| `CustomKeywordsDialogService` | `ICustomKeywordsDialogService` | Shows `CustomKeywordsDialog`. |
| `FitsKeywordsDialogService` | `IFitsKeywordsDialogService` | Shows `FitsKeywordsDialog`. |

### Rendering
- `FitsImageRenderer` (static) — Central image decoder. Routes by extension:
  - `.fits/.fit/.fts` → `FitsParser.ReadImage` → `Gray8` `BitmapSource` with auto-stretch (MTF).
  - `.xisf` → `XisfParser.ReadImage` / `ReadImageRgb` with auto-stretch; uses `XisfHeaderCache` to skip redundant header parses.
  - `.arw` → `MagickImage` (Magick.NET) decoded to `Bgra32` `BitmapSource`.
  - All others → WPF `BitmapImage` via URI or stream.

### Configuration
- `AppConfig` — JSON-serialised to `%APPDATA%\ApexAstro\appsettings.json`. Stores window position, last/recent folders, column visibility flags, keyword lists, theme, auto-stretch settings, play interval, update preferences, etc.

## Core library (ApexAstro.Core)
- `FitsParser` — FITS header and image data reader; block-by-block streaming header parse for large files; `CalculateStats` for median/mean.
- `FitsUtilities` — Extension/signature detection, header value formatting, RA/Dec conversion, image statistics.

## Utils library (ApexAstro.Utils)
- `XisfParser` — XISF header XML parse, image data extraction (mono + RGB), targeted `FitKeyword` regex search, `CalculateStats`.
- `XisfUtilities` — Signature detection, property value formatting, coordinate helpers.

## Application startup / single-instance
`App.xaml.cs` enforces single-instance via a named `Mutex`; a second launch sends the file path over a named pipe to the running instance and exits. `MainWindow` accepts optional `string[] filePaths` from command-line arguments: if provided, the containing folder is loaded and the target file is selected; otherwise the folder-picker dialog opens after a splash screen.

## Test coverage
| Test file | Covers |
|---|---|
| `FileManagementServiceTests` | Directory load, extension inclusion (including `.arw`), move, unique filename |
| `FileItemTests` | `FileItem` property-change notifications |
| `FilenameParserTests` | Custom keyword extraction from filename |
| `FitsParserTests` / `FitsUtilitiesTests` | FITS header parse, stats, format helpers |
| `XisfParserTests` / `XisfUtilitiesTests` | XISF header parse, stats, property formatting |
| `KeywordExtractionServiceTests` | End-to-end keyword population |
| `CsvMetadataServiceTests` | CSV load/parse |
| `RelayCommandTests` | `ICommand` CanExecute / Execute |
