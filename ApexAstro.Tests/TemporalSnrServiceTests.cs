using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ApexAstro.Core;
using Xunit;

namespace ApexAstro.Tests
{
    public class TemporalSnrServiceTests : IDisposable
    {
        private readonly string _tempDirectory;

        public TemporalSnrServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "ApexAstroTemporalSnrTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [Fact]
        public void ComputeDirectory_GroupsByFilterAndRejectsOutlierWithMad()
        {
            int width = 4;
            int height = 4;
            int pixelCount = width * height;

            short[] baseR1 = Enumerable.Repeat((short)100, pixelCount).ToArray();
            short[] baseR2 = Enumerable.Repeat((short)102, pixelCount).ToArray();
            short[] baseR3 = Enumerable.Repeat((short)101, pixelCount).ToArray();
            baseR3[5] = 10000; // Synthetic satellite-trail outlier at one pixel.

            short[] baseG1 = Enumerable.Repeat((short)200, pixelCount).ToArray();
            short[] baseG2 = Enumerable.Repeat((short)199, pixelCount).ToArray();
            short[] baseG3 = Enumerable.Repeat((short)201, pixelCount).ToArray();

            WriteFits16(Path.Combine(_tempDirectory, "2026-01-01_00-00-01_R_HFR_1.5_0001.fits"), width, height, "R", baseR1);
            WriteFits16(Path.Combine(_tempDirectory, "2026-01-01_00-00-02_R_HFR_1.2_0002.fits"), width, height, "R", baseR2);
            WriteFits16(Path.Combine(_tempDirectory, "2026-01-01_00-00-03_R_HFR_1.8_0003.fits"), width, height, "R", baseR3);

            WriteFits16(Path.Combine(_tempDirectory, "2026-01-01_00-00-01_G_HFR_1.4_0004.fits"), width, height, "G", baseG1);
            WriteFits16(Path.Combine(_tempDirectory, "2026-01-01_00-00-02_G_HFR_1.3_0005.fits"), width, height, "G", baseG2);
            WriteFits16(Path.Combine(_tempDirectory, "2026-01-01_00-00-03_G_HFR_1.6_0006.fits"), width, height, "G", baseG3);

            var service = new TemporalSnrService();
            var options = new TemporalSnrOptions
            {
                RegisterFrames = false,
                MinSamples = 2,
                MadSigmaThreshold = 3.5,
                Profile = TemporalSnrExecutionProfile.Portable
            };

            TemporalSnrComputationResult result = service.ComputeDirectory(_tempDirectory, options);

            Assert.Equal(2, result.FilterResults.Count);
            Assert.True(result.FilterResults.ContainsKey("R"));
            Assert.True(result.FilterResults.ContainsKey("G"));

            TemporalSnrFilterResult rResult = result.FilterResults["R"];
            Assert.Equal(width * height, rResult.TemporalSnrMap.Length);
            Assert.Equal(width * height, rResult.RejectedSampleCountMap.Length);
            Assert.Equal(1, rResult.RejectedSampleCountMap[5]);
            Assert.Contains("_R_", Path.GetFileName(rResult.ReferenceFile), StringComparison.OrdinalIgnoreCase);

            TemporalSnrFilterResult gResult = result.FilterResults["G"];
            Assert.Equal(3, gResult.FrameCount);
            Assert.True(gResult.RejectedSampleCountMap.All(v => v == 0));
        }

        [Fact]
        public void ComputeDirectory_MismatchedDimensions_ThrowsWhenStopOnMismatchEnabled()
        {
            short[] pixels4x4 = Enumerable.Repeat((short)100, 16).ToArray();
            short[] pixels5x4 = Enumerable.Repeat((short)100, 20).ToArray();

            WriteFits16(Path.Combine(_tempDirectory, "2026-01-01_00-00-01_R_0001.fits"), 4, 4, "R", pixels4x4);
            WriteFits16(Path.Combine(_tempDirectory, "2026-01-01_00-00-02_R_0002.fits"), 5, 4, "R", pixels5x4);

            var service = new TemporalSnrService();

            var options = new TemporalSnrOptions
            {
                RegisterFrames = false,
                StopOnMismatch = true
            };

            Assert.Throws<InvalidOperationException>(() => service.ComputeDirectory(_tempDirectory, options));
        }

        private static void WriteFits16(string filePath, int width, int height, string filter, short[] pixels)
        {
            if (pixels.Length != width * height)
                throw new ArgumentException("Pixel array size does not match dimensions", nameof(pixels));

            var cards = new List<string>
            {
                BuildCard("SIMPLE", "T"),
                BuildCard("BITPIX", "16"),
                BuildCard("NAXIS", "2"),
                BuildCard("NAXIS1", width.ToString()),
                BuildCard("NAXIS2", height.ToString()),
                BuildCard("FILTER", $"'{filter}'"),
                BuildCard("END", string.Empty, false)
            };

            byte[] headerBytes = BuildHeader(cards);
            byte[] dataBytes = new byte[pixels.Length * 2];
            for (int i = 0; i < pixels.Length; i++)
            {
                short value = pixels[i];
                dataBytes[i * 2] = (byte)((value >> 8) & 0xFF);
                dataBytes[i * 2 + 1] = (byte)(value & 0xFF);
            }

            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(dataBytes, 0, dataBytes.Length);
        }

        private static string BuildCard(string keyword, string value, bool includeEquals = true)
        {
            string card;
            if (includeEquals)
            {
                card = $"{keyword.PadRight(8)}= {value.PadLeft(20)}";
            }
            else
            {
                card = keyword;
            }

            return card.PadRight(80);
        }

        private static byte[] BuildHeader(List<string> cards)
        {
            var header = string.Concat(cards);
            int blockSize = 2880;
            int paddedLength = ((header.Length + blockSize - 1) / blockSize) * blockSize;
            header = header.PadRight(paddedLength, ' ');
            return Encoding.ASCII.GetBytes(header);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, true);
                }
            }
            catch
            {
                // No-op cleanup fallback for test temp files.
            }
        }
    }
}
