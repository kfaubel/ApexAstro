using System.Globalization;
using System.Text.Json;
using ApexAstro.Core;

namespace ComputeSnr;

internal static class SnrCacheFileWriter
{
    private const string CacheFileName = ".apexastro.snr.json";
    private const int SchemaVersion = 2;

    public static string Write(string folderPath, TemporalSnrComputationResult result)
    {
        var manifest = new SnrCacheManifest
        {
            SchemaVersion = SchemaVersion,
            ComputedUtc = DateTime.UtcNow,
            FolderPath = folderPath,
            Filters = result.FilterResults.Values
                .OrderBy(filter => filter.FilterName, StringComparer.OrdinalIgnoreCase)
                .Select(filter => new SnrCacheFilterEntry
                {
                    FilterName = filter.FilterName,
                    Width = filter.Width,
                    Height = filter.Height,
                    FrameCount = filter.FrameCount,
                    IntegrationMinutes = ComputeIntegrationMinutes(filter.SourceFiles.Select(source => source.FileName), folderPath),
                    ReferenceFile = filter.ReferenceFile,
                    MeanTemporalSnr = filter.MeanTemporalSnr,
                    MedianTemporalSnr = filter.MedianTemporalSnr,
                    TargetMedianTemporalSnr = filter.TargetMedianTemporalSnr,
                    BackgroundMedianTemporalSnr = filter.BackgroundMedianTemporalSnr,
                    StandardDeviationTemporalSnr = filter.StandardDeviationTemporalSnr,
                    SourceFiles = filter.SourceFiles
                        .Select(source => new SnrCacheSourceEntry
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

        string outputPath = Path.Combine(folderPath, CacheFileName);
        string tempPath = outputPath + ".tmp." + Guid.NewGuid().ToString("N");

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(tempPath, json);

        if (File.Exists(outputPath))
        {
            File.Replace(tempPath, outputPath, null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, outputPath);
        }

        return outputPath;
    }

    private static double? ComputeIntegrationMinutes(IEnumerable<string> sourceFileNames, string folderPath)
    {
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

    private sealed class SnrCacheManifest
    {
        public int SchemaVersion { get; set; }
        public DateTime ComputedUtc { get; set; }
        public string FolderPath { get; set; } = string.Empty;
        public List<SnrCacheFilterEntry> Filters { get; set; } = new();
    }

    private sealed class SnrCacheFilterEntry
    {
        public string FilterName { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int FrameCount { get; set; }
        public double? IntegrationMinutes { get; set; }
        public string ReferenceFile { get; set; } = string.Empty;
        public double MeanTemporalSnr { get; set; }
        public double MedianTemporalSnr { get; set; }
        public double? TargetMedianTemporalSnr { get; set; }
        public double? BackgroundMedianTemporalSnr { get; set; }
        public double StandardDeviationTemporalSnr { get; set; }
        public List<SnrCacheSourceEntry> SourceFiles { get; set; } = new();
    }

    private sealed class SnrCacheSourceEntry
    {
        public string FileName { get; set; } = string.Empty;
        public DateTime LastWriteUtc { get; set; }
        public long FileSizeBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Bitpix { get; set; }
        public double? Hfr { get; set; }
    }
}
