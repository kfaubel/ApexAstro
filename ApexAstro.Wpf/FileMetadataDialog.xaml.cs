using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ApexAstro.Core;
using ApexAstro.Utils;
using ApexAstro.Wpf.Models;
using MetadataExtractor;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace ApexAstro.Wpf
{
    /// <summary>
    /// Dialog for displaying comprehensive file metadata including FITS headers, 
    /// file properties, and extracted keywords
    /// </summary>
    public partial class FileMetadataDialog : Window
    {
        private readonly FileItem _fileItem;
        private readonly List<FitsHeaderItem> _allFitsHeaders = new List<FitsHeaderItem>();
        private readonly ObservableCollection<FitsHeaderItem> _filteredFitsHeaders = new ObservableCollection<FitsHeaderItem>();
        
        // Store metadata for export functionality
        private FileInfo? _fileInfo;
        private BitmapSource? _imageSource;

        public FileMetadataDialog(FileItem fileItem)
        {
            InitializeComponent();
            _fileItem = fileItem;
            
            // Bind the filtered collection to the ListView
            FitsHeadersListView.ItemsSource = _filteredFitsHeaders;
            
            LoadFileMetadata();
        }

        private void LoadFileMetadata()
        {
            try
            {
                // Set basic file info in header
                FileNameTextBlock.Text = _fileItem.Name;
                FilePathTextBlock.Text = _fileItem.Path;
                
                var fileInfo = new FileInfo(_fileItem.Path);
                if (fileInfo.Exists)
                {
                    FileSizeTextBlock.Text = $"Size: {FormatFileSize(fileInfo.Length)}";
                    
                    // Load file properties
                    LoadFileProperties(fileInfo);
                    
                    // Load image properties if it's an image
                    LoadImageProperties();
                    
                    // Load custom keywords
                    LoadCustomKeywords();
                    
                    // Check if it's a FITS, XISF, or standard image file and load headers/metadata
                    if (IsFitsFile(_fileItem.Path))
                    {
                        LoadFitsHeaders();
                        FitsHeadersTab.Visibility = Visibility.Visible;
                        FitsHeadersTab.Header = "FITS Headers"; // Ensure correct header
                        MetadataTabControl.SelectedItem = FitsHeadersTab; // Show FITS tab first for FITS files
                    }
                    else if (IsXisfFile(_fileItem.Path))
                    {
                        LoadXisfHeaders();
                        FitsHeadersTab.Visibility = Visibility.Visible;
                        FitsHeadersTab.Header = "XISF Metadata"; // Change tab header for XISF files
                        MetadataTabControl.SelectedItem = FitsHeadersTab; // Show metadata tab first for XISF files
                    }
                    else if (IsArwFile(_fileItem.Path))
                    {
                        LoadArwMetadata();
                        FitsHeadersTab.Visibility = Visibility.Visible;
                        FitsHeadersTab.Header = "ARW Metadata";
                        MetadataTabControl.SelectedItem = FitsHeadersTab;
                    }
                    else if (IsStandardImageFile(_fileItem.Path))
                    {
                        LoadExifMetadata();
                        FitsHeadersTab.Visibility = Visibility.Visible;
                        FitsHeadersTab.Header = "EXIF Metadata"; // Change tab header for standard images
                        MetadataTabControl.SelectedItem = FitsHeadersTab; // Show metadata tab first for images with EXIF
                    }
                    else
                    {
                        FitsHeadersTab.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    FileSizeTextBlock.Text = "File not found";
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading file metadata: {ex.Message}", "Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void LoadFileProperties(FileInfo fileInfo)
        {
            _fileInfo = fileInfo; // Store for export
            
            PropFileName.Text = fileInfo.Name;
            PropFullPath.Text = fileInfo.FullName;
            PropFileSize.Text = FormatFileSize(fileInfo.Length);
            PropCreated.Text = fileInfo.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
            PropModified.Text = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
            PropFileType.Text = GetFileTypeDescription(fileInfo.Extension);
        }

        private void LoadImageProperties()
        {
            try
            {
                // Try to load as image to get properties
                var image = FitsImageRenderer.RenderFitsFile(_fileItem.Path);
                if (image is BitmapSource bitmap)
                {
                    _imageSource = bitmap; // Store for export
                    
                    ImagePropertiesSection.Visibility = Visibility.Visible;
                    PropDimensions.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight}";
                    PropPixelFormat.Text = bitmap.Format.ToString();
                    PropDPI.Text = $"{bitmap.DpiX:F1} × {bitmap.DpiY:F1}";
                    PropColorDepth.Text = $"{bitmap.Format.BitsPerPixel} bits";
                    PropHasAlpha.Text = HasAlphaChannel(bitmap.Format) ? "Yes" : "No";
                }
            }
            catch
            {
                // If we can't load as image, hide the image properties section
                ImagePropertiesSection.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadCustomKeywords()
        {
            if (_fileItem.CustomKeywords != null && _fileItem.CustomKeywords.Count > 0)
            {
                CustomKeywordsSection.Visibility = Visibility.Visible;
                var keywordItems = _fileItem.CustomKeywords.Select(kv => new KeyValueItem 
                { 
                    Key = kv.Key, 
                    Value = kv.Value 
                }).ToList();
                CustomKeywordsListView.ItemsSource = keywordItems;
            }
            else
            {
                CustomKeywordsSection.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadFitsHeaders()
        {
            try
            {
                var bytes = File.ReadAllBytes(_fileItem.Path);
                var headers = FitsParser.ParseHeader(bytes);
                
                _allFitsHeaders.Clear();
                
                foreach (var header in headers)
                {
                    var fitsHeader = new FitsHeaderItem
                    {
                        Key = header.Key,
                        Value = header.Value?.ToString() ?? "",
                        Type = GetValueTypeName(header.Value),
                        Comment = "" // FITS comments could be extracted if the parser provided them
                    };
                    _allFitsHeaders.Add(fitsHeader);
                }
                
                // Sort headers alphabetically by key
                _allFitsHeaders.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
                
                // Initially show all headers
                RefreshFitsHeadersList();
                
                FitsCountLabel.Text = $"{_allFitsHeaders.Count} headers";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading FITS headers: {ex.Message}", "FITS Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void LoadXisfHeaders()
        {
            try
            {
                var bytes = File.ReadAllBytes(_fileItem.Path);
                var metadata = XisfParser.ParseMetadata(bytes);
                
                _allFitsHeaders.Clear();
                
                foreach (var prop in metadata)
                {
                    var xisfHeader = new FitsHeaderItem
                    {
                        Key = prop.Key,
                        Value = XisfUtilities.FormatPropertyValue(prop.Value),
                        Type = GetValueTypeName(prop.Value),
                        Comment = "" // Comments are separate in XISF and would need special handling
                    };
                    _allFitsHeaders.Add(xisfHeader);
                }
                
                // Sort headers alphabetically by key
                _allFitsHeaders.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
                
                // Initially show all headers
                RefreshFitsHeadersList();
                
                FitsCountLabel.Text = $"{_allFitsHeaders.Count} properties";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading XISF metadata: {ex.Message}", "XISF Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void LoadExifMetadata()
        {
            try
            {
                // Use MetadataExtractor to read EXIF data
                var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(_fileItem.Path);
                
                _allFitsHeaders.Clear();
                
                // Filter to only include the most common/useful directories to speed up processing
                var relevantDirectoryTypes = new HashSet<string>
                {
                    "EXIF IFD0", "EXIF SubIFD", "GPS", "IPTC", "XMP", "ICC Profile", 
                    "JPEG", "PNG-IHDR", "PNG-tEXt", "TIFF", "File Type"
                };
                
                foreach (var directory in directories)
                {
                    // Skip less important directories for faster loading
                    if (!relevantDirectoryTypes.Contains(directory.Name) && 
                        !directory.Name.StartsWith("EXIF") && 
                        !directory.Name.StartsWith("GPS"))
                    {
                        continue;
                    }
                    
                    foreach (var tag in directory.Tags)
                    {
                        // Skip very long descriptions that might slow down the UI
                        var description = tag.Description ?? "";
                        if (description.Length > 500)
                        {
                            description = description.Substring(0, 500) + "...";
                        }
                        
                        var exifHeader = new FitsHeaderItem
                        {
                            Key = tag.Name,
                            Value = description,
                            Type = directory.Name, // Use directory name as type (e.g., "EXIF IFD0", "GPS")
                            Comment = $"Tag ID: {tag.Type}" // Show the tag ID as comment
                        };
                        _allFitsHeaders.Add(exifHeader);
                    }
                }
                
                // Sort headers alphabetically by key
                _allFitsHeaders.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
                
                // Initially show all headers
                RefreshFitsHeadersList();
                
                FitsCountLabel.Text = $"{_allFitsHeaders.Count} metadata tags";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading EXIF metadata: {ex.Message}", "EXIF Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void LoadArwMetadata()
        {
            try
            {
                var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(_fileItem.Path);

                _allFitsHeaders.Clear();

                var relevantDirectoryTypes = new HashSet<string>
                {
                    "EXIF IFD0", "EXIF SubIFD", "GPS", "IPTC", "XMP", "ICC Profile",
                    "JPEG", "PNG-IHDR", "PNG-tEXt", "TIFF", "File Type", "Sony Makernote"
                };

                foreach (var directory in directories)
                {
                    if (!relevantDirectoryTypes.Contains(directory.Name) &&
                        !directory.Name.StartsWith("EXIF", StringComparison.OrdinalIgnoreCase) &&
                        !directory.Name.StartsWith("GPS", StringComparison.OrdinalIgnoreCase) &&
                        !directory.Name.Contains("Sony", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (var tag in directory.Tags)
                    {
                        var description = tag.Description ?? string.Empty;
                        if (description.Length > 500)
                        {
                            description = description.Substring(0, 500) + "...";
                        }

                        var arwHeader = new FitsHeaderItem
                        {
                            Key = tag.Name,
                            Value = description,
                            Type = directory.Name,
                            Comment = $"Tag ID: {tag.Type}"
                        };
                        _allFitsHeaders.Add(arwHeader);
                    }
                }

                _allFitsHeaders.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
                RefreshFitsHeadersList();
                FitsCountLabel.Text = $"{_allFitsHeaders.Count} metadata tags";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading ARW metadata: {ex.Message}", "ARW Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void RefreshFitsHeadersList()
        {
            var searchText = FitsSearchBox.Text?.ToLowerInvariant() ?? "";
            
            _filteredFitsHeaders.Clear();
            
            var filtered = string.IsNullOrEmpty(searchText) ? 
                _allFitsHeaders : 
                _allFitsHeaders.Where(h => 
                    h.Key.ToLowerInvariant().Contains(searchText) || 
                    h.Value.ToLowerInvariant().Contains(searchText)).ToList();
            
            foreach (var header in filtered)
            {
                _filteredFitsHeaders.Add(header);
            }
            
            FitsCountLabel.Text = $"{_filteredFitsHeaders.Count} of {_allFitsHeaders.Count} headers";
        }

        private static bool IsFitsFile(string filePath)
        {
            try
            {
                return FitsUtilities.IsFitsFile(filePath);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsXisfFile(string filePath)
        {
            try
            {
                return XisfUtilities.IsXisfFile(filePath);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsStandardImageFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png" || 
                   extension == ".tiff" || extension == ".tif" || extension == ".bmp" ||
                   extension == ".gif" || extension == ".webp";
        }

        private static bool IsArwFile(string filePath)
        {
            return Path.GetExtension(filePath).Equals(".arw", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private static string GetFileTypeDescription(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".fits" or ".fit" or ".fts" => "FITS (Flexible Image Transport System)",
                ".xisf" => "XISF (Extensible Image Serialization Format)",
                ".jpg" or ".jpeg" => "JPEG Image",
                ".png" => "PNG Image",
                ".bmp" => "Bitmap Image",
                ".tiff" or ".tif" => "TIFF Image",
                ".gif" => "GIF Image",
                ".webp" => "WebP Image",
                ".arw" => "Sony ARW Raw Image",
                _ => $"{extension.ToUpperInvariant()} File"
            };
        }

        private static string GetValueTypeName(object? value)
        {
            return value switch
            {
                null => "null",
                string => "string",
                int => "int",
                long => "long", 
                float => "float",
                double => "double",
                bool => "bool",
                _ => value.GetType().Name
            };
        }

        private static bool HasAlphaChannel(System.Windows.Media.PixelFormat format)
        {
            // Check common pixel formats that have alpha channels
            return format == System.Windows.Media.PixelFormats.Bgra32 ||
                   format == System.Windows.Media.PixelFormats.Rgba64 ||
                   format == System.Windows.Media.PixelFormats.Prgba64 ||
                   format == System.Windows.Media.PixelFormats.Pbgra32 ||
                   format.ToString().ToLowerInvariant().Contains("alpha");
        }

        private void FitsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshFitsHeadersList();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Title = "Export Metadata",
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    FileName = $"{Path.GetFileNameWithoutExtension(_fileItem.Name)}_metadata.txt"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    ExportMetadataToFile(saveDialog.FileName);
                    System.Windows.MessageBox.Show($"Metadata exported to: {saveDialog.FileName}", "Export Complete", 
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error exporting metadata: {ex.Message}", "Export Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ExportMetadataToFile(string filePath)
        {
            using var writer = new StreamWriter(filePath);
            
            writer.WriteLine($"File Metadata Report");
            writer.WriteLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine(new string('=', 50));
            writer.WriteLine();
            
            // Basic file info
            writer.WriteLine("FILE INFORMATION:");
            writer.WriteLine($"Name: {_fileItem.Name}");
            writer.WriteLine($"Path: {_fileItem.Path}");
            
            if (_fileInfo != null)
            {
                writer.WriteLine($"Size: {FormatFileSize(_fileInfo.Length)}");
                writer.WriteLine($"Created: {_fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"Modified: {_fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"Type: {GetFileTypeDescription(_fileInfo.Extension)}");
            }
            writer.WriteLine();
            
            // Image properties if available
            if (_imageSource != null)
            {
                writer.WriteLine("IMAGE PROPERTIES:");
                writer.WriteLine($"Dimensions: {_imageSource.PixelWidth} × {_imageSource.PixelHeight}");
                writer.WriteLine($"Pixel Format: {_imageSource.Format}");
                writer.WriteLine($"DPI: {_imageSource.DpiX:F1} × {_imageSource.DpiY:F1}");
                writer.WriteLine($"Color Depth: {_imageSource.Format.BitsPerPixel} bits");
                writer.WriteLine($"Has Alpha: {(HasAlphaChannel(_imageSource.Format) ? "Yes" : "No")}");
                writer.WriteLine();
            }
            
            // Custom keywords if available
            if (_fileItem.CustomKeywords != null && _fileItem.CustomKeywords.Count > 0)
            {
                writer.WriteLine("CUSTOM KEYWORDS:");
                foreach (var kv in _fileItem.CustomKeywords)
                {
                    writer.WriteLine($"{kv.Key}: {kv.Value}");
                }
                writer.WriteLine();
            }
            
            // FITS headers if available
            if (_allFitsHeaders.Count > 0)
            {
                writer.WriteLine("FITS HEADERS:");
                writer.WriteLine($"{"Keyword",-20} {"Type",-10} {"Value",-30} Comment");
                writer.WriteLine(new string('-', 80));
                
                foreach (var header in _allFitsHeaders)
                {
                    writer.WriteLine($"{header.Key,-20} {header.Type,-10} {header.Value,-30} {header.Comment}");
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>
    /// Helper class for displaying FITS header information
    /// </summary>
    public class FitsHeaderItem
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public string Type { get; set; } = "";
        public string Comment { get; set; } = "";
    }

    /// <summary>
    /// Helper class for displaying key-value pairs
    /// </summary>
    public class KeyValueItem
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }
}