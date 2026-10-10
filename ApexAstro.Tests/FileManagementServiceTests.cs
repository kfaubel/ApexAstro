using Xunit;
using ApexAstro.Wpf;
using ApexAstro.Wpf.Services;
using ApexAstro.Wpf.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ApexAstro.Tests
{
    /// <summary>
    /// Tests for FileManagementService which handles file operations like loading, moving, and organizing files.
    /// </summary>
    public class FileManagementServiceTests
    {
        private readonly FileManagementService _service;
        private readonly string _testDataPath = Path.Combine("..", "..", "..", "..", "TestData2");
        
        public FileManagementServiceTests()
        {
            _service = new FileManagementService();
        }
        
        [Fact]
        public void LoadFilesFromDirectory_ValidDirectory_ReturnsFileList()
        {
            // Arrange
            var directory = _testDataPath;
            
            if (!Directory.Exists(directory))
            {
                return; // Skip if test data doesn't exist
            }
            
            // Act
            var files = _service.LoadFilesFromDirectory(directory);
            
            // Assert
            Assert.NotNull(files);
            Assert.NotEmpty(files);
            Assert.All(files, file => 
            {
                Assert.NotNull(file.Name);
                Assert.NotNull(file.Path);
                Assert.True(file.Size >= 0);
            });
        }
        
        [Fact]
        public void LoadFilesFromDirectory_NonExistentDirectory_ReturnsEmptyList()
        {
            // Arrange
            var directory = "C:\\NonExistent\\Directory";
            
            // Act
            var files = _service.LoadFilesFromDirectory(directory);
            
            // Assert
            Assert.NotNull(files);
            Assert.Empty(files);
        }
        
        [Fact]
        public void LoadFilesFromDirectory_ValidDirectory_FilesSortedByName()
        {
            // Arrange
            var directory = _testDataPath;
            
            if (!Directory.Exists(directory))
            {
                return;
            }
            
            // Act
            var files = _service.LoadFilesFromDirectory(directory);
            
            // Assert
            if (files.Count > 1)
            {
                var sortedNames = files.Select(f => f.Name).ToList();
                var expectedSorted = sortedNames.OrderBy(n => n).ToList();
                Assert.Equal(expectedSorted, sortedNames);
            }
        }
        
        [Fact]
        public void LoadFilesFromDirectory_LoadsFitsFiles()
        {
            // Arrange
            var directory = _testDataPath;
            
            if (!Directory.Exists(directory))
            {
                return;
            }
            
            // Act
            var files = _service.LoadFilesFromDirectory(directory);
            
            // Assert
            var fitsFiles = files.Where(f => 
                f.Name.EndsWith(".fits", StringComparison.OrdinalIgnoreCase) ||
                f.Name.EndsWith(".fit", StringComparison.OrdinalIgnoreCase) ||
                f.Name.EndsWith(".fts", StringComparison.OrdinalIgnoreCase)
            );
            
            Assert.NotEmpty(fitsFiles);
        }
        
        [Fact]
        public void GetFileInfo_ValidFile_ReturnsFileItem()
        {
            // Arrange
            var directory = _testDataPath;
            
            if (!Directory.Exists(directory))
            {
                return;
            }
            
            var files = Directory.GetFiles(directory, "*.fits").FirstOrDefault();
            if (files == null)
            {
                return;
            }
            
            // Act
            var fileItem = _service.GetFileInfo(files);
            
            // Assert
            Assert.NotNull(fileItem);
            Assert.NotNull(fileItem.Name);
            Assert.NotNull(fileItem.Path);
            Assert.True(fileItem.Size > 0);
        }
        
        [Fact]
        public void GenerateUniqueFileName_NonExistentFile_ReturnsSamePath()
        {
            // Arrange
            var filePath = "C:\\NonExistent\\test.fits";
            
            // Act
            var result = _service.GenerateUniqueFileName(filePath);
            
            // Assert
            Assert.Equal(filePath, result);
        }
        
        [Fact]
        public void GenerateUniqueFileName_ExistingFile_ReturnsUniqueFileName()
        {
            // Arrange - Use a file we know exists in TestData
            var directory = _testDataPath;
            
            if (!Directory.Exists(directory))
            {
                return;
            }
            
            var existingFile = Directory.GetFiles(directory, "*.fits").FirstOrDefault();
            if (existingFile == null)
            {
                return;
            }
            
            // Act
            var result = _service.GenerateUniqueFileName(existingFile);
            
            // Assert
            Assert.NotEqual(existingFile, result);
            Assert.Contains(" (1)", result);
        }
        
        [Fact]
        public void LoadFilesFromDirectory_LoadsMultipleFileTypes()
        {
            // Arrange
            var directory = _testDataPath;
            
            if (!Directory.Exists(directory))
            {
                return;
            }
            
            // Act
            var files = _service.LoadFilesFromDirectory(directory);
            
            // Assert
            Assert.NotNull(files);
            // Should load FITS, XISF, and possibly image files
            var extensions = files.Select(f => Path.GetExtension(f.Name).ToLowerInvariant()).Distinct();
            Assert.NotEmpty(extensions);
        }
        
        [Fact]
        public void GetFileInfo_CreatesFileItemWithCorrectProperties()
        {
            // Arrange
            var directory = _testDataPath;
            
            if (!Directory.Exists(directory))
            {
                return;
            }
            
            var testFile = Directory.GetFiles(directory).FirstOrDefault();
            if (testFile == null)
            {
                return;
            }
            
            var expectedInfo = new FileInfo(testFile);
            
            // Act
            var fileItem = _service.GetFileInfo(testFile);
            
            // Assert
            Assert.Equal(expectedInfo.Name, fileItem.Name);
            Assert.Equal(expectedInfo.FullName, fileItem.Path);
            Assert.Equal(expectedInfo.Length, fileItem.Size);
        }
        
        [Fact]
        public void GenerateUniqueFileName_MultipleIterations_GeneratesIncrementingNames()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "ApexAstroTest_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            try
            {
                var baseFileName = Path.Combine(tempDir, "test.fits");
                
                // Create the base file and a few numbered versions
                File.WriteAllText(baseFileName, "test");
                File.WriteAllText(Path.Combine(tempDir, "test (1).fits"), "test");
                File.WriteAllText(Path.Combine(tempDir, "test (2).fits"), "test");
                
                // Act
                var result = _service.GenerateUniqueFileName(baseFileName);
                
                // Assert
                Assert.Contains(" (3)", result); // Should be the next number
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }
        
        [Fact]
        public void LoadFilesFromDirectory_HandlesEmptyDirectory()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "ApexAstroTest_Empty_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            try
            {
                // Act
                var files = _service.LoadFilesFromDirectory(tempDir);
                
                // Assert
                Assert.NotNull(files);
                Assert.Empty(files);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir);
            }
        }
        
        [Fact]
        public void LoadFilesFromDirectory_IncludesXisfFiles()
        {
            // Arrange
            var directory = _testDataPath;
            
            if (!Directory.Exists(directory))
            {
                return;
            }
            
            // Act
            var files = _service.LoadFilesFromDirectory(directory);
            
            // Assert - Check if XISF files are included
            var xisfFiles = files.Where(f => f.Name.EndsWith(".xisf", StringComparison.OrdinalIgnoreCase));
            // May or may not have XISF files, just ensure method handles them
            Assert.NotNull(xisfFiles);
        }
        
        [Fact]
        public void LoadFilesFromDirectory_UsesDataAggregationWhenDataFolderExists()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "ApexAstroTest_DataAggregate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var dataDir = Path.Combine(tempRoot, "Data");
                var night1 = Path.Combine(dataDir, "NIGHT_2026-09-10");
                var night2 = Path.Combine(dataDir, "NIGHT_2026-09-11");

                Directory.CreateDirectory(Path.Combine(night1, "LIGHT"));
                Directory.CreateDirectory(Path.Combine(night2, "LIGHT"));

                var file1 = Path.Combine(night1, "LIGHT", "night1.fits");
                var file2 = Path.Combine(night2, "LIGHT", "night2.xisf");
                File.WriteAllText(file1, "fits");
                File.WriteAllText(file2, "xisf");

                var files = _service.LoadFilesFromDirectory(tempRoot);

                Assert.Equal(2, files.Count);
                Assert.Contains(files, f => f.Path == file1);
                Assert.Contains(files, f => f.Path == file2);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
        }

        [Fact]
        public void LoadFilesFromDirectory_PrefersDirectFilesOverDataAggregation()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "ApexAstroTest_DataPriority_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var directFile = Path.Combine(tempRoot, "direct.fits");
                File.WriteAllText(directFile, "direct");

                var dataDir = Path.Combine(tempRoot, "Data");
                var nightDir = Path.Combine(dataDir, "NIGHT_2026-09-12");
                Directory.CreateDirectory(Path.Combine(nightDir, "LIGHT"));
                var dataFile = Path.Combine(nightDir, "LIGHT", "night.fits");
                File.WriteAllText(dataFile, "night");

                var files = _service.LoadFilesFromDirectory(tempRoot);

                Assert.Single(files);
                Assert.Equal(directFile, files[0].Path);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
        }

        [Fact]
        public void LoadFilesFromDirectory_DataDirectory_IgnoresDirectFilesAndUsesNightLightFiles()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "ApexAstroTest_DataDirectory_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var dataDir = Path.Combine(tempRoot, "Data");
                Directory.CreateDirectory(dataDir);

                var directFile = Path.Combine(dataDir, "should-ignore.fits");
                File.WriteAllText(directFile, "ignore");

                var nightDir = Path.Combine(dataDir, "NIGHT_2026-09-13");
                var lightDir = Path.Combine(nightDir, "LIGHT");
                Directory.CreateDirectory(lightDir);

                var lightFile1 = Path.Combine(lightDir, "night1.fits");
                var lightFile2 = Path.Combine(lightDir, "night2.xisf");
                File.WriteAllText(lightFile1, "fits");
                File.WriteAllText(lightFile2, "xisf");

                var files = _service.LoadFilesFromDirectory(dataDir);

                Assert.Equal(2, files.Count);
                Assert.DoesNotContain(files, f => f.Path == directFile);
                Assert.Contains(files, f => f.Path == lightFile1);
                Assert.Contains(files, f => f.Path == lightFile2);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
        }

        [Fact]
        public void AutoSelectForegroundConverter_UsesSelectionRulesForCustomKeywords()
        {
            var file = new FileItem
            {
                Name = "sample.fits",
                Path = "C:\\Temp\\sample.fits",
                CustomKeywords = new Dictionary<string, string>
                {
                    ["HFR"] = "3.0",
                    ["RMS"] = "1.1"
                }
            };

            var criteria = new List<AutoSelectCriteriaSetting>
            {
                new AutoSelectCriteriaSetting
                {
                    Key = "HFR",
                    IsEnabled = true,
                    MaxValue = "2.5",
                    AllowBlank = false
                },
                new AutoSelectCriteriaSetting
                {
                    Key = "RMS",
                    IsEnabled = true,
                    MaxValue = "1.5",
                    AllowBlank = false
                }
            };

            var converter = new ApexAstro.Wpf.Converters.AutoSelectRowForegroundConverter();
            var redBrush = converter.Convert(
                file,
                typeof(System.Windows.Media.Brush),
                new ApexAstro.Wpf.Converters.AutoSelectKeywordForegroundContext
                {
                    Keyword = "HFR",
                    Criteria = criteria
                },
                System.Globalization.CultureInfo.InvariantCulture) as System.Windows.Media.Brush;

            var greenBrush = converter.Convert(
                file,
                typeof(System.Windows.Media.Brush),
                new ApexAstro.Wpf.Converters.AutoSelectKeywordForegroundContext
                {
                    Keyword = "RMS",
                    Criteria = criteria
                },
                System.Globalization.CultureInfo.InvariantCulture) as System.Windows.Media.Brush;

            Assert.NotNull(redBrush);
            Assert.NotNull(greenBrush);
            Assert.Equal(System.Windows.Media.Brushes.Red.Color, ((System.Windows.Media.SolidColorBrush)redBrush).Color);
            Assert.Equal(System.Windows.Media.Brushes.Green.Color, ((System.Windows.Media.SolidColorBrush)greenBrush).Color);
        }

        [Fact]
        public void MoveToRecycleBin_NonExistentFile_ReturnsFalse()
        {
            // Arrange
            var nonExistentFile = "C:\\NonExistent\\File.fits";
            
            // Act
            var result = _service.MoveToRecycleBin(nonExistentFile);
            
            // Assert
            Assert.False(result);
        }
    }
}
