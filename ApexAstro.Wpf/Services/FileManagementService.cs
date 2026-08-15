using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ApexAstro.Wpf.Models;

namespace ApexAstro.Wpf.Services
{
    public class FileManagementService
    {
        public void MoveSelectedFiles(List<FileItem> selectedFiles, string targetDirectory, bool moveToTrash)
        {
            foreach (var file in selectedFiles)
            {
                try
                {
                    if (moveToTrash)
                    {
                        if (!MoveToRecycleBin(file.Path))
                        {
                            throw new InvalidOperationException($"Failed to move {file.Name} to recycle bin");
                        }
                    }
                    else
                    {
                        var destinationPath = Path.Combine(targetDirectory, file.Name);
                        
                        // Handle file overwrite by deleting existing file first
                        if (File.Exists(destinationPath))
                        {
                            File.Delete(destinationPath);
                        }
                        
                        File.Move(file.Path, destinationPath);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error moving {file.Name}: {ex.Message}", ex);
                }
            }
        }

        public bool MoveToRecycleBin(string filePath)
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(filePath, 
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, 
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public string GenerateUniqueFileName(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            var directory = Path.GetDirectoryName(filePath) ?? "";
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);

            int counter = 1;
            string newFilePath;
            
            do
            {
                var newName = $"{nameWithoutExtension} ({counter}){extension}";
                newFilePath = Path.Combine(directory, newName);
                counter++;
            }
            while (File.Exists(newFilePath));

            return newFilePath;
        }

        /// <summary>
        /// Gets file information for a single file path.
        /// Used when loading specific files instead of a full directory.
        /// </summary>
        /// <param name="filePath">Full path to the file</param>
        /// <returns>FileItem with basic file information</returns>
        public FileItem GetFileInfo(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            
            return new FileItem
            {
                Name = fileInfo.Name,
                Path = fileInfo.FullName,
                Size = fileInfo.Length
            };
        }

        public List<FileItem> LoadFilesFromDirectory(string directory)
        {
            var files = new List<FileItem>();
            
            if (!Directory.Exists(directory))
                return files;

            try
            {
                var operationStopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                // Load all supported image formats
                var supportedExtensions = new[]
                {
                    "*.fits", "*.fit", "*.fts",  // FITS files
                    "*.xisf",                     // XISF files
                    "*.arw",                      // Sony ARW raw files
                    "*.jpg", "*.jpeg",           // JPEG files
                    "*.png",                     // PNG files
                    "*.bmp",                     // Bitmap files
                    "*.tiff", "*.tif",           // TIFF files
                    "*.gif",                     // GIF files
                    "*.webp"                     // WebP files
                };

                var allFiles = new List<string>();
                foreach (var pattern in supportedExtensions)
                {
                    var matchingFiles = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
                    allFiles.AddRange(matchingFiles);
                }
                
                operationStopwatch.Stop();
                if (operationStopwatch.ElapsedMilliseconds > 3000)
                {
                    App.LoggingService?.LogWarning("Directory Enumeration", $"'{directory}' took {operationStopwatch.ElapsedMilliseconds}ms (>3s threshold)");
                }
                
                var fileInfos = allFiles.Select(f => new FileInfo(f))
                    .OrderBy(f => f.Name);

                foreach (var fileInfo in fileInfos)
                {
                    files.Add(new FileItem
                    {
                        Name = fileInfo.Name,
                        Path = fileInfo.FullName,
                        Size = fileInfo.Length
                    });
                }
            }
            catch (Exception ex)
            {
                // Log directory access failures (permissions, network issues, etc.)
                App.LoggingService?.LogError("Directory Load", $"Failed to load files from {directory}", ex);
            }

            return files;
        }
    }
}