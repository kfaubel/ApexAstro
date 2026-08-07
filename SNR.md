# ApexAstro SNR File Format

This document describes the structure of the per-folder SNR cache file:

- `.apexastro.snr.json`

Use this file when you want to read SNR results and source-file validation metadata from another program.

## Purpose

`.apexastro.snr.json` stores:

- When SNR was computed
- Which folder the results belong to
- Per-filter SNR statistics
- Per-filter source file metadata used to validate cache freshness

## Top-level JSON structure

```json
{
  "schemaVersion": 2,
  "computedUtc": "2026-08-07T02:31:55.1234567Z",
  "folderPath": "C:\\Data\\M31\\LIGHT",
  "filters": [
    {
      "filterName": "L",
      "width": 6248,
      "height": 4176,
      "frameCount": 42,
      "integrationMinutes": 42.0,
      "referenceFile": "C:\\Data\\M31\\LIGHT\\2026-03-27_06-38-15_L_..._0755.fits",
      "meanTemporalSnr": 38.42,
      "medianTemporalSnr": 37.95,
      "standardDeviationTemporalSnr": 4.73,
      "sourceFiles": [
        {
          "fileName": "2026-03-27_06-38-15_L_..._0755.fits",
          "lastWriteUtc": "2026-03-27T13:38:16.0000000Z",
          "fileSizeBytes": 103234560,
          "width": 6248,
          "height": 4176,
          "bitpix": 16,
          "hfr": 1.9
        }
      ]
    }
  ]
}
```

## Field reference

### Root object

- `schemaVersion` (integer)
  - Current value is `2`.
- `computedUtc` (string, ISO-8601 UTC timestamp)
  - Time when SNR was computed.
- `folderPath` (string)
  - Absolute folder path this cache applies to.
- `filters` (array of filter objects)
  - One entry per filter (for example `L`, `R`, `G`, `B`, `HA`).

### Filter object (`filters[]`)

- `filterName` (string)
  - Filter identifier.
- `width` (integer)
  - Frame width in pixels.
- `height` (integer)
  - Frame height in pixels.
- `frameCount` (integer)
  - Number of frames included for this filter.
- `integrationMinutes` (number or null)
  - Total integration time in minutes for this filter.
  - Nullable when exposure could not be determined.
- `referenceFile` (string)
  - Full path of the frame used as the registration reference.
- `meanTemporalSnr` (number)
  - Mean SNR value across sampled valid pixels.
- `medianTemporalSnr` (number)
  - Median SNR value across sampled valid pixels.
- `standardDeviationTemporalSnr` (number)
  - Standard deviation of SNR across sampled valid pixels.
- `sourceFiles` (array of source-file objects)
  - Metadata used to validate whether the cache is still current.

### Source file object (`filters[].sourceFiles[]`)

- `fileName` (string)
  - File name only (no directory).
- `lastWriteUtc` (string, ISO-8601 UTC timestamp)
  - File timestamp at compute time.
- `fileSizeBytes` (integer)
  - File size at compute time.
- `width` (integer)
  - Image width from file metadata.
- `height` (integer)
  - Image height from file metadata.
- `bitpix` (integer)
  - FITS BITPIX value.
- `hfr` (number or null)
  - Parsed HFR value when available.

## How ApexAstro uses this file

ApexAstro considers SNR cache data valid only when the current image files match the cached `sourceFiles` set and metadata:

- Same file count
- Same file names
- Same file sizes
- Same last-write timestamps (within small tolerance)

If validation fails, the cache is treated as out-of-date and SNR should be recomputed.

## Compatibility notes

- Current filename: `.apexastro.snr.json`
- Legacy filename still seen in older data: `.apexastro.temporal-snr.json`
- `schemaVersion` should be checked before parsing.

## Related file

ApexAstro also writes a summary file intended for project-level reporting:

- `.apexastro.snr-summary.json`

That file contains filter summary rows and per-frame SNR ranking data.
