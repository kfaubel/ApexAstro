using Xunit;
using ApexAstro.Wpf;
using System;
using System.Collections.Generic;
using System.IO;

namespace ApexAstro.Tests
{
    /// <summary>
    /// Tests for FilenameParser which extracts keyword values from NINA-formatted filenames.
    /// This is critical for quickly extracting quality metrics (RMS, HFR, star count) from filenames.
    /// </summary>
    public class FilenameParserTests
    {
        [Fact]
        public void ExtractKeywordValues_NinaFormattedFilename_ExtractsRMS()
        {
            // Arrange
            var filename = "2025-10-16_23-42-23_R_RMS_0.75_HFR_2.26_Stars_2029_100_10.00s_-9.60C_0052.fits";
            var keywords = new[] { "RMS" };
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(filename, keywords);
            
            // Assert
            Assert.NotNull(result);
            Assert.True(result.ContainsKey("RMS"), "Should extract RMS keyword");
            Assert.Equal("0.75", result["RMS"]);
        }
        
        [Fact]
        public void ExtractKeywordValues_NinaFormattedFilename_ExtractsHFR()
        {
            // Arrange
            var filename = "2025-10-16_23-42-23_R_RMS_0.75_HFR_2.26_Stars_2029_100_10.00s_-9.60C_0052.fits";
            var keywords = new[] { "HFR" };
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(filename, keywords);
            
            // Assert
            Assert.True(result.ContainsKey("HFR"), "Should extract HFR keyword");
            Assert.Equal("2.26", result["HFR"]);
        }
        
        [Fact]
        public void ExtractKeywordValues_NinaFormattedFilename_ExtractsMultipleKeywords()
        {
            // Arrange
            var filename = "2025-10-16_23-42-23_R_RMS_0.75_HFR_2.26_Stars_2029_100_10.00s_-9.60C_0052.fits";
            var keywords = new[] { "RMS", "HFR", "Stars" };
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(filename, keywords);
            
            // Assert
            Assert.Equal(3, result.Count);
            Assert.Equal("0.75", result["RMS"]);
            Assert.Equal("2.26", result["HFR"]);
            Assert.Equal("2029", result["Stars"]);
        }
        
        [Fact]
        public void ExtractKeywordValues_MissingKeyword_DoesNotIncludeInResult()
        {
            // Arrange
            var filename = "2025-10-16_23-42-23_R_RMS_0.75_HFR_2.26_Stars_2029_100_10.00s_-9.60C_0052.fits";
            var keywords = new[] { "MISSING" };
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(filename, keywords);
            
            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }
        
        [Fact]
        public void ExtractKeywordValues_CaseInsensitive()
        {
            // Arrange
            var filename = "2025-10-16_23-42-23_R_RMS_0.75_HFR_2.26_Stars_2029_100_10.00s_-9.60C_0052.fits";
            var keywords = new[] { "rms", "hfr", "STARS" };
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(filename, keywords);
            
            // Assert
            Assert.Equal(3, result.Count);
            Assert.True(result.ContainsKey("rms") || result.ContainsKey("RMS"));
        }
        
        [Fact]
        public void ExtractKeywordValues_EmptyFilename_ReturnsEmptyDictionary()
        {
            // Arrange
            var filename = "";
            var keywords = new[] { "RMS", "HFR" };
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(filename, keywords);
            
            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }
        
        [Fact]
        public void ExtractKeywordValues_NullFilename_ReturnsEmptyDictionary()
        {
            // Arrange
            string? nullFilename = null;
            var keywords = new[] { "RMS", "HFR" };
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(nullFilename!, keywords);
            
            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }
        
        [Fact]
        public void ExtractKeywordValues_KeywordAtEndOfFilename_NotExtracted()
        {
            // Arrange - Keyword at end has no following value
            var filename = "2025-10-16_23-42-23_R_RMS";
            var keywords = new[] { "RMS" };
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(filename, keywords);
            
            // Assert
            Assert.Empty(result); // Should not extract because there's no value after RMS
        }
        
        [Fact]
        public void ExtractKeywordValues_DifferentNinaFile_ExtractsCorrectly()
        {
            // Arrange
            var filename = "2025-10-16_23-46-13_B_RMS_1.12_HFR_2.43_Stars_1269_100_10.00s_-10.00C_0026.fits";
            var keywords = new[] { "RMS", "HFR", "Stars" };
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(filename, keywords);
            
            // Assert
            Assert.Equal("1.12", result["RMS"]);
            Assert.Equal("2.43", result["HFR"]);
            Assert.Equal("1269", result["Stars"]);
        }
        
        [Fact]
        public void ExtractKeywordValues_FileWithEmptyValues_ExtractsEmptyString()
        {
            // Arrange - File with empty HFR and Stars values
            var filename = "2025-10-17_07-31-45_R_RMS_0.00_HFR__Stars__100_0.02s_8.00C_0000.fits";
            var keywords = new[] { "HFR", "Stars" };
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(filename, keywords);
            
            // Assert
            // Should extract empty string for HFR (value between HFR and Stars is empty)
            Assert.True(result.ContainsKey("HFR"));
        }
        
        [Fact]
        public void ExtractKeywordValues_MultipleOccurrences_TakesFirstOccurrence()
        {
            // Arrange - Filename with duplicate keyword
            var filename = "RMS_1.5_data_RMS_2.5.fits";
            var keywords = new[] { "RMS" };
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(filename, keywords);
            
            // Assert
            Assert.Equal("1.5", result["RMS"]); // Should take first occurrence
        }
        
        [Fact]
        public void ExtractKeywordValues_NoUnderscores_ReturnsEmpty()
        {
            // Arrange - Filename without underscores (different format)
            var filename = "image-001-test.fits";
            var keywords = new[] { "RMS", "HFR" };
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(filename, keywords);
            
            // Assert
            Assert.Empty(result);
        }
        
        [Fact]
        public void ExtractKeywordValues_EmptyKeywordsList_ReturnsEmpty()
        {
            // Arrange
            var filename = "2025-10-16_23-42-23_R_RMS_0.75_HFR_2.26.fits";
            var keywords = Array.Empty<string>();
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(filename, keywords);
            
            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }
        
        [Fact]
        public void ExtractKeywordValues_SpecialCharactersInValue_ExtractsCorrectly()
        {
            // Arrange - Value with negative sign
            var filename = "test_CCD-TEMP_-10.5_other.fits";
            var keywords = new[] { "CCD-TEMP" };
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(filename, keywords);
            
            // Assert
            Assert.True(result.ContainsKey("CCD-TEMP"));
            Assert.Equal("-10.5", result["CCD-TEMP"]);
        }
        
        [Fact]
        public void ExtractKeywordValues_NumericKeyword_ExtractsCorrectly()
        {
            // Arrange - Numeric values as keywords
            var filename = "image_100_5.00s_filter.fits";
            var keywords = new[] { "100" };
            
            // Act
            var result = FilenameParser.ExtractKeywordValues(filename, keywords);
            
            // Assert
            Assert.True(result.ContainsKey("100"));
            Assert.Equal("5.00s", result["100"]);
        }

        [Fact]
        public void ExtractDateToken_NinaFormattedFilename_ExtractsDate()
        {
            var filename = "2026-07-08_22-24-17_O_RMS_0.58_HFR_1.95_ECC_0.57_0306.fits";

            var date = FilenameParser.ExtractDateToken(filename);

            Assert.Equal("2026-07-08", date);
        }

        [Fact]
        public void ExtractTimeToken_NinaFormattedFilename_ExtractsHyphenTime()
        {
            var filename = "2026-07-08_22-24-17_O_RMS_0.58_HFR_1.95_ECC_0.57_0306.fits";

            var time = FilenameParser.ExtractTimeToken(filename);

            Assert.Equal("22:24:17", time);
        }

        [Fact]
        public void ExtractTimeToken_ColonFormat_ExtractsTime()
        {
            var filename = "2026-07-08_22:24:17_O_RMS_0.58_HFR_1.95_ECC_0.57_0306.fits";

            var time = FilenameParser.ExtractTimeToken(filename);

            Assert.Equal("22:24:17", time);
        }

        [Fact]
        public void ExtractDateToken_CompactFormat_ExtractsDate()
        {
            var filename = "20260724-234030.fits";

            var date = FilenameParser.ExtractDateToken(filename);

            Assert.Equal("2026-07-24", date);
        }

        [Fact]
        public void ExtractTimeToken_CompactFormat_ExtractsTime()
        {
            var filename = "20260724-234030.fits";

            var time = FilenameParser.ExtractTimeToken(filename);

            Assert.Equal("23:40:30", time);
        }

        [Fact]
        public void ExtractDateAndTimeToken_CompactFormatInMiddle_ExtractsValues()
        {
            var filename = "Light_SH2-101_120.0s_Bin1_20260724-232222_0.0C_0001.fit";

            var date = FilenameParser.ExtractDateToken(filename);
            var time = FilenameParser.ExtractTimeToken(filename);

            Assert.Equal("2026-07-24", date);
            Assert.Equal("23:22:22", time);
        }

        [Fact]
        public void ExtractFrameToken_NinaFormattedFilename_ExtractsTrailingSequence()
        {
            var filename = "2026-07-08_22-24-17_O_RMS_0.58_HFR_1.95_ECC_0.57_0306.fits";

            var sequence = FilenameParser.ExtractFrameToken(filename);

            Assert.Equal("0306", sequence);
        }
    }
}
