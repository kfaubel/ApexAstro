
# Data Structure & Grouping

To prevent small sample sizes from breaking your math, your code should ingest all 1,000+ files and group them into a nested dictionary or object structured by Filter first, then Night.
Dataset -> Filter (e.g., Red) -> Night 1 (e.g., 4 subs)
                              -> Night 2 (e.g., 5 subs)
                              -> Night 3 (e.g., 4 subs)

# The Multi-Pass Grading Algorithm

Because you have a massive total dataset, you can leverage a two-pass statistical approach to calculate thresholds accurately.

Pass 1: Global Filter Baseline (Solving the Sample Size Problem)

Instead of looking at Night 1's 4 Red subs alone, pool all Red subs across all nights into a single statistical array.
- Calculate the Global Median HFR and Global MAD (Median Absolute Deviation) for that filter across the entire 1,000-frame library. Note: MAD is much more robust against extreme outliers than Standard Deviation.
- The Rule: Any frame where HFR > Global Median + 3 * Global MAD is a catastrophic failure (e.g., cloud cover, a tree branch, or severe dew).
- Action: Flag these frames for immediate deletion. This cleans your dataset so the next pass uses pure data.

Pass 2: Night-by-Night Relative Grading

Now that the catastrophic failures are gone, you need to handle the "Good Night vs. Bad Night" (HFR 2.0 vs 3.5) dilemma. Your code should calculate a relative score for how a sub-frame performed against its own night.
- For each specific night and filter combination, calculate the local median.
- For each sub-frame, calculate a Relative Deviation Score:
\(\text{Relative\ Score}=\frac{\text{Frame\ HFR}}{\text{Local\ Nightly\ Median\ HFR}}\)
- The Rule: A frame with a score of 1.0 is perfectly average for that night. A frame with a score of 1.15 is 15% worse than its night's average.
- Action: Cull frames where the Relative Score is > 1.15 or 1.20.

# How to Output the Results for WBPP

Since you want WBPP to do the heavy lifting for the nuanced weighting (preserving the background SNR of the 3.5 HFR night), your program's job is simply to separate the fatal flaws from the usable data.
Your program should output three things:
- The Hard Rejects: Permanently delete or move frames with tracking failures (eccentricity > 0.45 or 0.5) and severe clouds (Pass 1 failures).
- The Local Outliers: Delete or move frames that were uniquely bad for their specific night (Pass 2 failures, e.g., a localized tracking glitch or a single passing cloud).
- The Cleaned Dataset: Pass the remaining frames to WBPP. Even if Night 2 has an average HFR of 3.5, as long as those frames are consistent with each other, they pass your pre-sorter. Once inside WBPP, PixInsight’s PSFSignal weighting will automatically down-weight Night 2 to ~20% influence on sharpness, while fully utilizing its photons to squash background noise.

# New SNR
Since you are writing this tool yourself in C#, you have full control over the pixel arrays or metadata calculations. Knowing your Mean and Median opens up some powerful options.
Here are the precise formulas and C# architectures to calculate a robust SNRWeight proxy, alongside a critical fix to prevent the "light pollution trap" mentioned earlier.
------------------------------
## 1. The Core C# SNRWeight Formula
If you only have global metadata variables (Mean, Median, and a baseline Noise), you can use the Median Absolute Deviation (MAD) as your signal proxy, which is much more resilient against bright stars than standard deviation.

// 1. Calculate Average Deviation Proxy from Mean and Median// (Robust statistical scale factor for normal distributions)double avgDev = Math.Abs(mean - median) * 1.253314; 
// 2. Compute the relative weight// Note: noiseFloor must come from a constant background evaluation (see section 2)double snrWeight = Math.Pow(avgDev / noiseFloor, 2);

## 2. Solving the "NoiseFloor" Problem in C#
You cannot use the global RMS of the whole frame as the noiseFloor because bright stars will falsely spike the noise value.
To make this formula accurate in code, you need a way to measure the background sky noise natively. In C#, if you are parsing the image pixels (e.g., using ImageSharp or a raw FITS array wrapper), you can implement a fast Iterative K-Sigma Clipping routine on a downsampled patch or the whole frame to isolate the background noise floor:

```csharp
public double CalculateBackgroundNoise(double[] pixels, double clippingThreshold = 3.0, int maxIterations = 5)
{
    // Start with global stats
    double currentMean = pixels.Average();
    double currentStdDev = Math.Sqrt(pixels.Average(v => Math.Pow(v - currentMean, 2)));
    
    for (int i = 0; i < maxIterations; i++)
    {
        // Filter out pixels that are too bright (stars) or too dark (hot/cold pixels)
        var clippedPixels = pixels.Where(p => Math.Abs(p - currentMean) <= clippingThreshold * currentStdDev).ToArray();
        
        if (clippedPixels.Length == 0) break;
        
        double newMean = clippedPixels.Average();
        double newStdDev = Math.Sqrt(clippedPixels.Average(v => Math.Pow(v - newMean, 2)));
        
        // If the standard deviation stabilizes, we found the background noise
        if (Math.Abs(currentStdDev - newStdDev) < 0.0001)
        {
            currentStdDev = newStdDev;
            break;
        }
        
        currentMean = newMean;
        currentStdDev = newStdDev;
    }
    
    // This StdDev represents the RMS of your background sky noise floor
    return currentStdDev; 
}
```

## 3. Preventing the Light Pollution / Moon Trap
If a frame is hit by moonlight, the background sky brightens, forcing both mean and avgDev up. Your math will trick the script into thinking the frame has a high SNR.
To fix this in your code, you must penalize the weight based on the background brightness (using the Median). PixInsight’s SubframeSelector fixes this by subtracting the pedestal. You can implement a normalized correction factor directly in your C# algorithm:

// Calculate a penalty factor using a known "clean background night" reference mediandouble backgroundPenalty = referenceMedian / currentMedian; 
// Apply the penalty to ensure washed-out frames get heavily de-weighteddouble correctedSNRWeight = Math.Pow(avgDev / noiseFloor, 2) * backgroundPenalty;

## 4. The Ideal Custom Formula for C# Culling
Because your C# application already tracks Star Count, HFR, Eccentricity, and the pixel stats, you don't have to rely purely on SNRWeight. You can combine them into a single Quality Score property on your image class:

```csharp
public class AstrophotoFrame
{
    public double Hfr { get; set; }
    public double Eccentricity { get; set; }
    public int StarCount { get; set; }
    public double Mean { get; set; }
    public double Median { get; set; }
    public double BackgroundNoise { get; set; }

    public double GetQualityScore(double referenceMedian)
    {
        // 1. Structural Quality (Smaller HFR and lower Eccentricity is best)
        // Eccentricity is penalized exponentially if it creeps past 0.42
        double structuralMetric = StarCount / (Hfr * Math.Max(0.1, Eccentricity));

        // 2. Signal Quality
        double avgDev = Math.Abs(Mean - Median) * 1.2533;
        double backgroundPenalty = referenceMedian / Median;
        double snrWeight = Math.Pow(avgDev / BackgroundNoise, 2) * backgroundPenalty;

        // Combined score
        return structuralMetric * snrWeight;
    }
}
```

By sorting a List<AstrophotoFrame> by GetQualityScore() descending, the worst frames from all nights will automatically sink to the bottom of your collection for easy automated deletion or exclusion.

