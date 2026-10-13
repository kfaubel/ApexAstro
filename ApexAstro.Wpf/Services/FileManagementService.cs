using System;
using System.Collections.Generic;
using System.Globalization;
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

        public static string ResolveDirectoryForLoad(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return directory;

            return directory;
        }

        public List<FileItem> LoadFilesFromDirectory(string directory)
        {
            var files = new List<FileItem>();

            if (!Directory.Exists(directory))
                return files;

            try
            {
                if (IsLightDirectory(directory))
                {
                    return LoadFilesFromSingleDirectory(directory);
                }

                var lightDirectories = FindLightDirectories(directory);
                if (lightDirectories.Count > 0)
                {
                    return LoadFilesFromLightDirectories(lightDirectories);
                }

                return LoadFilesFromSingleDirectory(directory);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("Directory Load", $"Failed to load files from {directory}", ex);
            }

            return files;
        }

        private List<FileItem> LoadFilesFromSingleDirectory(string directory)
        {
            var files = new List<FileItem>();
            var allFiles = EnumerateSupportedFiles(directory).ToList();

            foreach (var fileInfo in allFiles.Select(f => new FileInfo(f)).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                files.Add(new FileItem
                {
                    Name = fileInfo.Name,
                    Path = fileInfo.FullName,
                    Size = fileInfo.Length
                });
            }

            return files;
        }

        private static List<string> FindLightDirectories(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return new List<string>();

            var lightDirectories = new List<string>();

            foreach (var dir in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(dir), "LIGHT", StringComparison.OrdinalIgnoreCase))
                {
                    lightDirectories.Add(dir);
                }
            }

            if (lightDirectories.Count > 0)
            {
                return lightDirectories
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return new List<string>();
        }

        private static List<FileItem> LoadFilesFromLightDirectories(IEnumerable<string> lightDirectories)
        {
            var allFiles = new List<string>();

            foreach (var lightDirectory in lightDirectories)
            {
                allFiles.AddRange(EnumerateSupportedFiles(lightDirectory));
            }

            return allFiles
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new FileInfo(path))
                .OrderBy(fileInfo => fileInfo.Name, StringComparer.OrdinalIgnoreCase)
                .Select(fileInfo => new FileItem
                {
                    Name = fileInfo.Name,
                    Path = fileInfo.FullName,
                    Size = fileInfo.Length
                })
                .ToList();
        }

        private static bool IsLightDirectory(string directory)
        {
            return !string.IsNullOrWhiteSpace(directory) &&
                   string.Equals(Path.GetFileName(directory), "LIGHT", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateSupportedFiles(string directory)
        {
            if (!Directory.Exists(directory))
                return Enumerable.Empty<string>();

            var supportedExtensions = new[]
            {
                "*.fits", "*.fit", "*.fts",
                "*.xisf",
                "*.arw",
                "*.jpg", "*.jpeg",
                "*.png",
                "*.bmp",
                "*.tiff", "*.tif",
                "*.gif",
                "*.webp"
            };

            var allFiles = new List<string>();
            foreach (var pattern in supportedExtensions)
            {
                allFiles.AddRange(Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly));
            }

            return allFiles.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsNightDirectory(string directoryPath)
        {
            string name = Path.GetFileName(directoryPath);
            if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("NIGHT_", StringComparison.OrdinalIgnoreCase))
                return false;

            string datePart = name.Substring("NIGHT_".Length);
            return DateTime.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }
    }
}