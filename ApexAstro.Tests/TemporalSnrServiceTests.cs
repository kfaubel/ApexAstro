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
        public void ComputeDirectory_GroupsByFilterWithGeminiTemporalSnr()
        {
            int width = 8;
            int height = 8;
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
            Assert.True(rResult.RejectedSampleCountMap.All(v => v == 0));
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

        [Fact]
        public void ComputeDirectory_WithSamplePixelCount_ComputesOnlySampledPoints()
        {
            int width = 20;
            int height = 20;
            int pixelCount = width * height;

            short[] frame1 = Enumerable.Repeat((short)100, pixelCount).ToArray();
            short[] frame2 = Enumerable.Repeat((short)110, pixelCount).ToArray();
            short[] frame3 = Enumerable.Repeat((short)120, pixelCount).ToArray();

            WriteFits16(Path.Combine(_tempDirectory, "2026-01-01_00-00-01_L_0001.fits"), width, height, "L", frame1);
            WriteFits16(Path.Combine(_tempDirectory, "2026-01-01_00-00-02_L_0002.fits"), width, height, "L", frame2);
            WriteFits16(Path.Combine(_tempDirectory, "2026-01-01_00-00-03_L_0003.fits"), width, height, "L", frame3);

            var service = new TemporalSnrService();
            var options = new TemporalSnrOptions
            {
                RegisterFrames = false,
                MinSamples = 2,
                SamplePixelCount = 50,
                Profile = TemporalSnrExecutionProfile.Portable
            };

            TemporalSnrComputationResult result = service.ComputeDirectory(_tempDirectory, options);
            TemporalSnrFilterResult lResult = result.FilterResults["L"];

            int computedPointCount = lResult.TemporalSnrMap.Count(float.IsFinite);
            int unsampledPointCount = lResult.RejectedSampleCountMap.Count(value => value < 0);

            Assert.Equal(50, computedPointCount);
            Assert.Equal(pixelCount - 50, unsampledPointCount);
        }

        [Fact]
        public void ComputeDirectory_WithBzeroOffset_ComputesFiniteGeminiTemporalSnr()
        {
            int width = 4;
            int height = 4;
            int pixelCount = width * height;

            // Mostly flat background near BZERO with one varying target pixel. The result should
            // retain non-zero target SNR without inflating the whole frame from offsets.
            int[] frame1 = Enumerable.Repeat(32768, pixelCount).ToArray();
            int[] frame2 = Enumerable.Repeat(32768, pixelCount).ToArray();
            int[] frame3 = Enumerable.Repeat(32768, pixelCount).ToArray();
            for (int i = 0; i < 10; i++)
            {
                frame1[i] = 32778;
                frame2[i] = 32779;
                frame3[i] = 32777;
            }

            WriteFits16(Path.Combine(_tempDirectory, "2026-01-01_00-00-01_L_0001.fits"), width, height, "L", frame1, bzero: 32768);
            WriteFits16(Path.Combine(_tempDirectory, "2026-01-01_00-00-02_L_0002.fits"), width, height, "L", frame2, bzero: 32768);
            WriteFits16(Path.Combine(_tempDirectory, "2026-01-01_00-00-03_L_0003.fits"), width, height, "L", frame3, bzero: 32768);

            var service = new TemporalSnrService();
            var options = new TemporalSnrOptions
            {
                RegisterFrames = false,
                MinSamples = 2,
                SamplePixelCount = 0,
                Profile = TemporalSnrExecutionProfile.Portable
            };

            TemporalSnrComputationResult result = service.ComputeDirectory(_tempDirectory, options);
            TemporalSnrFilterResult lResult = result.FilterResults["L"];

            Assert.True(double.IsFinite(lResult.MedianTemporalSnr));
            Assert.True(lResult.MedianTemporalSnr > 0.0);
        }

        private static void WriteFits16(string filePath, int width, int height, string filter, short[] pixels, int bzero = 0)
        {
            WriteFits16(filePath, width, height, filter, pixels.Select(value => (int)value).ToArray(), bzero);
        }

        private static void WriteFits16(string filePath, int width, int height, string filter, int[] pixels, int bzero = 0)
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

            if (bzero != 0)
            {
                cards.Insert(5, BuildCard("BZERO", bzero.ToString()));
                cards.Insert(6, BuildCard("BSCALE", "1"));
            }

            byte[] headerBytes = BuildHeader(cards);
            byte[] dataBytes = new byte[pixels.Length * 2];
            for (int i = 0; i < pixels.Length; i++)
            {
                int rawValue = pixels[i] - bzero;
                if (rawValue < short.MinValue || rawValue > short.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(pixels), "Raw FITS sample value is out of Int16 range after BZERO adjustment.");

                short value = (short)rawValue;
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
