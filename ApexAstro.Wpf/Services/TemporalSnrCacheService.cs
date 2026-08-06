using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ApexAstro.Core;

namespace ApexAstro.Wpf.Services
{
    /// <summary>
    /// Persists temporal SNR results and the validation data needed to decide whether
    /// the cache is still valid for the current contents of a folder.
    /// </summary>
    public class TemporalSnrCacheService
    {
        private const string CacheFileName = ".apexastro.temporal-snr.json";
        private const string SummaryFileName = ".apexastro.snr.json";
        private const int SchemaVersion = 2;
        private const int SummarySchemaVersion = 2;

        public TemporalSnrFolderStatus GetFolderStatus(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return new TemporalSnrFolderStatus
                {
                    FolderPath = folderPath ?? string.Empty,
                    HasCacheFile = false,
                    HasSummaryFile = false,
                    IsUpToDate = false
                };
            }

            bool hasCacheFile = File.Exists(GetCachePath(folderPath));
            bool hasSummaryFile = File.Exists(GetSummaryPath(folderPath));
            bool isUpToDate = TryLoadIfValid(folderPath, out _);

            return new TemporalSnrFolderStatus
            {
                FolderPath = folderPath,
                HasCacheFile = hasCacheFile,
                HasSummaryFile = hasSummaryFile,
                IsUpToDate = isUpToDate
            };
        }

        public bool TryLoadIfValid(string folderPath, out TemporalSnrCacheManifest? manifest)
        {
            manifest = null;

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return false;

            string cachePath = GetCachePath(folderPath);
            if (!File.Exists(cachePath))
                return false;

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                manifest = JsonSerializer.Deserialize<TemporalSnrCacheManifest>(File.ReadAllText(cachePath), options);
                if (manifest == null || manifest.SchemaVersion != SchemaVersion)
                    return false;

                return IsValid(folderPath, manifest);
            }
            catch
            {
                manifest = null;
                return false;
            }
        }

        public void Save(string folderPath, TemporalSnrComputationResult result)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || result == null)
                return;

            try
            {
                var manifest = new TemporalSnrCacheManifest
                {
                    SchemaVersion = SchemaVersion,
                    ComputedUtc = DateTime.UtcNow,
                    FolderPath = folderPath,
                    Filters = result.FilterResults.Values
                        .Select(filter => new TemporalSnrCacheFilterEntry
                        {
                            FilterName = filter.FilterName,
                            Width = filter.Width,
                            Height = filter.Height,
                            FrameCount = filter.FrameCount,
                            ReferenceFile = filter.ReferenceFile,
                            MeanTemporalSnr = filter.MeanTemporalSnr,
                            MedianTemporalSnr = filter.MedianTemporalSnr,
                            StandardDeviationTemporalSnr = filter.StandardDeviationTemporalSnr,
                            SourceFiles = filter.SourceFiles
                                .Select(source => new TemporalSnrCacheSourceEntry
                                {
                                    FileName = source.FileName,
                                    LastWriteUtc = source.LastWriteUtc,
                                    FileSizeBytes = source.FileSizeBytes,
                                    Width = source.Width,
                                    Height = source.Height,
                                    Bitpix = source.Bitpix,
                                    Hfr = source.Hfr
                                })
                                .ToList()
                        })
                        .ToList()
                };

                string cachePath = GetCachePath(folderPath);
                var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(cachePath, json);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("Temporal SNR Cache", $"Failed to save cache in {folderPath}", ex);
            }
        }

        public void SaveProjectSummary(string folderPath, TemporalSnrComputationResult result)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || result == null)
                return;

            try
            {
                var summary = new TemporalSnrProjectSummary
                {
                    SchemaVersion = SummarySchemaVersion,
                    ComputedUtc = DateTime.UtcNow,
                    FolderPath = folderPath,
                    FilterSummaries = result.FilterResults.Values
                        .OrderBy(filter => filter.FilterName, StringComparer.OrdinalIgnoreCase)
                        .Select(filter => new TemporalSnrProjectFilterSummary
                        {
                            FilterName = filter.FilterName,
                            FrameCount = filter.FrameCount,
                            MedianTemporalSnr = filter.MedianTemporalSnr,
                            MeanTemporalSnr = filter.MeanTemporalSnr,
                            StandardDeviationTemporalSnr = filter.StandardDeviationTemporalSnr,
                            CoefficientOfVariation = filter.MeanTemporalSnr > 1e-9
                                ? filter.StandardDeviationTemporalSnr / filter.MeanTemporalSnr
                                : 0.0
                        })
                        .ToList(),
                    FrameSummaries = result.FrameResults
                        .OrderBy(frame => frame.FileName, StringComparer.OrdinalIgnoreCase)
                        .Select(frame => new TemporalSnrProjectFrameSummary
                        {
                            FileName = frame.FileName,
                            FilterName = frame.FilterName,
                            FrameSnr = frame.FrameSnr,
                            Grade = frame.Grade,
                            Keep = frame.Keep,
                            Rank = frame.Rank,
                            FrameCountInFilter = frame.FrameCountInFilter
                        })
                        .ToList()
                };

                string summaryPath = GetSummaryPath(folderPath);
                var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(summaryPath, json);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("Temporal SNR Summary", $"Failed to save SNR summary in {folderPath}", ex);
            }
        }

        public bool TryLoadProjectSummary(string folderPath, out TemporalSnrProjectSummary? summary)
        {
            summary = null;

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return false;

            string summaryPath = GetSummaryPath(folderPath);
            if (!File.Exists(summaryPath))
                return false;

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                summary = JsonSerializer.Deserialize<TemporalSnrProjectSummary>(File.ReadAllText(summaryPath), options);
                if (summary == null || summary.SchemaVersion < 1)
                    return false;

                return true;
            }
            catch
            {
                summary = null;
                return false;
            }
        }

        private static bool IsValid(string folderPath, TemporalSnrCacheManifest manifest)
        {
            var currentFiles = Directory.GetFiles(folderPath)
                .Where(path => IsSupportedImageFile(path))
                .ToDictionary(path => Path.GetFileName(path), path => new FileInfo(path), StringComparer.OrdinalIgnoreCase);

            var manifestFiles = manifest.Filters
                .SelectMany(filter => filter.SourceFiles)
                .ToList();

            if (currentFiles.Count != manifestFiles.Count)
                return false;

            foreach (var source in manifestFiles)
            {
                if (!currentFiles.TryGetValue(source.FileName, out var currentInfo))
                    return false;

                if (Math.Abs((currentInfo.LastWriteTimeUtc - source.LastWriteUtc).TotalSeconds) > 2)
                    return false;

                if (currentInfo.Length != source.FileSizeBytes)
                    return false;
            }

            return true;
        }

        private static bool IsSupportedImageFile(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            return extension.Equals(".fits", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".fit", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".fts", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetCachePath(string folderPath) => Path.Combine(folderPath, CacheFileName);
        private static string GetSummaryPath(string folderPath) => Path.Combine(folderPath, SummaryFileName);
    }

    public sealed class TemporalSnrFolderStatus
    {
        public string FolderPath { get; set; } = string.Empty;
        public bool HasCacheFile { get; set; }
        public bool HasSummaryFile { get; set; }
        public bool IsUpToDate { get; set; }
        public bool HasAnySnrData => HasCacheFile || HasSummaryFile;
        public bool IsOutOfDate => HasAnySnrData && !IsUpToDate;
    }

    public sealed class TemporalSnrCacheManifest
    {
        public int SchemaVersion { get; set; }
        public DateTime ComputedUtc { get; set; }
        public string FolderPath { get; set; } = string.Empty;
        public List<TemporalSnrCacheFilterEntry> Filters { get; set; } = new();
    }

    public sealed class TemporalSnrCacheFilterEntry
    {
        public string FilterName { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int FrameCount { get; set; }
        public string ReferenceFile { get; set; } = string.Empty;
        public double MeanTemporalSnr { get; set; }
        public double MedianTemporalSnr { get; set; }
        public double StandardDeviationTemporalSnr { get; set; }
        public List<TemporalSnrCacheSourceEntry> SourceFiles { get; set; } = new();
    }

    public sealed class TemporalSnrCacheSourceEntry
    {
        public string FileName { get; set; } = string.Empty;
        public DateTime LastWriteUtc { get; set; }
        public long FileSizeBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Bitpix { get; set; }
        public double? Hfr { get; set; }
    }

    public sealed class TemporalSnrProjectSummary
    {
        public int SchemaVersion { get; set; }
        public DateTime ComputedUtc { get; set; }
        public string FolderPath { get; set; } = string.Empty;
        public List<TemporalSnrProjectFilterSummary> FilterSummaries { get; set; } = new();
        public List<TemporalSnrProjectFrameSummary> FrameSummaries { get; set; } = new();
    }

    public sealed class TemporalSnrProjectFilterSummary
    {
        public string FilterName { get; set; } = string.Empty;
        public int FrameCount { get; set; }
        public double MedianTemporalSnr { get; set; }
        public double MeanTemporalSnr { get; set; }
        public double StandardDeviationTemporalSnr { get; set; }
        public double CoefficientOfVariation { get; set; }
    }

    public sealed class TemporalSnrProjectFrameSummary
    {
        public string FileName { get; set; } = string.Empty;
        public string FilterName { get; set; } = string.Empty;
        public double FrameSnr { get; set; }
        public string Grade { get; set; } = string.Empty;
        public bool Keep { get; set; }
        public int Rank { get; set; }
        public int FrameCountInFilter { get; set; }
    }
}
