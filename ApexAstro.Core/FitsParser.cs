using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ApexAstro.Core
{
    /// <summary>
    /// Professional-grade FITS parser with enhanced capabilities and robust error handling.
    /// Provides comprehensive FITS file support including all standard data types, scaling parameters,
    /// and astronomical metadata extraction while maintaining full FITS standard compliance.
    /// </summary>
    public static class FitsParser
    {
        /// <summary>
        /// Parse FITS header from byte buffer and return header dictionary
        /// </summary>
        public static Dictionary<string, object> ParseHeader(byte[] buffer)
        {
            var (header, _) = ParseHeaderWithSize(buffer);
            return header;
        }

        /// <summary>
        /// Parse FITS header directly from file, reading only the header blocks.
        /// This is much faster than reading the entire file for large FITS files.
        /// </summary>
        /// <param name="filePath">Path to the FITS file</param>
        /// <returns>Dictionary of header keywords and values</returns>
        public static Dictionary<string, object> ParseHeaderFromFile(string filePath)
        {
            const int blockSize = 2880;  // FITS standard block size
            const int maxBlocks = 100;   // Maximum header blocks to read (288KB max)
            
            var header = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            
            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, blockSize))
                {
                    byte[] blockBuffer = new byte[blockSize];
                    
                    for (int blockIndex = 0; blockIndex < maxBlocks; blockIndex++)
                    {
                        // Read one FITS block
                        int bytesRead = fileStream.Read(blockBuffer, 0, blockSize);
                        if (bytesRead < blockSize)
                            break; // End of file or incomplete block
                        
                        // Parse this block
                        var blockString = Encoding.ASCII.GetString(blockBuffer);
                        bool foundEnd = ParseHeaderBlock(blockString, header);
                        
                        if (foundEnd)
                            break; // Found END card, header is complete
                    }
                }
            }
            catch (Exception)
            {
                // Return partial header on error
            }
            
            return header;
        }

        /// <summary>
        /// Parse a single FITS header block (2880 bytes) and add keywords to the header dictionary.
        /// Returns true if END card is found.
        /// </summary>
        private static bool ParseHeaderBlock(string blockString, Dictionary<string, object> header)
        {
            const int cardSize = 80;     // FITS standard card size
            const int blockSize = 2880;  // FITS standard block size
            
            for (int cardStart = 0; cardStart < blockSize; cardStart += cardSize)
            {
                if (cardStart + cardSize > blockSize) break;
                
                var card = blockString.Substring(cardStart, cardSize);
                
                // Check for END card
                if (card.StartsWith("END") && (card.Length < 4 || card[3] == ' '))
                {
                    return true; // Found END card
                }
                
                // Parse the card and add to header
                ParseCard(card, header);
            }
            
            return false; // END card not found in this block
        }

        /// <summary>
        /// Parse a single FITS header card (80 characters) and add to header dictionary
        /// </summary>
        private static void ParseCard(string card, Dictionary<string, object> header)
        {
            try
            {
                // Skip blank lines and comment-only lines
                if (string.IsNullOrWhiteSpace(card) || card.StartsWith("COMMENT") || card.StartsWith("HISTORY"))
                    return;
                
                // Extract keyword (first 8 characters, trimmed)
                if (card.Length < 8) return;
                var keyword = card.Substring(0, 8).Trim();
                if (string.IsNullOrEmpty(keyword)) return;
                
                // Check for value indicator (= in position 8)
                if (card.Length < 10 || card[8] != '=') return;
                
                // Extract value and comment
                var valueAndComment = card.Substring(10); // After "KEYWORD = "
                var value = ExtractValue(valueAndComment);
                
                if (value != null && !header.ContainsKey(keyword))
                {
                    header[keyword] = value;
                }
            }
            catch (Exception)
            {
                // Skip malformed cards
            }
        }

        /// <summary>
        /// Extract value from FITS card value section
        /// </summary>
        private static object? ExtractValue(string valueAndComment)
        {
            var trimmed = valueAndComment.Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;
            
            // String value (enclosed in single quotes)
            if (trimmed.StartsWith("'"))
            {
                var endQuote = trimmed.IndexOf("'", 1);
                if (endQuote > 0)
                {
                    return trimmed.Substring(1, endQuote - 1).Trim();
                }
                return trimmed.Substring(1).Trim();
            }
            
            // Remove inline comment (anything after /)
            var commentPos = trimmed.IndexOf('/');
            if (commentPos >= 0)
            {
                trimmed = trimmed.Substring(0, commentPos).Trim();
            }
            
            // Boolean value
            if (trimmed == "T") return true;
            if (trimmed == "F") return false;
            
            // Numeric value
            if (int.TryParse(trimmed, out int intVal)) return intVal;
            if (double.TryParse(trimmed, out double doubleVal)) return doubleVal;
            
            // Return as string if nothing else matches
            return trimmed;
        }

        /// <summary>
        /// Enhanced FITS header parser with comprehensive support for all FITS header types
        /// and robust error handling
        /// </summary>
        public static (Dictionary<string, object> header, int headerSize) ParseHeaderWithSize(byte[] buffer)
        {
            const int blockSize = 2880;  // FITS standard block size
            const int cardSize = 80;     // FITS standard card size
            
            var header = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            int totalHeaderSize = 0;
            bool foundEnd = false;
            
            try
            {
                // Process up to 100 blocks (288KB) to handle very large headers
                for (int blockIndex = 0; blockIndex < 100 && (blockIndex * blockSize) < buffer.Length; blockIndex++)
                {
                    int blockOffset = blockIndex * blockSize;
                    if (blockOffset + blockSize > buffer.Length) break;
                    
                    var blockBytes = new byte[blockSize];
                    Array.Copy(buffer, blockOffset, blockBytes, 0, blockSize);
                    var blockString = Encoding.ASCII.GetString(blockBytes);
                    
                    // Process each 80-character card in the block
                    for (int cardStart = 0; cardStart < blockSize; cardStart += cardSize)
                    {
                        if (cardStart + cardSize > blockSize) break;
                        
                        var card = blockString.Substring(cardStart, cardSize);
                        
                        // Check for END card
                        if (card.StartsWith("END") && (card.Length < 4 || card[3] == ' '))
                        {
                            totalHeaderSize = blockOffset + blockSize;
                            foundEnd = true;
                            break;
                        }
                        
                        // Parse the card
                        var parsedCard = ParseFitsCard(card);
                        if (parsedCard.HasValue)
                        {
                            var (keyword, value, comment) = parsedCard.Value;
                            
                            // Store the value, allowing extension headers to override primary
                            header[keyword] = value;
                            
                            // Also store comments for special keywords
                            if (!string.IsNullOrEmpty(comment) && 
                                (keyword == "COMMENT" || keyword == "HISTORY" || keyword == ""))
                            {
                                var commentKey = string.IsNullOrEmpty(keyword) ? "BLANK" : keyword;
                                if (header.ContainsKey($"{commentKey}_COMMENTS"))
                                {
                                    header[$"{commentKey}_COMMENTS"] += "\n" + comment;
                                }
                                else
                                {
                                    header[$"{commentKey}_COMMENTS"] = comment;
                                }
                            }
                        }
                    }
                    
                    if (foundEnd) break;
                }
                
                // If no END card found, estimate header size
                if (totalHeaderSize == 0)
                {
                    totalHeaderSize = blockSize; // Minimum one block
                }
                
                return (header, totalHeaderSize);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse FITS header: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Enhanced FITS image reader with comprehensive data type support and proper scaling
        /// </summary>
        public static (int width, int height, byte[] pixels) ReadImage(byte[] buffer)
        {
            try
            {
                var (header, headerSize) = ParseHeaderWithSize(buffer);
                
                // Validate required header keywords
                if (!header.TryGetValue("NAXIS", out var naxisObj))
                    throw new InvalidOperationException("Missing NAXIS keyword in FITS header");
                
                int naxis = Convert.ToInt32(naxisObj);
                if (naxis < 2)
                    throw new InvalidOperationException($"FITS file has {naxis} dimensions, need at least 2 for image data");
                
                if (!header.TryGetValue("NAXIS1", out var wObj) || !header.TryGetValue("NAXIS2", out var hObj))
                    throw new InvalidOperationException("Missing NAXIS1/NAXIS2 in FITS header");
                
                int width = Convert.ToInt32(wObj);
                int height = Convert.ToInt32(hObj);
                
                if (width <= 0 || height <= 0)
                    throw new InvalidOperationException($"Invalid image dimensions: {width}x{height}");
                
                if (!header.TryGetValue("BITPIX", out var bitpixObj))
                    throw new InvalidOperationException("Missing BITPIX in FITS header");
                
                int bitpix = Convert.ToInt32(bitpixObj);
                
                // Get scaling parameters (FITS standard)
                double bzero = header.TryGetValue("BZERO", out var bzeroObj) ? Convert.ToDouble(bzeroObj) : 0.0;
                double bscale = header.TryGetValue("BSCALE", out var bscaleObj) ? Convert.ToDouble(bscaleObj) : 1.0;
                
                // Calculate expected data size
                int bytesPerPixel = Math.Abs(bitpix) / 8;
                long expectedDataSize = (long)width * height * bytesPerPixel;
                
                if (buffer.Length < headerSize + expectedDataSize)
                    throw new ArgumentOutOfRangeException(
                        $"FITS buffer too small: expected {headerSize + expectedDataSize} bytes, got {buffer.Length}");
                
                // Convert image data to byte array
                var pixels = new byte[width * height];
                ConvertImageData(buffer, headerSize, pixels, width, height, bitpix, bzero, bscale);
                
                return (width, height, pixels);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read FITS image: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Read FITS image data into scaled floating-point pixels suitable for quantitative analysis.
        /// Values are returned after FITS BSCALE/BZERO are applied.
        /// </summary>
        public static (int width, int height, float[] pixels, Dictionary<string, object> header) ReadImageAsFloat(byte[] buffer)
        {
            try
            {
                var (header, headerSize) = ParseHeaderWithSize(buffer);

                if (!header.TryGetValue("NAXIS", out var naxisObj))
                    throw new InvalidOperationException("Missing NAXIS keyword in FITS header");

                int naxis = Convert.ToInt32(naxisObj);
                if (naxis < 2)
                    throw new InvalidOperationException($"FITS file has {naxis} dimensions, need at least 2 for image data");

                if (!header.TryGetValue("NAXIS1", out var wObj) || !header.TryGetValue("NAXIS2", out var hObj))
                    throw new InvalidOperationException("Missing NAXIS1/NAXIS2 in FITS header");

                int width = Convert.ToInt32(wObj);
                int height = Convert.ToInt32(hObj);

                if (width <= 0 || height <= 0)
                    throw new InvalidOperationException($"Invalid image dimensions: {width}x{height}");

                if (!header.TryGetValue("BITPIX", out var bitpixObj))
                    throw new InvalidOperationException("Missing BITPIX in FITS header");

                int bitpix = Convert.ToInt32(bitpixObj);
                double bzero = header.TryGetValue("BZERO", out var bzeroObj) ? Convert.ToDouble(bzeroObj) : 0.0;
                double bscale = header.TryGetValue("BSCALE", out var bscaleObj) ? Convert.ToDouble(bscaleObj) : 1.0;

                int bytesPerPixel = Math.Abs(bitpix) / 8;
                long expectedDataSize = (long)width * height * bytesPerPixel;
                if (buffer.Length < headerSize + expectedDataSize)
                {
                    throw new ArgumentOutOfRangeException(
                        $"FITS buffer too small: expected {headerSize + expectedDataSize} bytes, got {buffer.Length}");
                }

                var pixels = new float[width * height];
                ConvertImageDataToFloat(buffer, headerSize, pixels, width, height, bitpix, bzero, bscale);
                return (width, height, pixels, header);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read FITS image as float: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Parse a single FITS header card (80 characters)
        /// </summary>
        private static (string keyword, object value, string comment)? ParseFitsCard(string card)
        {
            if (string.IsNullOrWhiteSpace(card) || card.Length != 80)
                return null;
            
            // Extract keyword (first 8 characters, trimmed)
            string keyword = card.Substring(0, 8).Trim();
            
            if (string.IsNullOrEmpty(keyword))
                return null;
            
            // Handle special cases
            if (keyword == "COMMENT" || keyword == "HISTORY")
            {
                string comment = card.Substring(8).Trim();
                return (keyword, comment, comment);
            }
            
            // Check for value indicator (column 8 should be '=')
            if (card.Length > 8 && card[8] == '=')
            {
                // Extract value and comment
                string valueAndComment = card.Substring(9).Trim();
                string valueStr = valueAndComment;
                string comment = "";
                
                // Find comment separator
                int commentStart = FindCommentStart(valueAndComment);
                if (commentStart >= 0)
                {
                    valueStr = valueAndComment.Substring(0, commentStart).Trim();
                    comment = valueAndComment.Substring(commentStart + 1).Trim();
                }
                
                // Parse the value
                object value = ParseFitsValue(valueStr);
                return (keyword, value, comment);
            }
            else
            {
                // No value, treat as comment
                string comment = card.Substring(8).Trim();
                return (keyword, "", comment);
            }
        }

        /// <summary>
        /// Find the start of a comment in a FITS value string
        /// </summary>
        private static int FindCommentStart(string valueStr)
        {
            bool inQuotes = false;
            for (int i = 0; i < valueStr.Length; i++)
            {
                if (valueStr[i] == '\'')
                {
                    inQuotes = !inQuotes;
                }
                else if (valueStr[i] == '/' && !inQuotes)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Parse a FITS value string to appropriate .NET type
        /// </summary>
        private static object ParseFitsValue(string valueStr)
        {
            if (string.IsNullOrWhiteSpace(valueStr))
                return "";
            
            valueStr = valueStr.Trim();
            
            // Handle quoted strings
            if (valueStr.StartsWith("'"))
            {
                if (valueStr.EndsWith("'") && valueStr.Length >= 2)
                {
                    return valueStr.Substring(1, valueStr.Length - 2);
                }
                else
                {
                    // Malformed quoted string, return as-is
                    return valueStr;
                }
            }
            
            // Handle boolean values
            if (valueStr.Equals("T", StringComparison.OrdinalIgnoreCase))
                return true;
            if (valueStr.Equals("F", StringComparison.OrdinalIgnoreCase))
                return false;
            
            // Handle numeric values
            if (long.TryParse(valueStr, out long longValue))
            {
                // Return as int if it fits, otherwise as long
                if (longValue >= int.MinValue && longValue <= int.MaxValue)
                    return (int)longValue;
                else
                    return longValue;
            }
            
            if (double.TryParse(valueStr, out double doubleValue))
            {
                return doubleValue;
            }
            
            // Return as string if all else fails
            return valueStr;
        }

        /// <summary>
        /// Convert FITS image data to byte array with comprehensive type support
        /// </summary>
        private static void ConvertImageData(byte[] buffer, int dataOffset, byte[] pixels, 
            int width, int height, int bitpix, double bzero, double bscale)
        {
            switch (bitpix)
            {
                case 8:   // Unsigned 8-bit integer
                    ConvertUInt8Data(buffer, dataOffset, pixels, bzero, bscale);
                    break;
                case 16:  // Signed 16-bit integer
                    ConvertInt16Data(buffer, dataOffset, pixels, width, height, bzero, bscale);
                    break;
                case 32:  // Signed 32-bit integer
                    ConvertInt32Data(buffer, dataOffset, pixels, width, height, bzero, bscale);
                    break;
                case -32: // 32-bit floating point
                    ConvertFloat32Data(buffer, dataOffset, pixels, width, height, bzero, bscale);
                    break;
                case -64: // 64-bit floating point
                    ConvertFloat64Data(buffer, dataOffset, pixels, width, height, bzero, bscale);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported BITPIX value: {bitpix}");
            }
        }

        private static void ConvertImageDataToFloat(byte[] buffer, int dataOffset, float[] pixels,
            int width, int height, int bitpix, double bzero, double bscale)
        {
            int pixelCount = width * height;
            switch (bitpix)
            {
                case 8:
                    for (int i = 0; i < pixelCount && dataOffset + i < buffer.Length; i++)
                    {
                        pixels[i] = (float)(buffer[dataOffset + i] * bscale + bzero);
                    }
                    break;

                case 16:
                    for (int i = 0; i < pixelCount && dataOffset + i * 2 + 1 < buffer.Length; i++)
                    {
                        short rawValue = (short)((buffer[dataOffset + i * 2] << 8) | buffer[dataOffset + i * 2 + 1]);
                        pixels[i] = (float)(rawValue * bscale + bzero);
                    }
                    break;

                case 32:
                    for (int i = 0; i < pixelCount && dataOffset + i * 4 + 3 < buffer.Length; i++)
                    {
                        int rawValue = (buffer[dataOffset + i * 4] << 24) |
                                       (buffer[dataOffset + i * 4 + 1] << 16) |
                                       (buffer[dataOffset + i * 4 + 2] << 8) |
                                        buffer[dataOffset + i * 4 + 3];
                        pixels[i] = (float)(rawValue * bscale + bzero);
                    }
                    break;

                case -32:
                    for (int i = 0; i < pixelCount && dataOffset + i * 4 + 3 < buffer.Length; i++)
                    {
                        int bits = (buffer[dataOffset + i * 4] << 24) |
                                   (buffer[dataOffset + i * 4 + 1] << 16) |
                                   (buffer[dataOffset + i * 4 + 2] << 8) |
                                    buffer[dataOffset + i * 4 + 3];
                        float rawValue = BitConverter.Int32BitsToSingle(bits);
                        pixels[i] = float.IsFinite(rawValue) ? (float)(rawValue * bscale + bzero) : float.NaN;
                    }
                    break;

                case -64:
                    for (int i = 0; i < pixelCount && dataOffset + i * 8 + 7 < buffer.Length; i++)
                    {
                        long bits = ((long)buffer[dataOffset + i * 8] << 56) |
                                    ((long)buffer[dataOffset + i * 8 + 1] << 48) |
                                    ((long)buffer[dataOffset + i * 8 + 2] << 40) |
                                    ((long)buffer[dataOffset + i * 8 + 3] << 32) |
                                    ((long)buffer[dataOffset + i * 8 + 4] << 24) |
                                    ((long)buffer[dataOffset + i * 8 + 5] << 16) |
                                    ((long)buffer[dataOffset + i * 8 + 6] << 8) |
                                     buffer[dataOffset + i * 8 + 7];

                        double rawValue = BitConverter.Int64BitsToDouble(bits);
                        pixels[i] = double.IsFinite(rawValue) ? (float)(rawValue * bscale + bzero) : float.NaN;
                    }
                    break;

                default:
                    throw new NotSupportedException($"Unsupported BITPIX value: {bitpix}");
            }
        }

        private static void ConvertUInt8Data(byte[] buffer, int offset, byte[] pixels, double bzero, double bscale)
        {
            for (int i = 0; i < pixels.Length && offset + i < buffer.Length; i++)
            {
                double val = buffer[offset + i] * bscale + bzero;
                pixels[i] = (byte)Math.Clamp(val, 0, 255);
            }
        }

        private static void ConvertInt16Data(byte[] buffer, int offset, byte[] pixels, int width, int height, 
            double bzero, double bscale)
        {
            int pixelCount = width * height;
            var values = new double[pixelCount];
            double min = double.MaxValue, max = double.MinValue;
            
            // Read and scale all values, find min/max
            for (int i = 0; i < pixelCount && offset + i * 2 + 1 < buffer.Length; i++)
            {
                // FITS uses big-endian byte order
                short rawValue = (short)((buffer[offset + i * 2] << 8) | buffer[offset + i * 2 + 1]);
                double scaledValue = rawValue * bscale + bzero;
                values[i] = scaledValue;
                
                if (scaledValue < min) min = scaledValue;
                if (scaledValue > max) max = scaledValue;
            }
            
            // Convert to 0-255 range
            double range = max - min;
            if (range > 0)
            {
                for (int i = 0; i < pixelCount; i++)
                {
                    int scaled = (int)((values[i] - min) * 255.0 / range);
                    pixels[i] = (byte)Math.Clamp(scaled, 0, 255);
                }
            }
            else
            {
                // All values are the same
                byte constValue = (byte)Math.Clamp(values[0], 0, 255);
                Array.Fill(pixels, constValue);
            }
        }

        private static void ConvertInt32Data(byte[] buffer, int offset, byte[] pixels, int width, int height, 
            double bzero, double bscale)
        {
            int pixelCount = width * height;
            var values = new double[pixelCount];
            double min = double.MaxValue, max = double.MinValue;
            
            // Read and scale all values, find min/max
            for (int i = 0; i < pixelCount && offset + i * 4 + 3 < buffer.Length; i++)
            {
                // FITS uses big-endian byte order
                int rawValue = (buffer[offset + i * 4] << 24) | 
                              (buffer[offset + i * 4 + 1] << 16) | 
                              (buffer[offset + i * 4 + 2] << 8) | 
                               buffer[offset + i * 4 + 3];
                
                double scaledValue = rawValue * bscale + bzero;
                values[i] = scaledValue;
                
                if (scaledValue < min) min = scaledValue;
                if (scaledValue > max) max = scaledValue;
            }
            
            // Convert to 0-255 range
            double range = max - min;
            if (range > 0)
            {
                for (int i = 0; i < pixelCount; i++)
                {
                    int scaled = (int)((values[i] - min) * 255.0 / range);
                    pixels[i] = (byte)Math.Clamp(scaled, 0, 255);
                }
            }
            else
            {
                byte constValue = (byte)Math.Clamp(values[0], 0, 255);
                Array.Fill(pixels, constValue);
            }
        }

        private static void ConvertFloat32Data(byte[] buffer, int offset, byte[] pixels, int width, int height, 
            double bzero, double bscale)
        {
            int pixelCount = width * height;
            var values = new double[pixelCount];
            double min = double.MaxValue, max = double.MinValue;
            
            // Read and scale all values, find min/max
            for (int i = 0; i < pixelCount && offset + i * 4 + 3 < buffer.Length; i++)
            {
                // Convert big-endian bytes to float
                byte[] floatBytes = new byte[4];
                floatBytes[3] = buffer[offset + i * 4];
                floatBytes[2] = buffer[offset + i * 4 + 1];
                floatBytes[1] = buffer[offset + i * 4 + 2];
                floatBytes[0] = buffer[offset + i * 4 + 3];
                
                float rawValue = BitConverter.ToSingle(floatBytes, 0);
                
                if (float.IsFinite(rawValue))
                {
                    double scaledValue = rawValue * bscale + bzero;
                    values[i] = scaledValue;
                    
                    if (scaledValue < min) min = scaledValue;
                    if (scaledValue > max) max = scaledValue;
                }
                else
                {
                    values[i] = 0; // Handle NaN/Infinity
                }
            }
            
            // Convert to 0-255 range
            double range = max - min;
            if (range > 0)
            {
                for (int i = 0; i < pixelCount; i++)
                {
                    int scaled = (int)((values[i] - min) * 255.0 / range);
                    pixels[i] = (byte)Math.Clamp(scaled, 0, 255);
                }
            }
            else
            {
                byte constValue = (byte)Math.Clamp(min, 0, 255);
                Array.Fill(pixels, constValue);
            }
        }

        private static void ConvertFloat64Data(byte[] buffer, int offset, byte[] pixels, int width, int height, 
            double bzero, double bscale)
        {
            int pixelCount = width * height;
            var values = new double[pixelCount];
            double min = double.MaxValue, max = double.MinValue;
            
            // Read and scale all values, find min/max
            for (int i = 0; i < pixelCount && offset + i * 8 + 7 < buffer.Length; i++)
            {
                // Convert big-endian bytes to double
                byte[] doubleBytes = new byte[8];
                for (int j = 0; j < 8; j++)
                {
                    doubleBytes[7 - j] = buffer[offset + i * 8 + j];
                }
                
                double rawValue = BitConverter.ToDouble(doubleBytes, 0);
                
                if (double.IsFinite(rawValue))
                {
                    double scaledValue = rawValue * bscale + bzero;
                    values[i] = scaledValue;
                    
                    if (scaledValue < min) min = scaledValue;
                    if (scaledValue > max) max = scaledValue;
                }
                else
                {
                    values[i] = 0; // Handle NaN/Infinity
                }
            }
            
            // Convert to 0-255 range
            double range = max - min;
            if (range > 0)
            {
                for (int i = 0; i < pixelCount; i++)
                {
                    int scaled = (int)((values[i] - min) * 255.0 / range);
                    pixels[i] = (byte)Math.Clamp(scaled, 0, 255);
                }
            }
            else
            {
                byte constValue = (byte)Math.Clamp(min, 0, 255);
                Array.Fill(pixels, constValue);
            }
        }

        /// <summary>
        /// Calculate the median pixel value from FITS data.
        /// Returns the median in the 0.0-1.0 range based on the actual pixel values
        /// after applying BZERO and BSCALE, normalized by the theoretical maximum for the bit depth.
        /// </summary>
        public static double? CalculateMedian(byte[] buffer)
        {
            var (median, _) = CalculateStats(buffer);
            return median;
        }

        /// <summary>
        /// Calculate both median and mean pixel values from FITS data in a single pass.
        /// Returns both values in the 0.0-1.0 range.
        /// </summary>
        public static (double? median, double? mean) CalculateStats(byte[] buffer)
        {
            try
            {
                var (header, headerSize) = ParseHeaderWithSize(buffer);
                
                // Validate required header keywords
                if (!header.TryGetValue("NAXIS", out var naxisObj))
                    return (null, null);
                
                int naxis = Convert.ToInt32(naxisObj);
                if (naxis < 2)
                    return (null, null);
                
                if (!header.TryGetValue("NAXIS1", out var wObj) || !header.TryGetValue("NAXIS2", out var hObj))
                    return (null, null);
                
                int width = Convert.ToInt32(wObj);
                int height = Convert.ToInt32(hObj);
                
                if (width <= 0 || height <= 0)
                    return (null, null);
                
                if (!header.TryGetValue("BITPIX", out var bitpixObj))
                    return (null, null);
                
                int bitpix = Convert.ToInt32(bitpixObj);
                
                // Get scaling parameters (FITS standard)
                double bzero = header.TryGetValue("BZERO", out var bzeroObj) ? Convert.ToDouble(bzeroObj) : 0.0;
                double bscale = header.TryGetValue("BSCALE", out var bscaleObj) ? Convert.ToDouble(bscaleObj) : 1.0;
                
                // Calculate expected data size
                int bytesPerPixel = Math.Abs(bitpix) / 8;
                long expectedDataSize = (long)width * height * bytesPerPixel;
                
                if (buffer.Length < headerSize + expectedDataSize)
                    return (null, null);
                
                // Read pixel values and calculate both median and mean
                return CalculateStatsFromRawData(buffer, headerSize, width, height, bitpix, bzero, bscale);
            }
            catch (Exception)
            {
                return (null, null);
            }
        }

        /// <summary>
        /// Calculate median from raw FITS data
        /// </summary>
        private static double CalculateMedianFromRawData(byte[] buffer, int dataOffset, 
            int width, int height, int bitpix, double bzero, double bscale)
        {
            var (median, _) = CalculateStatsFromRawData(buffer, dataOffset, width, height, bitpix, bzero, bscale);
            return median;
        }

        /// <summary>
        /// Calculate both median and mean from raw FITS data in a single pass.
        /// </summary>
        private static (double median, double mean) CalculateStatsFromRawData(byte[] buffer, int dataOffset,
            int width, int height, int bitpix, double bzero, double bscale)
        {
            int pixelCount = width * height;
            var values = new List<double>(pixelCount);
            
            switch (bitpix)
            {
                case 8:   // Unsigned 8-bit integer
                    for (int i = 0; i < pixelCount && dataOffset + i < buffer.Length; i++)
                    {
                        double val = buffer[dataOffset + i] * bscale + bzero;
                        values.Add(val);
                    }
                    break;
                    
                case 16:  // Signed 16-bit integer
                    for (int i = 0; i < pixelCount && dataOffset + i * 2 + 1 < buffer.Length; i++)
                    {
                        // FITS uses big-endian byte order
                        short rawValue = (short)((buffer[dataOffset + i * 2] << 8) | buffer[dataOffset + i * 2 + 1]);
                        double scaledValue = rawValue * bscale + bzero;
                        values.Add(scaledValue);
                    }
                    break;
                    
                case 32:  // Signed 32-bit integer
                    for (int i = 0; i < pixelCount && dataOffset + i * 4 + 3 < buffer.Length; i++)
                    {
                        // FITS uses big-endian byte order
                        int rawValue = (buffer[dataOffset + i * 4] << 24) | 
                                      (buffer[dataOffset + i * 4 + 1] << 16) | 
                                      (buffer[dataOffset + i * 4 + 2] << 8) | 
                                       buffer[dataOffset + i * 4 + 3];
                        
                        double scaledValue = rawValue * bscale + bzero;
                        values.Add(scaledValue);
                    }
                    break;
                    
                case -32: // 32-bit floating point
                    for (int i = 0; i < pixelCount && dataOffset + i * 4 + 3 < buffer.Length; i++)
                    {
                        // Convert big-endian bytes to float
                        byte[] floatBytes = new byte[4];
                        floatBytes[3] = buffer[dataOffset + i * 4];
                        floatBytes[2] = buffer[dataOffset + i * 4 + 1];
                        floatBytes[1] = buffer[dataOffset + i * 4 + 2];
                        floatBytes[0] = buffer[dataOffset + i * 4 + 3];
                        
                        float rawValue = BitConverter.ToSingle(floatBytes, 0);
                        
                        if (float.IsFinite(rawValue))
                        {
                            double scaledValue = rawValue * bscale + bzero;
                            values.Add(scaledValue);
                        }
                    }
                    break;
                    
                case -64: // 64-bit floating point
                    for (int i = 0; i < pixelCount && dataOffset + i * 8 + 7 < buffer.Length; i++)
                    {
                        // Convert big-endian bytes to double
                        byte[] doubleBytes = new byte[8];
                        for (int j = 0; j < 8; j++)
                            doubleBytes[7 - j] = buffer[dataOffset + i * 8 + j];
                        
                        double rawValue = BitConverter.ToDouble(doubleBytes, 0);
                        
                        if (double.IsFinite(rawValue))
                        {
                            double scaledValue = rawValue * bscale + bzero;
                            values.Add(scaledValue);
                        }
                    }
                    break;
                    
                default:
                    return (0.0, 0.0);
            }
            
            if (values.Count == 0)
                return (0.0, 0.0);
            
            // Calculate mean in one pass before sorting
            double sum = 0.0;
            for (int i = 0; i < values.Count; i++)
                sum += values[i];
            double mean = sum / values.Count;

            // Sort and find median
            values.Sort();
            int middle = values.Count / 2;
            
            double median;
            if (values.Count % 2 == 0)
            {
                median = (values[middle - 1] + values[middle]) / 2.0;
            }
            else
            {
                median = values[middle];
            }
            
            // Normalize based on data type
            // For 16-bit FITS with typical BZERO=32768, BSCALE=1, values range from 0-65535
            // For floating point FITS, values are typically already in 0.0-1.0 range
            if (bitpix > 0)
            {
                // Integer data - normalize based on bit depth
                double maxValue = Math.Pow(2, Math.Abs(bitpix)) - 1;
                return (Math.Clamp(median / maxValue, 0.0, 1.0), Math.Clamp(mean / maxValue, 0.0, 1.0));
            }
            else
            {
                // Floating point data - typically already normalized to 0.0-1.0
                return (Math.Clamp(median, 0.0, 1.0), Math.Clamp(mean, 0.0, 1.0));
            }
        }
    }
}
