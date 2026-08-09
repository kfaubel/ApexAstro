using ApexAstro.Core;

namespace ComputeSnr;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitUsage = 2;
    private const int ExitInputError = 3;
    private const int ExitFailure = 5;

    public static int Main(string[] args)
    {
        if (!TryParseArgs(args, out string directoryPath, out bool runSnr, out string? usageError))
        {
            if (!string.IsNullOrWhiteSpace(usageError))
                Console.Error.WriteLine(usageError);

            PrintUsage();
            return ExitUsage;
        }

        if (!runSnr)
        {
            PrintUsage();
            return ExitUsage;
        }

        if (!Directory.Exists(directoryPath))
        {
            Console.Error.WriteLine($"Directory not found: {directoryPath}");
            return ExitInputError;
        }

        if (!ValidateFitsFiles(directoryPath))
        {
            return ExitInputError;
        }

        try
        {
            var options = new TemporalSnrOptions
            {
                ComputePerFrameSnr = false,
                SaveIntermediateData = true
            };

            var service = new TemporalSnrService();
            TemporalSnrComputationResult result = service.ComputeDirectory(directoryPath, options);
            string outputPath = SnrCacheFileWriter.Write(directoryPath, result);

            Console.WriteLine($"SNR data written to {outputPath}");
            foreach (var filter in result.FilterResults.Values.OrderBy(r => r.FilterName, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"{filter.FilterName}: SNR {filter.MedianTemporalSnr:F0}");
            }

            return ExitSuccess;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to compute SNR: {ex.Message}");
            return ExitFailure;
        }
    }

    private static bool TryParseArgs(string[] args, out string directoryPath, out bool runSnr, out string? error)
    {
        directoryPath = string.Empty;
        runSnr = false;
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.Equals(arg, "--snr", StringComparison.OrdinalIgnoreCase))
            {
                runSnr = true;
                continue;
            }

            if (string.Equals(arg, "-d", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--directory", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    error = "Missing value for -d/--directory.";
                    return false;
                }

                directoryPath = args[++i];
                continue;
            }

            error = $"Unknown argument: {arg}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            error = "Missing required -d <path> argument.";
            return false;
        }

        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  compute-snr --snr -d <path-to-directory-with-fits-files>");
    }

    private static bool ValidateFitsFiles(string directoryPath)
    {
        string[] files = Directory.GetFiles(directoryPath)
            .Where(path => IsSupportedFitsFile(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        bool hasInvalidFiles = false;

        foreach (string file in files)
        {
            try
            {
                byte[] buffer = File.ReadAllBytes(file);
                _ = FitsParser.ReadImageAsFloat(buffer);
            }
            catch (Exception ex)
            {
                hasInvalidFiles = true;
                Console.WriteLine($"Invalid FITS file: {Path.GetFileName(file)}");
                Console.WriteLine($"  Error: {ex.Message}");
            }
        }

        if (hasInvalidFiles)
        {
            Console.WriteLine("SNR compute aborted due to invalid FITS files.");
            return false;
        }

        return true;
    }

    private static bool IsSupportedFitsFile(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return extension.Equals(".fits", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fit", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fts", StringComparison.OrdinalIgnoreCase);
    }
}
