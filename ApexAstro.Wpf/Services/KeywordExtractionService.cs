using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ApexAstro.Core;
using ApexAstro.Utils;
using MetadataExtractor;

namespace ApexAstro.Wpf.Services
{
    public class KeywordExtractionService
    {
        public Dictionary<string, string> ExtractCustomKeywordsFromFilename(string filename, IEnumerable<string> keywords)
        {
            return FilenameParser.ExtractKeywordValues(filename, keywords);
        }

        public Dictionary<string, string> ExtractFitsKeywords(string filePath, IEnumerable<string> keywords, bool skipXisf = false)
        {
            var operationStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = new Dictionary<string, string>();
            var requestedKeywords = keywords.ToList();

            if (!File.Exists(filePath) || requestedKeywords.Count == 0)
                return result;

            try
            {
                // Process FITS files
                if (FitsUtilities.IsFitsFile(filePath))
                {
                    // Use the optimized header-only reader instead of reading entire file
                    var fitsHeaders = FitsParser.ParseHeaderFromFile(filePath);

                    foreach (var keyword in requestedKeywords)
                    {
                        if (TryGetKeywordValueWithAliases(fitsHeaders, keyword, out var matchedKey, out var value))
                        {
                            // Apply keyword-aware formatting (including compact RA/Dec conversion).
                            result[keyword] = FitsUtilities.FormatHeaderValue(matchedKey, value);
                        }
                    }
                }
                // Process XISF files - they store FITS keywords with "FITS_" prefix
                else if (!skipXisf && XisfUtilities.IsXisfFile(filePath))
                {
                    try
                    {
                        // Debug: Log what keywords we're looking for
                        var lookupKeywords = BuildAliasKeywordList(requestedKeywords);
                        var keywordList = string.Join(", ", lookupKeywords);
                        System.Diagnostics.Debug.WriteLine($"Extracting XISF keywords from {System.IO.Path.GetFileName(filePath)}: {keywordList}");
                        
                        // Use the optimized method that only extracts requested keywords via regex
                        // This is much faster than ParseMetadataFromFile which parses all properties
                        var xisfKeywords = XisfParser.ParseSpecificFitsKeywords(filePath, lookupKeywords);
                        
                        System.Diagnostics.Debug.WriteLine($"Found {xisfKeywords.Count} keywords in {System.IO.Path.GetFileName(filePath)}");
                        
                        foreach (var keyword in requestedKeywords)
                        {
                            if (TryGetKeywordValueWithAliases(xisfKeywords, keyword, out var matchedKey, out var matchedValue))
                            {
                                System.Diagnostics.Debug.WriteLine($"  {matchedKey} = {matchedValue}");
                                var formatted = XisfUtilities.FormatPropertyValue(matchedValue);
                                result[keyword] = FitsUtilities.FormatCoordinateKeywordValue(matchedKey, matchedValue, formatted);
                            }
                        }
                    }
                    catch (Exception xisfEx)
                    {
                        // Log XISF-specific errors with more detail
                        App.LoggingService?.LogError("XISF Keyword Extraction", $"Failed to extract XISF keywords from {System.IO.Path.GetFileName(filePath)}: {xisfEx.Message}", xisfEx);
                        throw;
                    }
                }
                // Skip all other file formats (JPG, PNG, TIFF, etc.) for FITS keyword extraction
                // These files don't have FITS-style keywords, so return empty result
            }
            catch (Exception ex)
            {
                // Log unexpected parsing errors (corrupted files, I/O errors, etc.)
                App.LoggingService?.LogError("Keyword Extraction", $"Failed to extract keywords from {System.IO.Path.GetFileName(filePath)}", ex);
            }
            finally
            {
                operationStopwatch.Stop();
                if (operationStopwatch.ElapsedMilliseconds > 3000)
                {
                    App.LoggingService?.LogWarning("FITS Keyword Extraction", $"'{System.IO.Path.GetFileName(filePath)}' took {operationStopwatch.ElapsedMilliseconds}ms (>3s threshold)");
                }
            }

            return result;
        }

        public Dictionary<string, string> ExtractArwKeywords(string filePath, IEnumerable<string> keywords)
        {
            var operationStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = new Dictionary<string, string>();
            var requestedKeywords = keywords.ToList();

            if (!File.Exists(filePath) || requestedKeywords.Count == 0 || !IsArwFile(filePath))
                return result;

            try
            {
                var directories = ImageMetadataReader.ReadMetadata(filePath);
                var metadataByNormalizedKey = BuildArwMetadataMap(directories);

                foreach (var keyword in requestedKeywords)
                {
                    foreach (var normalizedAlias in BuildArwAliasKeys(keyword))
                    {
                        if (metadataByNormalizedKey.TryGetValue(normalizedAlias, out var value) &&
                            !string.IsNullOrWhiteSpace(value))
                        {
                            result[keyword] = value;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("ARW Keyword Extraction", $"Failed to extract ARW metadata from {System.IO.Path.GetFileName(filePath)}", ex);
            }
            finally
            {
                operationStopwatch.Stop();
                if (operationStopwatch.ElapsedMilliseconds > 3000)
                {
                    App.LoggingService?.LogWarning("ARW Keyword Extraction", $"'{System.IO.Path.GetFileName(filePath)}' took {operationStopwatch.ElapsedMilliseconds}ms (>3s threshold)");
                }
            }

            return result;
        }

        private static bool TryGetKeywordValueWithAliases<T>(
            Dictionary<string, T> values,
            string requestedKeyword,
            out string matchedKey,
            out T matchedValue)
        {
            if (values.TryGetValue(requestedKeyword, out var exactValue))
            {
                matchedKey = requestedKeyword;
                matchedValue = exactValue;
                return true;
            }

            var requestedNormalized = NormalizeKeyword(requestedKeyword);
            foreach (var kvp in values)
            {
                if (NormalizeKeyword(kvp.Key) == requestedNormalized)
                {
                    matchedKey = kvp.Key;
                    matchedValue = kvp.Value;
                    return true;
                }
            }

            matchedKey = requestedKeyword;
            matchedValue = default!;
            return false;
        }

        private static List<string> BuildAliasKeywordList(IEnumerable<string> keywords)
        {
            var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                all.Add(keyword);

                if (keyword.Contains('_'))
                    all.Add(keyword.Replace('_', '-'));

                if (keyword.Contains('-'))
                    all.Add(keyword.Replace('-', '_'));
            }

            return all.ToList();
        }

        private static string NormalizeKeyword(string keyword)
        {
            return keyword.Replace("_", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
        }

        private static string NormalizeMetadataKey(string value)
        {
            var chars = value.Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray();
            return new string(chars);
        }

        private static bool IsArwFile(string filePath)
        {
            return string.Equals(System.IO.Path.GetExtension(filePath), ".arw", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> BuildArwMetadataMap(IEnumerable<MetadataExtractor.Directory> directories)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in directories)
            {
                foreach (var tag in directory.Tags)
                {
                    if (string.IsNullOrWhiteSpace(tag.Name))
                        continue;

                    string description = tag.Description ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(description))
                        continue;

                    var normalizedTagName = NormalizeMetadataKey(tag.Name);
                    if (!map.ContainsKey(normalizedTagName))
                    {
                        map[normalizedTagName] = description;
                    }
                }
            }

            return map;
        }

        private static readonly Dictionary<string, string[]> ArwAliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Make"] = new[] { "Make" },
            ["Model"] = new[] { "Model" },
            ["LensModel"] = new[] { "Lens Model" },
            ["FocalLength"] = new[] { "Focal Length" },
            ["FNumber"] = new[] { "F-Number", "Aperture Value" },
            ["ISO"] = new[] { "ISO Speed Ratings", "Photographic Sensitivity", "ISO" },
            ["ExposureTime"] = new[] { "Exposure Time", "Shutter Speed Value" },
            ["DateTimeOriginal"] = new[] { "Date/Time Original" },
            ["WhiteBalance"] = new[] { "White Balance" },
            ["Orientation"] = new[] { "Orientation" },
            ["ColorSpace"] = new[] { "Color Space" },
            ["ExposureCompensation"] = new[] { "Exposure Bias Value", "Exposure Compensation" },
            ["Width"] = new[] { "Image Width", "Exif Image Width" },
            ["Height"] = new[] { "Image Height", "Exif Image Height" },
            ["BitDepth"] = new[] { "Bits Per Sample", "Bit Depth" },
            ["BlackLevel"] = new[] { "Black Level" },
            ["WhiteLevel"] = new[] { "White Level" }
        };

        private static IEnumerable<string> BuildArwAliasKeys(string requestedKeyword)
        {
            // Always include the canonical keyword itself as the first lookup candidate.
            yield return NormalizeMetadataKey(requestedKeyword);

            if (ArwAliasMap.TryGetValue(requestedKeyword, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    yield return NormalizeMetadataKey(alias);
                }
            }
        }



        public void PopulateKeywords(Models.FileItem fileItem, IEnumerable<string> customKeywords, IEnumerable<string> fitsKeywords, bool skipXisf = false, IEnumerable<string>? arwKeywords = null)
        {
            PopulateKeywords(fileItem, customKeywords, fitsKeywords, skipXisf, calculateMedian: false, arwKeywords: arwKeywords);
        }

        public void PopulateKeywords(Models.FileItem fileItem, IEnumerable<string> customKeywords, IEnumerable<string> fitsKeywords, bool skipXisf, bool calculateMedian, IEnumerable<string>? arwKeywords = null)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // Debug: Log median calculation flag
            if (calculateMedian)
            {
                System.Diagnostics.Debug.WriteLine($"PopulateKeywords for {fileItem.Name}: calculateMedian=true");
            }
            
            // Extract custom keywords from filename
            if (customKeywords.Any())
            {
                var extractedCustom = ExtractCustomKeywordsFromFilename(fileItem.Name, customKeywords);
                fileItem.CustomKeywords = extractedCustom;
                
                // Debug log
                if (extractedCustom.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Extracted {extractedCustom.Count} custom keywords from {fileItem.Name}");
                }
            }

            // Extract FITS keywords from file headers
            if (fitsKeywords.Any())
            {
                var extractedFits = ExtractFitsKeywords(fileItem.Path, fitsKeywords, skipXisf);
                fileItem.FitsKeywords = extractedFits;
                
                // Debug log
                if (extractedFits.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Extracted {extractedFits.Count} FITS keywords from {fileItem.Name}");
                }
            }

            if (arwKeywords != null && arwKeywords.Any())
            {
                fileItem.ArwKeywords = ExtractArwKeywords(fileItem.Path, arwKeywords);
            }
            
            // Calculate median and mean values for the image (only if requested)
            if (calculateMedian)
            {
                var (median, mean) = CalculateStats(fileItem.Path);
                fileItem.Median = median;
                fileItem.Mean = mean;
            }
            
            stopwatch.Stop();
            
            // Log all files asynchronously to avoid blocking
            var ext = System.IO.Path.GetExtension(fileItem.Name).ToUpperInvariant();
            var elapsed = stopwatch.ElapsedMilliseconds;
            var customCount = fileItem.CustomKeywords?.Count ?? 0;
            var fitsCount = fileItem.FitsKeywords?.Count ?? 0;
            var arwCount = fileItem.ArwKeywords?.Count ?? 0;
            
            // Queue the log entry instead of writing synchronously
            System.Threading.Tasks.Task.Run(() => 
            {
                App.LoggingService?.LogInfo($"Loaded metadata: {fileItem.Name} ({ext}) in {elapsed}ms - {customCount} custom, {fitsCount} FITS, {arwCount} ARW keywords");
            });
        }

        /// <summary>
        /// Calculate the median pixel value for an image file (0.0-1.0 range).
        /// Public method for calculating median on demand.
        /// </summary>
        public double? CalculateMedianForFile(string filePath)
        {
            return CalculateStats(filePath).median;
        }

        /// <summary>
        /// Calculate both median and mean pixel values for an image file (0.0-1.0 range).
        /// Public method for calculating stats on demand.
        /// </summary>
        public (double? median, double? mean) CalculateStatsForFile(string filePath)
        {
            return CalculateStats(filePath);
        }

        /// <summary>
        /// Calculate both median and mean pixel values for an image file (0.0-1.0 range).
        /// Supports FITS, XISF, and standard image formats.
        /// </summary>
        private (double? median, double? mean) CalculateStats(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    App.LoggingService?.LogWarning("Median Calculation", $"File not found: {System.IO.Path.GetFileName(filePath)}");
                    return (null, null);
                }

                // Handle FITS files
                if (FitsUtilities.IsFitsFile(filePath))
                {
                    byte[] bytes = File.ReadAllBytes(filePath);
                    if (!FitsUtilities.IsFitsData(bytes))
                    {
                        App.LoggingService?.LogWarning("Median Calculation", $"Invalid FITS data: {System.IO.Path.GetFileName(filePath)}");
                        return (null, null);
                    }

                    var (median, mean) = ApexAstro.Core.FitsParser.CalculateStats(bytes);
                    System.Diagnostics.Debug.WriteLine($"Calculated stats for FITS {System.IO.Path.GetFileName(filePath)}: median={median:F8}, mean={mean:F8}");
                    return (median, mean);
                }
                // Handle XISF files
                else if (XisfUtilities.IsXisfFile(filePath))
                {
                    byte[] bytes = File.ReadAllBytes(filePath);
                    var (median, mean) = ApexAstro.Utils.XisfParser.CalculateStats(bytes);
                    System.Diagnostics.Debug.WriteLine($"Calculated stats for XISF {System.IO.Path.GetFileName(filePath)}: median={median:F8}, mean={mean:F8}");
                    return (median, mean);
                }
                // Handle standard image formats (JPEG, PNG, etc.)
                else
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage(new Uri(filePath));
                    var (median, mean) = CalculateStatsFromBitmap(bitmap);
                    System.Diagnostics.Debug.WriteLine($"Calculated stats for image {System.IO.Path.GetFileName(filePath)}: median={median:F8}, mean={mean:F8}");
                    return (median, mean);
                }
            }
            catch (Exception ex)
            {
                // Log but don't throw - stats are optional
                App.LoggingService?.LogWarning("Median Calculation", $"Failed to calculate stats for {System.IO.Path.GetFileName(filePath)}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stats calculation error for {System.IO.Path.GetFileName(filePath)}: {ex.Message}");
                return (null, null);
            }
        }

        /// <summary>
        /// Calculate median from byte array (0-255 range) normalized to 0.0-1.0
        /// </summary>
        private (double median, double mean) CalculateStatsFromBytes(byte[] pixels)
        {
            if (pixels == null || pixels.Length == 0)
                return (0.0, 0.0);

            // Calculate mean in one pass
            double sum = 0.0;
            for (int i = 0; i < pixels.Length; i++)
                sum += pixels[i];
            double mean = sum / pixels.Length / 255.0;

            var sorted = pixels.OrderBy(p => p).ToArray();
            int middle = sorted.Length / 2;
            
            double median;
            if (sorted.Length % 2 == 0)
            {
                median = (sorted[middle - 1] + sorted[middle]) / 2.0;
            }
            else
            {
                median = sorted[middle];
            }
            
            // Normalize to 0.0-1.0 range
            return (median / 255.0, mean);
        }

        /// <summary>
        /// Calculate both median and mean from a BitmapSource (standard image)
        /// </summary>
        private (double? median, double? mean) CalculateStatsFromBitmap(System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            if (bitmap == null)
                return (null, null);

            try
            {
                // Convert bitmap to a known format (Bgra32) for consistent pixel reading
                var convertedBitmap = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                    bitmap, 
                    System.Windows.Media.PixelFormats.Bgra32, 
                    null, 
                    0);

                int width = convertedBitmap.PixelWidth;
                int height = convertedBitmap.PixelHeight;
                int stride = width * 4; // 4 bytes per pixel (BGRA)
                byte[] pixels = new byte[stride * height];
                
                convertedBitmap.CopyPixels(pixels, stride, 0);
                
                // Convert BGRA to grayscale and calculate stats
                var grayscalePixels = new List<byte>(width * height);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    // Standard grayscale conversion (ITU-R BT.601)
                    byte gray = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                    grayscalePixels.Add(gray);
                }
                
                var (median, mean) = CalculateStatsFromBytes(grayscalePixels.ToArray());
                return (median, mean);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in CalculateStatsFromBitmap: {ex.Message}");
                return (null, null);
            }
        }
    }
}