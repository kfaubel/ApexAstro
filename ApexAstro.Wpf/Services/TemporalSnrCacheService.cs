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
        private const string CacheFileName = ".apexastro.snr.json";
        private const string LegacyCacheFileName = ".apexastro.temporal-snr.json";
        private const string SummaryFileName = ".apexastro.snr-summary.json";
        private const string LegacySummaryFileName = ".apexastro.snr.json";
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

            bool hasCacheFile = File.Exists(GetCachePath(folderPath)) || File.Exists(GetLegacyCachePath(folderPath));
            bool hasSummaryFile = File.Exists(GetSummaryPath(folderPath)) || File.Exists(GetLegacySummaryPath(folderPath));
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

            foreach (string cachePath in GetCandidateCachePaths(folderPath))
            {
                if (!File.Exists(cachePath))
                    continue;

                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    manifest = JsonSerializer.Deserialize<TemporalSnrCacheManifest>(File.ReadAllText(cachePath), options);
                    if (manifest == null || manifest.SchemaVersion != SchemaVersion)
                    {
                        manifest = null;
                        continue;
                    }

                    if (manifest.Filters == null || manifest.Filters.Count == 0)
                    {
                        manifest = null;
                        continue;
                    }

                    if (IsValid(folderPath, manifest))
                        return true;
                }
                catch
                {
                    manifest = null;
                }
            }

            manifest = null;
            return false;
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
                            IntegrationMinutes = ComputeIntegrationMinutes(
                                filter.SourceFiles.Select(source => source.FileName),
                                folderPath),
                            ReferenceFile = filter.ReferenceFile,
                            MeanTemporalSnr = filter.MeanTemporalSnr,
                            MedianTemporalSnr = filter.MedianTemporalSnr,
                            StackEquivalentSnr = filter.StackEquivalentSnr,
                            TargetMedianTemporalSnr = filter.TargetMedianTemporalSnr,
                            BackgroundMedianTemporalSnr = filter.BackgroundMedianTemporalSnr,
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
                            IntegrationMinutes = ComputeIntegrationMinutes(
                                filter.SourceFiles.Select(source => source.FileName),
                                folderPath),
                            MedianTemporalSnr = filter.MedianTemporalSnr,
                            StackEquivalentSnr = filter.StackEquivalentSnr,
                            TargetMedianTemporalSnr = filter.TargetMedianTemporalSnr,
                            BackgroundMedianTemporalSnr = filter.BackgroundMedianTemporalSnr,
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

            foreach (string summaryPath in GetCandidateSummaryPaths(folderPath))
            {
                if (!File.Exists(summaryPath))
                    continue;

                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    summary = JsonSerializer.Deserialize<TemporalSnrProjectSummary>(File.ReadAllText(summaryPath), options);
                    if (summary == null || summary.SchemaVersion < 1)
                    {
                        summary = null;
                        continue;
                    }

                    bool hasSummaryData = (summary.FilterSummaries?.Count ?? 0) > 0
                        || (summary.FrameSummaries?.Count ?? 0) > 0;
                    if (!hasSummaryData)
                    {
                        summary = null;
                        continue;
                    }

                    return true;
                }
                catch
                {
                    summary = null;
                }
            }

            summary = null;
            return false;
        }

        private static double? ComputeIntegrationMinutes(IEnumerable<string> sourceFileNames, string folderPath)
        {
            if (sourceFileNames == null)
                return null;

            double totalSeconds = 0.0;
            int exposureCount = 0;

            foreach (string fileName in sourceFileNames)
            {
                string filePath = Path.Combine(folderPath, fileName);
                double? exposureSeconds = TryReadExposureSeconds(filePath, fileName);
                if (!exposureSeconds.HasValue || exposureSeconds.Value <= 0)
                    continue;

                totalSeconds += exposureSeconds.Value;
                exposureCount++;
            }

            if (exposureCount == 0)
                return null;

            return totalSeconds / 60.0;
        }

        private static double? TryReadExposureSeconds(string filePath, string fallbackFileName)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var headers = FitsParser.ParseHeaderFromFile(filePath);
                    if (TryGetExposureFromHeader(headers, out double headerExposureSeconds))
                        return headerExposureSeconds;
                }
            }
            catch
            {
                // Fall back to filename parsing.
            }

            return TryParseExposureFromFilename(fallbackFileName);
        }

        private static bool TryGetExposureFromHeader(Dictionary<string, object> headers, out double exposureSeconds)
        {
            exposureSeconds = 0.0;

            if (TryGetHeaderDouble(headers, "EXPTIME", out exposureSeconds))
                return true;

            if (TryGetHeaderDouble(headers, "EXPOSURE", out exposureSeconds))
                return true;

            return false;
        }

        private static bool TryGetHeaderDouble(Dictionary<string, object> headers, string key, out double value)
        {
            value = 0.0;
            if (!headers.TryGetValue(key, out var raw) || raw == null)
                return false;

            if (raw is double d)
            {
                value = d;
                return value > 0;
            }

            if (raw is float f)
            {
                value = f;
                return value > 0;
            }

            if (raw is int i)
            {
                value = i;
                return value > 0;
            }

            if (raw is long l)
            {
                value = l;
                return value > 0;
            }

            string text = raw.ToString() ?? string.Empty;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                value = parsed;
                return value > 0;
            }

            return false;
        }

        private static double? TryParseExposureFromFilename(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(nameWithoutExtension))
                return null;

            string[] tokens = nameWithoutExtension.Split('_', StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                if (!token.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                    continue;

                string numericPart = token[..^1];
                if (double.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                    && seconds > 0)
                {
                    return seconds;
                }
            }

            return null;
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

        private static IEnumerable<string> GetCandidateCachePaths(string folderPath)
        {
            yield return GetCachePath(folderPath);
            yield return GetLegacyCachePath(folderPath);
        }

        private static IEnumerable<string> GetCandidateSummaryPaths(string folderPath)
        {
            yield return GetSummaryPath(folderPath);
            yield return GetLegacySummaryPath(folderPath);
        }

        private static string GetCachePath(string folderPath) => Path.Combine(folderPath, CacheFileName);
        private static string GetLegacyCachePath(string folderPath) => Path.Combine(folderPath, LegacyCacheFileName);
        private static string GetSummaryPath(string folderPath) => Path.Combine(folderPath, SummaryFileName);
        private static string GetLegacySummaryPath(string folderPath) => Path.Combine(folderPath, LegacySummaryFileName);
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
        public double? IntegrationMinutes { get; set; }
        public string ReferenceFile { get; set; } = string.Empty;
        public double MeanTemporalSnr { get; set; }
        public double MedianTemporalSnr { get; set; }
        public double? StackEquivalentSnr { get; set; }
        public double? TargetMedianTemporalSnr { get; set; }
        public double? BackgroundMedianTemporalSnr { get; set; }
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
        public double? IntegrationMinutes { get; set; }
        public double MedianTemporalSnr { get; set; }
        public double? StackEquivalentSnr { get; set; }
        public double? TargetMedianTemporalSnr { get; set; }
        public double? BackgroundMedianTemporalSnr { get; set; }
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
