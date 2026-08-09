using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace ApexAstro.Core
{
    public enum TemporalSnrExecutionProfile
    {
        Auto,
        Portable,
        Balanced,
        Max
    }

    public sealed class TemporalSnrOptions
    {
        public TemporalSnrExecutionProfile Profile { get; set; } = TemporalSnrExecutionProfile.Auto;
        public bool UseFullMachine { get; set; } = false;
        public bool RegisterFrames { get; set; } = true;
        public bool StopOnMismatch { get; set; } = true;
        public bool SaveIntermediateData { get; set; } = true;
        public bool ComputePerFrameSnr { get; set; } = true;
        public double KeepBestPercent { get; set; } = 80.0;
        public int? MaxDegreeOfParallelism { get; set; }
        public int SamplePixelCount { get; set; } = 10_000;
        public int MinSamples { get; set; } = 4;
        public double MadSigmaThreshold { get; set; } = 3.5;
        public string[] Extensions { get; set; } = new[] { ".fits", ".fit", ".fts" };
    }

    public sealed class TemporalSnrProgressInfo
    {
        public string Stage { get; init; } = string.Empty;
        public int StageIndex { get; init; }
        public int StageCount { get; init; }
        public double PercentComplete { get; init; }
        public double? OverallPercentComplete { get; init; }
        public double? CurrentFilterPercentComplete { get; init; }
        public string Message { get; init; } = string.Empty;
        public string? FilterName { get; init; }
    }

    public sealed class TemporalSnrComputationResult
    {
        public required IReadOnlyDictionary<string, TemporalSnrFilterResult> FilterResults { get; init; }
        public required IReadOnlyList<TemporalSnrFrameResult> FrameResults { get; init; }
        public required TemporalSnrExecutionProfile EffectiveProfile { get; init; }
        public required int ThreadsUsed { get; init; }
    }

    public sealed class TemporalSnrFrameResult
    {
        public required string FileName { get; init; }
        public required string FilterName { get; init; }
        public required double FrameSnr { get; init; }
        public required string Grade { get; init; }
        public required bool Keep { get; init; }
        public required int Rank { get; init; }
        public required int FrameCountInFilter { get; init; }
    }

    public sealed class TemporalSnrFilterResult
    {
        public required string FilterName { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int FrameCount { get; init; }
        public required string ReferenceFile { get; init; }
        public required IReadOnlyList<TemporalSnrSourceValidation> SourceFiles { get; init; }
        public required float[] TemporalSnrMap { get; init; }
        public required int[] RejectedSampleCountMap { get; init; }
        public required double MeanTemporalSnr { get; init; }
        public required double MedianTemporalSnr { get; init; }
        public double? StackEquivalentSnr { get; init; }
        public double? TargetMedianTemporalSnr { get; init; }
        public double? BackgroundMedianTemporalSnr { get; init; }
        public required double StandardDeviationTemporalSnr { get; init; }
    }

    public sealed class TemporalSnrSourceValidation
    {
        public required string FileName { get; init; }
        public required DateTime LastWriteUtc { get; init; }
        public required long FileSizeBytes { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int Bitpix { get; init; }
        public double? Hfr { get; init; }
    }

    public sealed class TemporalSnrService
    {
        private const double ValidateProgress = 0.05;
        private const double ComputeStageSpan = 0.90;
        private const double SaveProgress = 0.98;

        private static readonly HashSet<string> KnownFilters = new(StringComparer.OrdinalIgnoreCase)
        {
            "L", "R", "G", "B", "HA", "OIII", "SII", "H", "O", "S", "LUM", "RGB"
        };

        public TemporalSnrComputationResult ComputeDirectory(string directoryPath, TemporalSnrOptions? options = null, CancellationToken cancellationToken = default, IProgress<TemporalSnrProgressInfo>? progress = null)
        {
            options ??= new TemporalSnrOptions();
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new ArgumentException("Directory path is required", nameof(directoryPath));
            if (!Directory.Exists(directoryPath))
                throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
            if (options.MinSamples < 2)
                throw new ArgumentOutOfRangeException(nameof(options.MinSamples), "MinSamples must be >= 2");
            if (options.SamplePixelCount < 0)
                throw new ArgumentOutOfRangeException(nameof(options.SamplePixelCount), "SamplePixelCount must be >= 0");
            if (options.MadSigmaThreshold <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.MadSigmaThreshold), "MadSigmaThreshold must be > 0");

            string[] files = Directory.GetFiles(directoryPath)
                .Where(path => options.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
                throw new InvalidOperationException("No FITS files found in the target directory");

            progress?.Report(new TemporalSnrProgressInfo
            {
                Stage = "Validate",
                StageIndex = 1,
                StageCount = 5,
                PercentComplete = 0.05,
                OverallPercentComplete = ValidateProgress,
                Message = "Reading headers and grouping source frames"
            });

            var frameDescriptors = new List<FrameDescriptor>(files.Length);
            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Dictionary<string, object> header = FitsParser.ParseHeaderFromFile(file);
                int bitpix = TryGetIntHeaderValue(header, "BITPIX");
                int width = TryGetIntHeaderValue(header, "NAXIS1");
                int height = TryGetIntHeaderValue(header, "NAXIS2");
                string filter = ResolveFilterName(file, header);
                double? hfr = ParseHfrFromFilename(Path.GetFileName(file));

                frameDescriptors.Add(new FrameDescriptor(file, filter, width, height, bitpix, hfr));
            }

            var grouped = frameDescriptors.GroupBy(f => f.FilterName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            TemporalSnrExecutionProfile effectiveProfile = ResolveProfile(options.Profile);
            int threadCount = ResolveThreadCount(effectiveProfile, options);

            var results = new Dictionary<string, TemporalSnrFilterResult>(StringComparer.OrdinalIgnoreCase);
            var frameResults = new List<TemporalSnrFrameResult>();
            int stageIndex = 2;
            int stageCount = Math.Max(3, grouped.Count + 2);
            for (int filterIndex = 0; filterIndex < grouped.Count; filterIndex++)
            {
                var group = grouped[filterIndex];
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new TemporalSnrProgressInfo
                {
                    Stage = "Filter",
                    StageIndex = stageIndex,
                    StageCount = stageCount,
                    PercentComplete = (stageIndex - 1) / (double)stageCount,
                    OverallPercentComplete = ComposeOverallProgress(filterIndex, grouped.Count, 0.0),
                    CurrentFilterPercentComplete = 0.0,
                    Message = $"Processing filter {group.Key}",
                    FilterName = group.Key
                });

                var frames = new List<FrameData>(group.Count());
                foreach (FrameDescriptor descriptor in group)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] buffer = File.ReadAllBytes(descriptor.Path);
                    var (width, height, pixels, header) = FitsParser.ReadImageAsFloat(buffer);
                    int bitpix = header.TryGetValue("BITPIX", out var bitpixObj) ? Convert.ToInt32(bitpixObj) : descriptor.Bitpix;

                    frames.Add(new FrameData(
                        descriptor.Path,
                        group.Key,
                        width,
                        height,
                        bitpix,
                        descriptor.Hfr,
                        pixels));
                }

                var (filterResult, perFrameResults) = ComputeForFilterGroup(group.Key, frames, options, threadCount, cancellationToken, progress, stageIndex, stageCount, filterIndex, grouped.Count);
                results[group.Key] = filterResult;
                if (options.ComputePerFrameSnr)
                    frameResults.AddRange(perFrameResults);
                stageIndex++;
            }

            progress?.Report(new TemporalSnrProgressInfo
            {
                Stage = "Save",
                StageIndex = stageCount,
                StageCount = stageCount,
                PercentComplete = SaveProgress,
                OverallPercentComplete = SaveProgress,
                CurrentFilterPercentComplete = 1.0,
                Message = options.SaveIntermediateData ? "Saving intermediate cache data" : "Finalizing results"
            });

            return new TemporalSnrComputationResult
            {
                FilterResults = results,
                FrameResults = frameResults,
                EffectiveProfile = effectiveProfile,
                ThreadsUsed = threadCount
            };
        }

        private static (TemporalSnrFilterResult filterResult, List<TemporalSnrFrameResult> frameResults) ComputeForFilterGroup(
            string filterName,
            List<FrameData> frames,
            TemporalSnrOptions options,
            int threadCount,
            CancellationToken cancellationToken,
            IProgress<TemporalSnrProgressInfo>? progress,
            int stageIndex,
            int stageCount,
            int filterIndex,
            int filterCount)
        {
            if (frames.Count == 0)
                throw new InvalidOperationException($"Filter group {filterName} has no frames");

            FrameData baseline = frames[0];
            for (int i = 1; i < frames.Count; i++)
            {
                FrameData frame = frames[i];
                bool mismatch = frame.Width != baseline.Width
                    || frame.Height != baseline.Height
                    || frame.Bitpix != baseline.Bitpix;

                if (mismatch && options.StopOnMismatch)
                {
                    throw new InvalidOperationException(
                        $"Frame format mismatch detected in filter {filterName}. " +
                        $"Expected {baseline.Width}x{baseline.Height} BITPIX={baseline.Bitpix}, " +
                        $"got {frame.Width}x{frame.Height} BITPIX={frame.Bitpix} for {Path.GetFileName(frame.Path)}.");
                }
            }

            FrameData referenceFrame = SelectReferenceFrame(frames);
            float[] referencePixels = referenceFrame.Pixels;

            int totalAlignmentFrames = Math.Max(1, frames.Count);
            int alignmentProcessed = 0;
            var alignedFrames = new float[frames.Count][];

            int alignmentThreadCount = ResolveAlignmentThreadCount(threadCount, frames.Count);
            var alignmentOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = alignmentThreadCount,
                CancellationToken = cancellationToken
            };

            Parallel.For(0, frames.Count, alignmentOptions, frameIndex =>
            {
                FrameData frame = frames[frameIndex];
                if (!options.RegisterFrames || frame.Path.Equals(referenceFrame.Path, StringComparison.OrdinalIgnoreCase))
                {
                    alignedFrames[frameIndex] = frame.Pixels;
                }
                else
                {
                    alignedFrames[frameIndex] = TryRegisterWithEcc(referencePixels, frame.Pixels, baseline.Width, baseline.Height);
                }

                int processed = Interlocked.Increment(ref alignmentProcessed);
                ReportAlignmentProgress(progress, filterName, stageIndex, stageCount, filterIndex, filterCount, processed, totalAlignmentFrames);
            });

            var workingSet = alignedFrames.ToList();
            var frameOffsets = workingSet.Select(ComputeFrameBackgroundOffset).ToArray();

            var geminiOffsets = new double[workingSet.Count];

            (float[] snrMap, int[] rejectedMap) = ComputeSnrMaps(
                workingSet,
                geminiOffsets,
                baseline.Width,
                baseline.Height,
                options.SamplePixelCount,
                options.MinSamples,
                double.PositiveInfinity,
                threadCount,
                cancellationToken,
                progress,
                filterName,
                stageIndex,
                stageCount,
                filterIndex,
                filterCount);

            var geminiStats = ComputeSnrStatistics(snrMap);
            int pixelCount = baseline.Width * baseline.Height;
            bool useSampling = options.SamplePixelCount > 0 && options.SamplePixelCount < pixelCount;
            int[]? splitIndices = useSampling
                ? BuildUniformSampleIndices(pixelCount, options.SamplePixelCount)
                : null;
            var (geminiTargetMedianSnr, geminiBackgroundMedianSnr) = ComputeTargetBackgroundSnrMedians(
                snrMap,
                referencePixels,
                splitIndices);

            double geminiSignalSnr = IsFinitePositive(geminiTargetMedianSnr)
                ? geminiTargetMedianSnr!.Value
                : geminiStats.meanSnr;
            double stackEquivalentSnr = geminiSignalSnr > 0
                ? geminiSignalSnr
                : 0.0;

            var scoredFrames = ComputeFrameSnrScores(frames, workingSet, frameOffsets);

            var filterResult = new TemporalSnrFilterResult
            {
                FilterName = filterName,
                Width = baseline.Width,
                Height = baseline.Height,
                FrameCount = frames.Count,
                ReferenceFile = referenceFrame.Path,
                SourceFiles = frames
                    .Select(frame => new TemporalSnrSourceValidation
                    {
                        FileName = Path.GetFileName(frame.Path),
                        LastWriteUtc = File.GetLastWriteTimeUtc(frame.Path),
                        FileSizeBytes = new FileInfo(frame.Path).Length,
                        Width = frame.Width,
                        Height = frame.Height,
                        Bitpix = frame.Bitpix,
                        Hfr = frame.Hfr
                    })
                    .ToList(),
                TemporalSnrMap = snrMap,
                RejectedSampleCountMap = rejectedMap,
                MeanTemporalSnr = geminiStats.meanSnr,
                MedianTemporalSnr = geminiStats.medianSnr,
                StackEquivalentSnr = stackEquivalentSnr,
                TargetMedianTemporalSnr = geminiTargetMedianSnr,
                BackgroundMedianTemporalSnr = geminiBackgroundMedianSnr,
                StandardDeviationTemporalSnr = geminiStats.stdDevSnr
            };

            var frameResults = options.ComputePerFrameSnr
                ? ComputePerFrameScores(filterName, scoredFrames, options.KeepBestPercent)
                : new List<TemporalSnrFrameResult>();

            return (filterResult, frameResults);
        }

        private static List<(FrameData frame, double score)> ComputeFrameSnrScores(
            List<FrameData> frames,
            List<float[]> alignedFrames,
            IReadOnlyList<double> frameOffsets)
        {
            const double epsilon = 1e-8;
            var scoredFrames = new List<(FrameData frame, double score)>();

            for (int i = 0; i < frames.Count; i++)
            {
                float[] pixels = alignedFrames[i];
                double frameOffset = i < frameOffsets.Count ? frameOffsets[i] : 0.0;
                int sampleCount = 0;
                double mean = 0.0;
                double m2 = 0.0;

                for (int p = 0; p < pixels.Length; p++)
                {
                    float value = pixels[p];
                    if (!float.IsFinite(value))
                        continue;

                    double adjusted = value - frameOffset;

                    sampleCount++;
                    double delta = adjusted - mean;
                    mean += delta / sampleCount;
                    double delta2 = adjusted - mean;
                    m2 += delta * delta2;
                }

                double variance = sampleCount > 1 ? m2 / (sampleCount - 1) : 0.0;
                double stdDev = Math.Sqrt(Math.Max(variance, 0.0));
                double frameSnr = stdDev > epsilon ? mean / stdDev : 0.0;
                if (!double.IsFinite(frameSnr) || frameSnr < 0)
                    frameSnr = 0.0;

                scoredFrames.Add((frames[i], frameSnr));
            }

            return scoredFrames;
        }

        private static (double meanSnr, double medianSnr, double stdDevSnr) ComputeSnrStatistics(float[] snrMap)
        {
            double[] snrFinite = snrMap.Where(v => float.IsFinite(v) && v > 0).Select(v => (double)v).ToArray();
            return ComputeSnrStatistics(snrFinite);
        }

        private static (double meanSnr, double medianSnr, double stdDevSnr) ComputeSnrStatistics(IEnumerable<double> snrValues)
        {
            double[] snrFinite = snrValues.Where(v => double.IsFinite(v) && v > 0).ToArray();
            double meanSnr = 0.0;
            if (snrFinite.Length > 0)
            {
                meanSnr = snrFinite.Average();
            }

            double medianSnr = 0.0;
            if (snrFinite.Length > 0)
            {
                Array.Sort(snrFinite);
                int mid = snrFinite.Length / 2;
                medianSnr = snrFinite.Length % 2 == 0
                    ? (snrFinite[mid - 1] + snrFinite[mid]) / 2.0
                    : snrFinite[mid];
            }

            double stdDevSnr = 0.0;
            if (snrFinite.Length > 1)
            {
                double sumSquares = 0.0;
                for (int i = 0; i < snrFinite.Length; i++)
                {
                    double delta = snrFinite[i] - meanSnr;
                    sumSquares += delta * delta;
                }

                stdDevSnr = Math.Sqrt(sumSquares / (snrFinite.Length - 1));
            }

            return (meanSnr, medianSnr, stdDevSnr);
        }

        private static double ComputeStackEquivalentSnr(IEnumerable<double> frameSnrValues)
        {
            double sumSquares = 0.0;
            foreach (double value in frameSnrValues)
            {
                if (!double.IsFinite(value) || value <= 0)
                    continue;

                sumSquares += value * value;
            }

            return Math.Sqrt(sumSquares);
        }

        private static List<TemporalSnrFrameResult> ComputePerFrameScores(
            string filterName,
            List<(FrameData frame, double score)> scoredFrames,
            double keepBestPercent)
        {
            var sorted = scoredFrames
                .OrderByDescending(entry => entry.score)
                .ToList();

            int frameCount = sorted.Count;
            if (frameCount == 0)
                return new List<TemporalSnrFrameResult>();

            double keepRatio = Math.Clamp(keepBestPercent / 100.0, 0.01, 1.0);
            int keepCount = Math.Clamp((int)Math.Ceiling(frameCount * keepRatio), 1, frameCount);
            int yellowCount = Math.Max(1, (int)Math.Ceiling(frameCount * 0.15));

            var results = new List<TemporalSnrFrameResult>(frameCount);
            for (int index = 0; index < sorted.Count; index++)
            {
                string grade = index < keepCount
                    ? "Green"
                    : index < keepCount + yellowCount
                        ? "Yellow"
                        : "Red";

                results.Add(new TemporalSnrFrameResult
                {
                    FileName = Path.GetFileName(sorted[index].frame.Path),
                    FilterName = filterName,
                    FrameSnr = sorted[index].score,
                    Grade = grade,
                    Keep = index < keepCount,
                    Rank = index + 1,
                    FrameCountInFilter = frameCount
                });
            }

            return results;
        }

        private static (float[] snrMap, int[] rejectedMap) ComputeSnrMaps(
            List<float[]> frames,
            IReadOnlyList<double> frameOffsets,
            int width,
            int height,
            int samplePixelCount,
            int minSamples,
            double madSigmaThreshold,
            int threadCount,
            CancellationToken cancellationToken,
            IProgress<TemporalSnrProgressInfo>? progress,
            string filterName,
            int stageIndex,
            int stageCount,
            int filterIndex,
            int filterCount)
        {
            int pixelCount = width * height;
            int frameCount = frames.Count;
            var snrMap = new float[pixelCount];
            var rejectedMap = new int[pixelCount];
            const double epsilon = 1e-8;
            bool useSampling = samplePixelCount > 0 && samplePixelCount < pixelCount;
            int[] sampledIndices = useSampling
                ? BuildUniformSampleIndices(pixelCount, samplePixelCount)
                : Array.Empty<int>();
            int workPixelCount = useSampling ? sampledIndices.Length : pixelCount;

            if (useSampling)
            {
                Array.Fill(snrMap, float.NaN);
                Array.Fill(rejectedMap, -1);
            }

            int completedPixels = 0;
            int nextReportAt = Math.Max(100, workPixelCount / 100);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = threadCount,
                CancellationToken = cancellationToken
            };

            Parallel.For(0, workPixelCount, parallelOptions,
                () => new ThreadLocalBuffers(frameCount),
                (workIndex, _, buffers) =>
                {
                    int pixelIndex = useSampling ? sampledIndices[workIndex] : workIndex;
                    int count = 0;
                    for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                    {
                        float value = frames[frameIndex][pixelIndex];
                        if (float.IsFinite(value))
                        {
                            double frameOffset = frameIndex < frameOffsets.Count ? frameOffsets[frameIndex] : 0.0;
                            buffers.Values[count++] = (float)(value - frameOffset);
                        }
                    }

                    if (count < minSamples)
                    {
                        snrMap[pixelIndex] = 0f;
                        rejectedMap[pixelIndex] = 0;
                        return buffers;
                    }

                    double median = ComputeMedian(buffers.Values, count);
                    for (int i = 0; i < count; i++)
                    {
                        buffers.AbsDeviation[i] = (float)Math.Abs(buffers.Values[i] - median);
                    }

                    double mad = ComputeMedian(buffers.AbsDeviation, count);
                    double robustSigma = (1.4826 * mad) + epsilon;
                    // Guard against MAD collapsing to ~0 on quantized/low-noise pixels.
                    // Keep a practical minimum threshold so natural variation is retained
                    // while obvious outliers are still rejected.
                    double threshold = Math.Max(madSigmaThreshold * robustSigma, 1.0);

                    int kept = 0;
                    int rejected = 0;
                    double mean = 0.0;
                    double m2 = 0.0;

                    for (int i = 0; i < count; i++)
                    {
                        float value = buffers.Values[i];
                        if (Math.Abs(value - median) > threshold)
                        {
                            rejected++;
                            continue;
                        }

                        kept++;
                        double delta = value - mean;
                        mean += delta / kept;
                        double delta2 = value - mean;
                        m2 += delta * delta2;
                    }

                    if (kept < minSamples)
                    {
                        snrMap[pixelIndex] = 0f;
                        rejectedMap[pixelIndex] = rejected;
                        return buffers;
                    }

                    double variance = kept > 1 ? m2 / (kept - 1) : 0.0;
                    double stdDev = Math.Sqrt(Math.Max(variance, 0.0));
                    double snr = stdDev > epsilon ? mean / stdDev : 0.0;

                    snrMap[pixelIndex] = float.IsFinite((float)snr) && snr > 0 ? (float)snr : 0f;
                    rejectedMap[pixelIndex] = rejected;

                    int processed = Interlocked.Increment(ref completedPixels);
                    if (progress != null && (processed >= nextReportAt || processed == workPixelCount))
                    {
                        nextReportAt = processed + Math.Max(100, workPixelCount / 100);
                        double pixelRatio = Math.Clamp(processed / (double)workPixelCount, 0.0, 1.0);
                        double filterProgress = 0.20 + (0.75 * pixelRatio);
                        progress.Report(new TemporalSnrProgressInfo
                        {
                            Stage = "Compute",
                            StageIndex = stageIndex,
                            StageCount = stageCount,
                            PercentComplete = filterProgress,
                            OverallPercentComplete = ComposeOverallProgress(filterIndex, filterCount, filterProgress),
                            CurrentFilterPercentComplete = filterProgress,
                            Message = useSampling
                                ? $"Computing sampled temporal SNR for {filterName} ({workPixelCount:N0} points)"
                                : $"Computing temporal SNR for {filterName}",
                            FilterName = filterName
                        });
                    }
                    return buffers;
                },
                _ => { });

            return (snrMap, rejectedMap);
        }

        private static void ApplyFrameCountNormalization(float[] snrMap, int frameCount)
        {
            if (snrMap.Length == 0 || frameCount <= 0)
                return;

            double scale = Math.Sqrt(frameCount / 16.0);
            if (!double.IsFinite(scale) || scale <= 0)
                return;

            for (int i = 0; i < snrMap.Length; i++)
            {
                float value = snrMap[i];
                if (!float.IsFinite(value) || value <= 0f)
                    continue;

                snrMap[i] = (float)(value * scale);
            }
        }

        private static (double? targetMedianSnr, double? backgroundMedianSnr) ComputeTargetBackgroundSnrMedians(
            float[] snrMap,
            float[] referencePixels,
            IReadOnlyList<int>? pixelIndices = null)
        {
            if (snrMap.Length == 0 || referencePixels.Length != snrMap.Length)
                return (null, null);

            int estimatedCount = pixelIndices?.Count ?? snrMap.Length;
            var intensity = new List<double>(Math.Max(estimatedCount, 32));
            var snr = new List<double>(Math.Max(estimatedCount, 32));

            if (pixelIndices != null)
            {
                for (int j = 0; j < pixelIndices.Count; j++)
                {
                    int i = pixelIndices[j];
                    if ((uint)i >= (uint)snrMap.Length)
                        continue;

                    float snrValue = snrMap[i];
                    float referenceValue = referencePixels[i];
                    if (!float.IsFinite(snrValue) || !float.IsFinite(referenceValue))
                        continue;

                    intensity.Add(referenceValue);
                    snr.Add(snrValue);
                }
            }
            else
            {
                for (int i = 0; i < snrMap.Length; i++)
                {
                    float snrValue = snrMap[i];
                    float referenceValue = referencePixels[i];
                    if (!float.IsFinite(snrValue) || !float.IsFinite(referenceValue))
                        continue;

                    intensity.Add(referenceValue);
                    snr.Add(snrValue);
                }
            }

            if (snr.Count < 32)
                return (null, null);

            double threshold = ComputeOtsuThreshold(intensity);
            var split = SplitSnrByThreshold(intensity, snr, threshold);

            // Fallback when Otsu is degenerate (very flat backgrounds, tiny targets, etc.).
            if (!split.HasBothGroups)
            {
                double percentileThreshold = ComputePercentile(intensity, 70.0);
                split = SplitSnrByThreshold(intensity, snr, percentileThreshold);
            }

            if (!split.HasBothGroups)
                return (null, null);

            return (ComputeMedian(split.Target), ComputeMedian(split.Background));
        }

        private static (List<double> Target, List<double> Background, bool HasBothGroups) SplitSnrByThreshold(
            List<double> intensity,
            List<double> snr,
            double threshold)
        {
            var target = new List<double>();
            var background = new List<double>();

            for (int i = 0; i < intensity.Count; i++)
            {
                if (intensity[i] >= threshold)
                    target.Add(snr[i]);
                else
                    background.Add(snr[i]);
            }

            const int minGroupSize = 8;
            bool hasBothGroups = target.Count >= minGroupSize && background.Count >= minGroupSize;
            return (target, background, hasBothGroups);
        }

        private static double ComputeOtsuThreshold(List<double> values)
        {
            if (values.Count == 0)
                return 0.0;

            double min = values.Min();
            double max = values.Max();
            if (!double.IsFinite(min) || !double.IsFinite(max) || max <= min)
                return min;

            const int bins = 256;
            var histogram = new long[bins];
            double scale = (bins - 1) / (max - min);

            for (int i = 0; i < values.Count; i++)
            {
                int index = (int)Math.Round((values[i] - min) * scale);
                index = Math.Clamp(index, 0, bins - 1);
                histogram[index]++;
            }

            long total = values.Count;
            double sum = 0.0;
            for (int i = 0; i < bins; i++)
            {
                sum += i * histogram[i];
            }

            long weightBackground = 0;
            double sumBackground = 0.0;
            double maxBetweenClassVariance = double.MinValue;
            int thresholdBin = bins / 2;

            for (int i = 0; i < bins; i++)
            {
                weightBackground += histogram[i];
                if (weightBackground == 0)
                    continue;

                long weightForeground = total - weightBackground;
                if (weightForeground == 0)
                    break;

                sumBackground += i * histogram[i];
                double meanBackground = sumBackground / weightBackground;
                double meanForeground = (sum - sumBackground) / weightForeground;
                double betweenClassVariance = weightBackground * weightForeground * Math.Pow(meanBackground - meanForeground, 2);

                if (betweenClassVariance > maxBetweenClassVariance)
                {
                    maxBetweenClassVariance = betweenClassVariance;
                    thresholdBin = i;
                }
            }

            return min + (thresholdBin / scale);
        }

        private static double ComputePercentile(List<double> values, double percentile)
        {
            if (values.Count == 0)
                return 0.0;

            var sorted = values.ToArray();
            Array.Sort(sorted);

            double p = Math.Clamp(percentile, 0.0, 100.0) / 100.0;
            double position = (sorted.Length - 1) * p;
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);

            if (lower == upper)
                return sorted[lower];

            double fraction = position - lower;
            return sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
        }

        private static double ComputeMedian(List<double> values)
        {
            if (values.Count == 0)
                return 0.0;

            var sorted = values.ToArray();
            Array.Sort(sorted);
            int mid = sorted.Length / 2;
            if ((sorted.Length & 1) == 0)
            {
                return (sorted[mid - 1] + sorted[mid]) / 2.0;
            }

            return sorted[mid];
        }

        private static double ComputeFrameBackgroundOffset(float[] pixels)
        {
            int finiteCount = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (float.IsFinite(pixels[i]))
                    finiteCount++;
            }

            if (finiteCount == 0)
                return 0.0;

            var values = new float[finiteCount];
            int index = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (float.IsFinite(pixels[i]))
                    values[index++] = pixels[i];
            }

            return ComputeMedian(values, finiteCount);
        }

        private static int[] BuildUniformSampleIndices(int pixelCount, int sampleCount)
        {
            int clampedCount = Math.Clamp(sampleCount, 1, pixelCount);
            var indices = new int[clampedCount];
            double step = pixelCount / (double)clampedCount;
            int previous = -1;

            for (int i = 0; i < clampedCount; i++)
            {
                int index = (int)Math.Floor((i + 0.5) * step);
                index = Math.Clamp(index, 0, pixelCount - 1);
                if (index <= previous)
                    index = Math.Min(pixelCount - 1, previous + 1);

                indices[i] = index;
                previous = index;
            }

            return indices;
        }

        private static void ReportAlignmentProgress(
            IProgress<TemporalSnrProgressInfo>? progress,
            string filterName,
            int stageIndex,
            int stageCount,
            int filterIndex,
            int filterCount,
            int processedFrames,
            int totalFrames)
        {
            if (progress == null)
                return;

            double ratio = Math.Clamp(processedFrames / (double)Math.Max(1, totalFrames), 0.0, 1.0);
            double filterProgress = 0.20 * ratio;
            progress.Report(new TemporalSnrProgressInfo
            {
                Stage = "Compute",
                StageIndex = stageIndex,
                StageCount = stageCount,
                PercentComplete = filterProgress,
                OverallPercentComplete = ComposeOverallProgress(filterIndex, filterCount, filterProgress),
                CurrentFilterPercentComplete = filterProgress,
                Message = $"Aligning frames for {filterName} ({processedFrames}/{totalFrames})",
                FilterName = filterName
            });
        }

        private static double ComposeOverallProgress(int filterIndex, int filterCount, double currentFilterProgress)
        {
            if (filterCount <= 0)
                return ValidateProgress;

            double filterSlice = (filterIndex + Math.Clamp(currentFilterProgress, 0.0, 1.0)) / filterCount;
            return Math.Clamp(ValidateProgress + (filterSlice * ComputeStageSpan), ValidateProgress, SaveProgress);
        }

        private static bool IsFinitePositive(double? value)
        {
            return value.HasValue && double.IsFinite(value.Value) && value.Value > 0;
        }

        private static int TryGetIntHeaderValue(Dictionary<string, object> header, string key)
        {
            if (!header.TryGetValue(key, out var value) || value == null)
                return 0;

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static double ComputeMedian(float[] values, int count)
        {
            Array.Sort(values, 0, count);
            int middle = count / 2;
            if ((count & 1) == 0)
            {
                return (values[middle - 1] + values[middle]) / 2.0;
            }

            return values[middle];
        }

        private static TemporalSnrExecutionProfile ResolveProfile(TemporalSnrExecutionProfile requested)
        {
            if (requested != TemporalSnrExecutionProfile.Auto)
                return requested;

            int cpuCount = Environment.ProcessorCount;
            long availableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (cpuCount >= 24 && availableMemory >= 32L * 1024 * 1024 * 1024)
                return TemporalSnrExecutionProfile.Max;
            if (cpuCount <= 8)
                return TemporalSnrExecutionProfile.Portable;
            return TemporalSnrExecutionProfile.Balanced;
        }

        private static int ResolveThreadCount(TemporalSnrExecutionProfile profile, TemporalSnrOptions options)
        {
            if (options.MaxDegreeOfParallelism.HasValue && options.MaxDegreeOfParallelism.Value > 0)
                return options.MaxDegreeOfParallelism.Value;

            int cpu = Environment.ProcessorCount;
            if (options.UseFullMachine)
                return cpu;

            return profile switch
            {
                TemporalSnrExecutionProfile.Portable => Math.Max(1, cpu / 2),
                TemporalSnrExecutionProfile.Balanced => Math.Max(1, cpu - 2),
                TemporalSnrExecutionProfile.Max => cpu,
                _ => Math.Max(1, cpu - 2)
            };
        }

        private static int ResolveAlignmentThreadCount(int computeThreadCount, int frameCount)
        {
            int cpu = Environment.ProcessorCount;
            int cap = cpu >= 16 ? 4 : cpu >= 8 ? 2 : 1;
            if (frameCount <= 4)
                cap = Math.Min(cap, 2);

            return Math.Max(1, Math.Min(computeThreadCount, cap));
        }

        private static FrameData SelectReferenceFrame(List<FrameData> frames)
        {
            FrameData? best = null;
            foreach (FrameData frame in frames)
            {
                if (!frame.Hfr.HasValue)
                    continue;

                if (best == null || frame.Hfr.Value < best.Hfr.GetValueOrDefault(double.MaxValue))
                {
                    best = frame;
                }
            }

            return best ?? frames[0];
        }

        private static float[] TryRegisterWithEcc(float[] reference, float[] target, int width, int height)
        {
            try
            {
                using var referenceMat = ToMat(reference, width, height);
                using var targetMat = ToMat(target, width, height);
                using var warp = new Mat(2, 3, MatType.CV_32F, Scalar.All(0));
                warp.Set(0, 0, 1f);
                warp.Set(1, 1, 1f);

                var criteria = new TermCriteria(CriteriaTypes.Count | CriteriaTypes.Eps, 100, 1e-6);
                Cv2.FindTransformECC(referenceMat, targetMat, warp, MotionTypes.Translation, criteria);

                using var aligned = new Mat();
                Cv2.WarpAffine(
                    targetMat,
                    aligned,
                    warp,
                    new Size(width, height),
                    InterpolationFlags.Linear | InterpolationFlags.WarpInverseMap,
                    BorderTypes.Constant,
                    Scalar.All(0));

                return FromMat(aligned, width, height);
            }
            catch
            {
                // Registration failure should not block tSNR computation; keep original frame.
                return target;
            }
        }

        private static Mat ToMat(float[] pixels, int width, int height)
        {
            var mat = new Mat(height, width, MatType.CV_32FC1);
            var indexer = mat.GetGenericIndexer<float>();
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    indexer[y, x] = pixels[index++];
                }
            }

            return mat;
        }

        private static float[] FromMat(Mat mat, int width, int height)
        {
            var pixels = new float[width * height];
            var indexer = mat.GetGenericIndexer<float>();
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[index++] = indexer[y, x];
                }
            }

            return pixels;
        }

        private static string ResolveFilterName(string filePath, Dictionary<string, object> header)
        {
            if (header.TryGetValue("FILTER", out var filterObj) && filterObj != null)
            {
                string fromHeader = filterObj.ToString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(fromHeader))
                    return NormalizeFilterToken(fromHeader);
            }

            string name = Path.GetFileNameWithoutExtension(filePath);
            string[] tokens = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                string normalized = NormalizeFilterToken(token);
                if (KnownFilters.Contains(normalized))
                    return normalized;
            }

            return "UNKNOWN";
        }

        private static string NormalizeFilterToken(string token)
        {
            string trimmed = token.Trim().ToUpperInvariant();
            trimmed = trimmed.Replace(" ", string.Empty, StringComparison.Ordinal);
            trimmed = trimmed.Replace("-", string.Empty, StringComparison.Ordinal);
            return trimmed;
        }

        private static double? ParseHfrFromFilename(string? filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                return null;

            Match match = Regex.Match(filename, @"_HFR_([^_]+)", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double hfr))
                return hfr;

            return null;
        }

        private sealed class ThreadLocalBuffers
        {
            public ThreadLocalBuffers(int frameCount)
            {
                Values = new float[frameCount];
                AbsDeviation = new float[frameCount];
            }

            public float[] Values { get; }
            public float[] AbsDeviation { get; }
        }

        private sealed class FrameData
        {
            public FrameData(string path, string filterName, int width, int height, int bitpix, double? hfr, float[] pixels)
            {
                Path = path;
                FilterName = filterName;
                Width = width;
                Height = height;
                Bitpix = bitpix;
                Hfr = hfr;
                Pixels = pixels;
            }

            public string Path { get; }
            public string FilterName { get; }
            public int Width { get; }
            public int Height { get; }
            public int Bitpix { get; }
            public double? Hfr { get; }
            public float[] Pixels { get; }
        }

        private sealed class FrameDescriptor
        {
            public FrameDescriptor(string path, string filterName, int width, int height, int bitpix, double? hfr)
            {
                Path = path;
                FilterName = filterName;
                Width = width;
                Height = height;
                Bitpix = bitpix;
                Hfr = hfr;
            }

            public string Path { get; }
            public string FilterName { get; }
            public int Width { get; }
            public int Height { get; }
            public int Bitpix { get; }
            public double? Hfr { get; }
        }
    }
}
