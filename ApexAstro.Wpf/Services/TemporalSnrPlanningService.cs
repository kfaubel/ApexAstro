using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using ApexAstro.Wpf.Models;

namespace ApexAstro.Wpf.Services
{
    public sealed class TemporalSnrPlanningService
    {
        private const int HistorySchemaVersion = 1;
        private const int MaxHistoryEntries = 250;

        public TemporalSnrPreflightPlan BuildPlan(IReadOnlyCollection<FileItem> files)
        {
            var hardware = DetectHardware();
            long totalBytes = files.Sum(file => file.Size);
            double totalMegabytes = totalBytes / 1024.0 / 1024.0;
            int fileCount = files.Count;

            // Baseline model tuned for modern multicore machines; history calibration below
            // corrects this further after real runs are observed on the local machine.
            double baseSeconds = Math.Max(20.0, fileCount * 1.9 + totalMegabytes * 0.10);

            double coreFactor = hardware.LogicalProcessors > 0
                ? Math.Clamp(12.0 / hardware.LogicalProcessors, 0.50, 1.55)
                : 1.0;

            double speedFactor = hardware.CpuSpeedMhz > 0
                ? Math.Clamp(3400.0 / hardware.CpuSpeedMhz, 0.82, 1.30)
                : 1.0;

            double ramGb = hardware.TotalRamBytes / 1024.0 / 1024.0 / 1024.0;
            double ramFactor = ramGb >= 64 ? 0.82 : ramGb >= 32 ? 0.92 : 1.12;

            double estimatedSeconds = baseSeconds * coreFactor * speedFactor * ramFactor;
            int calibrationSamples = 0;
            if (TryGetCalibrationMultiplier(hardware, out double calibrationMultiplier, out calibrationSamples))
            {
                // Keep calibration bounded so one anomalous run cannot over-correct estimates.
                estimatedSeconds *= Math.Clamp(calibrationMultiplier, 0.45, 1.55);
            }

            var estimate = TimeSpan.FromSeconds(estimatedSeconds);
            double lowFactor = calibrationSamples >= 3 ? 0.88 : 0.80;
            double highFactor = calibrationSamples >= 3 ? 1.18 : 1.30;
            var low = TimeSpan.FromSeconds(Math.Max(12.0, estimatedSeconds * lowFactor));
            var high = TimeSpan.FromSeconds(Math.Max(18.0, estimatedSeconds * highFactor));

            return new TemporalSnrPreflightPlan
            {
                Hardware = hardware,
                EstimatedDuration = estimate,
                EstimatedMinimumDuration = low,
                EstimatedMaximumDuration = high,
                TotalMegabytes = totalMegabytes,
                FileCount = fileCount,
                UseFullMachineDefault = hardware.LogicalProcessors >= 12 || ramGb >= 32,
                SaveIntermediateDataDefault = true,
                StopOnMismatchDefault = true,
                SummaryText = BuildSummaryText(hardware, fileCount, totalMegabytes, low, high, calibrationSamples)
            };
        }

        public void RecordObservedRun(IReadOnlyCollection<FileItem> files, TimeSpan elapsed)
        {
            if (files == null || files.Count == 0)
                return;

            double elapsedSeconds = elapsed.TotalSeconds;
            if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 1.0)
                return;

            try
            {
                var hardware = DetectHardware();
                long totalBytes = files.Sum(file => file.Size);
                double totalMegabytes = totalBytes / 1024.0 / 1024.0;
                int fileCount = files.Count;

                var store = LoadHistoryStore();
                store.SchemaVersion = HistorySchemaVersion;
                store.Runs.Add(new TemporalSnrObservedRun
                {
                    TimestampUtc = DateTime.UtcNow,
                    CpuName = hardware.CpuName,
                    LogicalProcessors = hardware.LogicalProcessors,
                    TotalRamBytes = hardware.TotalRamBytes,
                    FileCount = fileCount,
                    TotalMegabytes = totalMegabytes,
                    ElapsedSeconds = elapsedSeconds
                });

                store.Runs = store.Runs
                    .OrderByDescending(run => run.TimestampUtc)
                    .Take(MaxHistoryEntries)
                    .ToList();

                SaveHistoryStore(store);
            }
            catch
            {
                // Estimation telemetry should never block user workflows.
            }
        }

        public TemporalSnrHardwareInfo DetectHardware()
        {
            var hardware = new TemporalSnrHardwareInfo
            {
                LogicalProcessors = Environment.ProcessorCount,
                PhysicalCores = Environment.ProcessorCount,
                TotalRamBytes = GetTotalRamBytes(),
                CpuName = string.Empty,
                CpuSpeedMhz = 0
            };

            try
            {
                using var cpuSearcher = new ManagementObjectSearcher("SELECT Name, MaxClockSpeed, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
                foreach (ManagementObject cpu in cpuSearcher.Get())
                {
                    hardware.CpuName = cpu["Name"]?.ToString() ?? hardware.CpuName;
                    hardware.CpuSpeedMhz = Convert.ToInt32(cpu["MaxClockSpeed"] ?? hardware.CpuSpeedMhz);
                    hardware.PhysicalCores = Convert.ToInt32(cpu["NumberOfCores"] ?? hardware.PhysicalCores);
                    hardware.LogicalProcessors = Convert.ToInt32(cpu["NumberOfLogicalProcessors"] ?? hardware.LogicalProcessors);
                    break;
                }
            }
            catch
            {
                // Leave defaults in place.
            }

            return hardware;
        }

        private static ulong GetTotalRamBytes()
        {
            try
            {
                var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
                return computerInfo.TotalPhysicalMemory;
            }
            catch
            {
                return 0;
            }
        }

        private static string BuildSummaryText(TemporalSnrHardwareInfo hardware, int fileCount, double totalMegabytes, TimeSpan low, TimeSpan high, int calibrationSamples)
        {
            string cpu = string.IsNullOrWhiteSpace(hardware.CpuName) ? "CPU" : hardware.CpuName;
            string ramGb = hardware.TotalRamBytes > 0
                ? (hardware.TotalRamBytes / 1024.0 / 1024.0 / 1024.0).ToString("0.#", CultureInfo.InvariantCulture)
                : "unknown";

            string calibrationText = calibrationSamples > 0
                ? $" Calibrated from {calibrationSamples} prior run{(calibrationSamples == 1 ? string.Empty : "s")}."
                : string.Empty;

            return $"{fileCount} files ({totalMegabytes:0.#} MB). {cpu}, {hardware.PhysicalCores} cores / {hardware.LogicalProcessors} threads, {ramGb} GB RAM. Estimated {FormatSpan(low)} to {FormatSpan(high)}.{calibrationText}";
        }

        private static string GetHistoryPath()
        {
            string basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ApexAstro");
            Directory.CreateDirectory(basePath);
            return Path.Combine(basePath, "temporal-snr-estimation-history.json");
        }

        private static TemporalSnrHistoryStore LoadHistoryStore()
        {
            try
            {
                string path = GetHistoryPath();
                if (!File.Exists(path))
                    return new TemporalSnrHistoryStore();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var store = JsonSerializer.Deserialize<TemporalSnrHistoryStore>(File.ReadAllText(path), options);
                return store ?? new TemporalSnrHistoryStore();
            }
            catch
            {
                return new TemporalSnrHistoryStore();
            }
        }

        private static void SaveHistoryStore(TemporalSnrHistoryStore store)
        {
            string path = GetHistoryPath();
            string json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        private static bool TryGetCalibrationMultiplier(TemporalSnrHardwareInfo hardware, out double multiplier, out int sampleCount)
        {
            multiplier = 1.0;
            sampleCount = 0;

            var store = LoadHistoryStore();
            if (store.Runs.Count == 0)
                return false;

            var machineRuns = store.Runs
                .Where(run => string.Equals(run.CpuName, hardware.CpuName, StringComparison.OrdinalIgnoreCase)
                    && run.LogicalProcessors == hardware.LogicalProcessors)
                .ToList();

            if (machineRuns.Count == 0)
                machineRuns = store.Runs
                    .Where(run => run.LogicalProcessors == hardware.LogicalProcessors)
                    .ToList();

            if (machineRuns.Count == 0)
                return false;

            var ratios = new List<double>(machineRuns.Count);
            foreach (var run in machineRuns)
            {
                // Reconstruct baseline estimate for this historical run without calibration.
                double baseSeconds = Math.Max(20.0, run.FileCount * 1.9 + run.TotalMegabytes * 0.10);
                double coreFactor = run.LogicalProcessors > 0
                    ? Math.Clamp(12.0 / run.LogicalProcessors, 0.50, 1.55)
                    : 1.0;
                double ramGb = run.TotalRamBytes / 1024.0 / 1024.0 / 1024.0;
                double ramFactor = ramGb >= 64 ? 0.82 : ramGb >= 32 ? 0.92 : 1.12;

                // Speed factor is unavailable in persisted history; assume neutral for calibration ratio.
                double predicted = baseSeconds * coreFactor * ramFactor;
                if (predicted <= 1.0 || run.ElapsedSeconds <= 1.0)
                    continue;

                ratios.Add(run.ElapsedSeconds / predicted);
            }

            if (ratios.Count == 0)
                return false;

            ratios.Sort();
            sampleCount = ratios.Count;
            multiplier = ratios[ratios.Count / 2];
            return true;
        }

        private static string FormatSpan(TimeSpan span)
        {
            if (span.TotalHours >= 1)
                return span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);

            return span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }
    }

    public sealed class TemporalSnrHardwareInfo
    {
        public string CpuName { get; set; } = string.Empty;
        public int CpuSpeedMhz { get; set; }
        public int PhysicalCores { get; set; }
        public int LogicalProcessors { get; set; }
        public ulong TotalRamBytes { get; set; }
    }

    public sealed class TemporalSnrPreflightPlan
    {
        public required TemporalSnrHardwareInfo Hardware { get; init; }
        public required TimeSpan EstimatedDuration { get; init; }
        public required TimeSpan EstimatedMinimumDuration { get; init; }
        public required TimeSpan EstimatedMaximumDuration { get; init; }
        public required double TotalMegabytes { get; init; }
        public required int FileCount { get; init; }
        public required bool UseFullMachineDefault { get; init; }
        public required bool SaveIntermediateDataDefault { get; init; }
        public required bool StopOnMismatchDefault { get; init; }
        public required string SummaryText { get; init; }
    }

    public sealed class TemporalSnrHistoryStore
    {
        public int SchemaVersion { get; set; } = 1;
        public List<TemporalSnrObservedRun> Runs { get; set; } = new();
    }

    public sealed class TemporalSnrObservedRun
    {
        public DateTime TimestampUtc { get; set; }
        public string CpuName { get; set; } = string.Empty;
        public int LogicalProcessors { get; set; }
        public ulong TotalRamBytes { get; set; }
        public int FileCount { get; set; }
        public double TotalMegabytes { get; set; }
        public double ElapsedSeconds { get; set; }
    }
}
