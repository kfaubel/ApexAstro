using System;

namespace ApexAstro.Wpf.Services
{
    public interface IGeneralOptionsDialogService
    {
        /// <summary>
        /// Shows the Options dialog and returns the new settings if changed, or null if cancelled.
        /// Also handles FITS Keywords and Custom Keywords sub-dialogs.
        /// </summary>
        /// <param name="currentShowSizeColumn">The current ShowSizeColumn value.</param>
        /// <param name="currentShowMedianColumn">The current ShowMedianColumn value.</param>
        /// <param name="currentTheme">The current theme mode.</param>
        /// <param name="currentShowFullScreenHelp">The current ShowFullScreenHelp value.</param>
        /// <param name="currentPlayPauseInterval">The current play pause interval in seconds.</param>
        /// <param name="currentScanXisfForFitsKeywords">The current ScanXisfForFitsKeywords value.</param>
        /// <param name="currentMedianDisplayMode">The current median display mode.</param>
        /// <param name="currentShowHistogram">The current ShowHistogram value.</param>
        /// <param name="currentFitsKeywords">The current FITS keywords list.</param>
        /// <param name="currentCustomKeywords">The current custom keywords list.</param>
        /// <returns>A tuple with the new settings, or null values if cancelled.</returns>
        (bool? showSizeColumn, bool? showFullFilename, bool? showFilenameDateColumn, bool? showFilenameTimeColumn, bool? ShowFilenameFrameColumn, bool? showMedianColumn, ThemeMode? theme, bool? showFullScreenHelp, double? playPauseInterval, bool? scanXisfForFitsKeywords, MedianDisplayMode? medianDisplayMode, bool? showHistogram, List<string>? fitsKeywords, List<string>? customKeywords, List<string>? csvKeywords) ShowGeneralOptionsDialog(
            bool currentShowSizeColumn,
            bool currentShowFullFilename,
            bool currentShowFilenameDateColumn,
            bool currentShowFilenameTimeColumn,
            bool currentShowFilenameFrameColumn,
            bool currentShowMedianColumn,
            ThemeMode currentTheme, 
            bool currentShowFullScreenHelp,
            double currentPlayPauseInterval,
            bool currentScanXisfForFitsKeywords,
            MedianDisplayMode currentMedianDisplayMode,
            bool currentShowHistogram,
            IEnumerable<string> currentFitsKeywords,
            IEnumerable<string> currentCustomKeywords,
            IEnumerable<string> currentCsvKeywords);
    }
}
