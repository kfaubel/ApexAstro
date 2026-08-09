# ApexAstro Documentation

Let's take a look at the ApexAstro application and how to use it, including accessing additional metadata.  

## Features

- **View FITS and XISF astronomical images** - Includes display of metadata for these file formats
- **View other common image formats** - JPG, PNG, BMP, GIF, WEBP and TIFF
- **Zoom and pan controls** - Navigate through your images with mouse wheel zoom and drag-to-pan functionality
- **File metadata display** - View filename metadata as well as info in FITS/XISF headers
- **Graph display of metadata** - Visualize metadata values for all images in the file list using graphs
- **Select and move images** - Select images and then move them to a processing folder or the recycle bin
- **Image statistics and analysis** - Examine pixel statistics and image properties including median values
- **Histogram display** - Optional histogram panel with linear and logarithmic scales (can be hidden via Options)
- **Floating windows** - Detach image and histogram viewers for multi-monitor setups
- **Auto-stretch control** - Adjustable stretch factor for better image visibility
- **Dark mode** - Switch between light and dark themes for comfortable viewing

## File List Columns
### FITS Keywords
Any metadata in the FITS header can be extracted and displayed in the file list.  See Options -> Fits Keywords... .

### Custom Keywords
Any metadata value that can be encoded in the image filename can be extracted and displayed in the file list.  See Options -> Custom Keywords... .

### Session Metadata
If you use the Session Metadata plugin in N.I.N.A., additional metadata that is not stored in the FITS header or filename can also be extracted and displayed as custom keywords in ApexAstro. This allows for a more comprehensive view of your imaging session's data directly within the application.

Columns can be dragged to change their order in the file list.  You can also click on a column header to sort the images based on that column's values.

# Configure the File Pattern in N.I.N.A.
In N.I.N.A. go to Options → Imaging and paste the following pattern into the Image file pattern field.

```
$$SEQUENCETITLE$$\NIGHT_$$DATEMINUS12$$\$$IMAGETYPE$$\$$DATETIME$$_$$FILTER$$_RMS_$$RMSARCSEC$$_HFR_$$HFR$$_ECC_$$ECCENTRICITY$$_FWHM_$$FWHM$$_Stars_$$STARCOUNT$$_$$GAIN$$_$$EXPOSURETIME$$s_$$SENSORTEMP$$C_$$FRAMENR$$
```
The filenames will then look like this:
```
...\M33\NIGHT_2015-12-31\LIGHT\2016-01-01_12-00-00_L_RMS_0.65_HFR_3.25_ECC_0.66_FWHM_4.23_Stars_3294_1600_10.21s_-15C_0001.fits
```
The directory part of the pattern is optional and can be adjusted for your needs (the "NIGHT" in the folder name helps PixInsight handle data from multiple nights).

The leading "$$DATETIME$$", the "RMS", "HFR", "ECC", "Stars" labels and values ($$RMSARCSEC", $$HFR$$, ...) are parsed and displayed in sortable columns in the file list.  

In Options → Custom Keywords you can add columns for these values.  The column names must match the labels in the filename exactly (case-sensitive).  For example, to add a column for RMS, click Add and enter "RMS" as the name.  Repeat for HFR, Stars, and Eccentricity.

Once additional (N.I.N.A.) metadata is configured your view will look something like this:
<img alt="ApexAstro" src="ApexAstro with full metadata.png" />

### RMS
A good RMS value is typically below 1.0 for well-guided and well-focused images.  Or, more precicely, less than the Image Scale (arcseconds/pixel) of the imaging setup.  The Image scale is calculated as: (206.3 * pixel size in microns) / (focal length in mm).  For example, with a pixel size of 3.76 microns and focal length of 800mm, the image scale is about 0.97 arcseconds/pixel.  So an RMS below 0.97 would be considered good.

### HFR
HFR (Half Flux Radius) indicates the radius (in pixels) that contains half of a star's total flux.  Lower HFR values indicate sharper stars.  Typical good values are between 1.5 and 2.0 pixels, depending on seeing conditions and focal length.

### Stars
The star count indicates the number of stars detected in the image.  Higher star counts generally indicate better focus and sky conditions, but this can vary based on the field of view and exposure settings.  If the star count is very low (e.g., below 50), it may indicate poor focus, clouds or light pollution.  Sorting on the Stars column can help cull out poorly focused or cloudy images.

### Eccentricity
Eccentricity measures how elongated the stars appear in the image.  An eccentricity of 0 indicates perfectly round stars, while values closer to 1 indicate more elongated stars.  Values above 0.6 typically indicate poor tracking or focus.  Lower eccentricity values are generally better, indicating well-tracked and well-focused images.

### FITS/XISF Keywords
Specific FITS/XISF header keywords can be added as columns in the file list for sorting. Use the Options menu to add desired keywords.

Examples of useful keywords include: GAIN, EXPTIME, FILTER, DATE-OBS, OBJECT, TELESCOP, INSTRUME, etc.

### Median and Mean Display
If enabled, the median pixel value is automatically calculated when a folder is loaded. You can display it in two formats via the Options menu:
- **Normalized (0.0-1.0)**: Shows median as a decimal value with 5 decimal places (e.g., 0.03604)
- **16-bit range (0-65535)**: Shows median as an integer value (e.g., 2362)

Note: enabling this option slows down the loading of folders, especially with large numbers of images, as the median and mean values need to be calculated for each image.  They are cached to make opening the same folder faster in the future.

## Auto Select Dialog
The Auto Select Dialog allows you to automatically select images based on specific criteria such as RMS, HFR, star count, and eccentricity. You can set thresholds for each parameter, and images that meet the criteria will be highlighted or selected in the file list.  This is most useful with the N.I.N.A. filename patterns configured, which encodes values like RMS, HFR and Eccentricity.

## Graph Display
Clicking the graph icon in the toolbar will display a plot of the metadata values for all of the images in the current folder. This allows for quick visual analysis of trends and outliers in the dataset.

## SNR Display
The SNR (Signal-to-Noise Ratio) tools estimate data quality for each filter in the current folder.

### Overall SNR (per filter)
When you run SNR analysis, ApexAstro computes an SNR value for each filter from the temporal behavior of pixel values across the frame set. The results are cached in a JSON file named `.apexastro.snr.json` in the folder being analyzed, which allows quick reloading without recomputation.

The SNR is an approximation of target signal quality for each filter and is intended to be comparable to common planning thresholds (about 30 is acceptable, 60 is good, and 90 is excellent).  The `SNR.md` file (on GitHub) contains more details on SNR computation, cache structure, and multi-night aggregation guidance for external tools.

The SNR calculation takes a few minutes depending on how many frames are in the folder and the PC being used.  RAM is the most important factor impacting performance.

### Processing Steps
- Load and validate the frames - ensure they are all the same size and bit depth.
- Choose a reference frame for alignment (Lowest HFR or the first frame is used by default)
- Align all frames to the reference frame using OpenCV's ECC algorithm.
- Select sample pixels for SNR computation (10,000 pixels per filter by default)
- Compute the pixel-wise temporal SNR map for each filter from frame-to-frame statistics.
- Split SNR values into target and background using Otsu thresholding (with percentile fallback if needed).
- Compute per-filter summary values (mean, median, target/background medians) and cache the results.




## Histogram Panel
A histogram panel can be shown below the image viewer via the Options menu. The histogram can be displayed in linear or logarithmic scale and automatically updates when you select different images. The histogram panel can be hidden to maximize the image viewing area.

## Floating Windows
For users with multiple monitors, click the float button at the top-right of the image viewer to detach the image and histogram into separate windows. These can be dragged to another monitor and shown full screen, allowing you to see the full file list and images simultaneously. Click the re-dock button in the toolbar to return windows to the main interface.

## Support
For technical support or feature requests, please refer to the Github repository: [ApexAstro on Github](https://github.com/kfaubel/ApexAstro/issues).

Software updates are published here: [ApexAstro Releases](https://github.com/kfaubel/ApexAstro/releases)