# ApexAstro SNR JSON Format

This document describes the SNR cache files written by ApexAstro and how to combine multiple nights of SNR into a single per-filter progress value.

## Files

ApexAstro writes two JSON files in each light-frame folder:

- `.apexastro.snr.json` (detailed cache + source validation)
- `.apexastro.snr-summary.json` (project/report summary)

## Method Summary (Current Code)

ApexAstro currently computes temporal SNR using a Gemini-style approach:

1. Group frames by filter.
2. Optionally align frames to a reference frame (ECC registration).
3. Compute per-pixel temporal SNR from aligned frames (no MAD rejection in this main pass).
4. Split target/background using reference-image segmentation (Otsu with fallback).
5. Use Gemini signal SNR directly for planning/display values.

SignalSNR is `targetMedianTemporalSnr` when available, otherwise `meanTemporalSnr`.

For compatibility with existing JSON/UI wiring, this value is currently stored in `stackEquivalentSnr` and shown in the SNR column.

## `.apexastro.snr.json` Structure

### Root object

- `schemaVersion` (integer)
  - Current value: `2`
- `computedUtc` (string, ISO-8601 UTC)
- `folderPath` (string)
- `filters` (array of filter objects)

### Filter object (`filters[]`)

- `filterName` (string)
- `width` (integer)
- `height` (integer)
- `frameCount` (integer)
- `integrationMinutes` (number or null)
- `referenceFile` (string)
- `meanTemporalSnr` (number)
- `medianTemporalSnr` (number)
- `stackEquivalentSnr` (number or null)
  - Gemini signal SNR used by planning/progress UI (compatibility field name)
- `targetMedianTemporalSnr` (number or null)
- `backgroundMedianTemporalSnr` (number or null)
- `standardDeviationTemporalSnr` (number)
- `sourceFiles` (array of source-file objects)

### Source file object (`filters[].sourceFiles[]`)

- `fileName` (string)
- `lastWriteUtc` (string, ISO-8601 UTC)
- `fileSizeBytes` (integer)
- `width` (integer)
- `height` (integer)
- `bitpix` (integer)
- `hfr` (number or null)

## `.apexastro.snr-summary.json` Structure

### Root object

- `schemaVersion` (integer)
  - Current value: `2`
- `computedUtc` (string, ISO-8601 UTC)
- `folderPath` (string)
- `filterSummaries` (array)
- `frameSummaries` (array)

### Filter summary object (`filterSummaries[]`)

- `filterName` (string)
- `frameCount` (integer)
- `integrationMinutes` (number or null)
- `medianTemporalSnr` (number)
- `stackEquivalentSnr` (number or null)
- `targetMedianTemporalSnr` (number or null)
- `backgroundMedianTemporalSnr` (number or null)
- `meanTemporalSnr` (number)
- `standardDeviationTemporalSnr` (number)
- `coefficientOfVariation` (number)

### Frame summary object (`frameSummaries[]`)

- `fileName` (string)
- `filterName` (string)
- `frameSnr` (number)
- `grade` (string)
- `keep` (boolean)
- `rank` (integer)
- `frameCountInFilter` (integer)

## Cache Validation Rules

ApexAstro treats `.apexastro.snr.json` as valid only when current files match cached `sourceFiles`:

- Same total file count
- Same file names
- Same file sizes
- Same last-write timestamps (small tolerance)

If validation fails, SNR is recomputed.

## Compatibility

- Current cache filename: `.apexastro.snr.json`
- Legacy cache filename: `.apexastro.temporal-snr.json`
- Current summary filename: `.apexastro.snr-summary.json`
- Legacy summary fallback path may use `.apexastro.snr.json` in older layouts

---

## Tool Section: Multi-Night Aggregate SNR

This section is intended for external automation that combines many nightly JSON files (for example 10 nights) to estimate progress toward SNR goals.

This aggregate is a planning estimate designed to answer: "How close am I to my capture target for each filter?"

### Inputs

- One or more nightly `.apexastro.snr.json` files
- Optional goal SNR values, typically: 30, 60, 90

### Per-night per-filter signal selection

For each folder/filter row, compute `nightSignalSnr` as:

1. If `targetMedianTemporalSnr` is finite and > 0, use it.
2. Else if `meanTemporalSnr` is finite and > 0, use it.
3. Else skip this night/filter row.

Then compute `nightEffectiveSnr` as:

1. If `stackEquivalentSnr` is finite and > 0, use it directly.
2. Else use `nightSignalSnr`.

### Aggregate across nights (per filter)

For each filter name (L, R, G, B, HA, etc.), combine independent folder contributions in quadrature:

$$
	ext{aggregateSnr} = \sqrt{\sum_{i=1}^{k} \left(\text{nightEffectiveSnr}_i\right)^2}
$$

Also compute total integration:

$$
\text{aggregateIntegrationMinutes} = \sum_{i=1}^{k} \text{integrationMinutes}_i
$$

(use only finite, positive integration values)

### Progress to goal

For each goal SNR $G$:

$$
	ext{progressRatio} = \frac{\text{aggregateSnr}}{G}
$$

$$
\text{progressPercent} = 100 \cdot \text{progressRatio}
$$

Clamp display percent to [0, 100+] depending on UI preference.

### Additional integration estimate to goal

If current aggregate SNR and integration are both finite and positive:

$$
	ext{requiredMinutesAtGoal} = \text{aggregateIntegrationMinutes} \cdot \left(\frac{G}{\text{aggregateSnr}}\right)^2
$$

$$
\text{additionalMinutes} = \max\left(0,\ \text{requiredMinutesAtGoal} - \text{aggregateIntegrationMinutes}\right)
$$

If aggregate SNR is missing or non-positive, return `additionalMinutes = null`.

### Recommended output object (per filter)

```json
{
  "filterName": "L",
  "nightCountUsed": 8,
  "aggregateSnr": 74.36,
  "aggregateIntegrationMinutes": 512.0,
  "goals": {
    "30": { "progressPercent": 100.0, "additionalMinutes": 0.0 },
    "60": { "progressPercent": 100.0, "additionalMinutes": 0.0 },
    "90": { "progressPercent": 82.62, "additionalMinutes": 238.7 }
  }
}
```

### Notes for robust tooling

- Match filters case-insensitively and normalize names (`Ha` -> `HA`).
- Reject rows where `frameCount <= 0`.
- Ignore NaN/Infinity values.
- Prefer reading from `.apexastro.snr.json` for per-night combine logic.
- Use `.apexastro.snr-summary.json` for reporting and frame ranking workflows.
- When the same folder is processed multiple times, keep only the newest entry by `computedUtc`.
