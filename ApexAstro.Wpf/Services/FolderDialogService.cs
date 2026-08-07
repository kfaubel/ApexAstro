using System;

namespace ApexAstro.Wpf.Services
{
    /// <summary>
    /// Service class that handles showing folder selection dialogs to the user.
    /// Implements IFolderDialogService interface for dependency injection and testability.
    /// This separates UI concerns from business logic - the ViewModel doesn't need to know
    /// about specific dialog implementations.
    /// 
    /// Uses OpenFolderDialog so the user sees folders only (no file list).
    /// </summary>
    public class FolderDialogService : IFolderDialogService
    {
        /// <summary>
        /// Shows a folder browser dialog and returns the selected folder path.
        /// Uses OpenFolderDialog so only folders are shown.
        /// </summary>
        /// <param name="initialDirectory">The directory to start browsing from</param>
        /// <returns>The selected folder path, or null if user cancelled</returns>
        public string? ShowFolderDialog(string initialDirectory)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Select Folder",
                    InitialDirectory = initialDirectory,
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName) && System.IO.Directory.Exists(dialog.FolderName))
                    return dialog.FolderName;

                return null;
            }
            catch
            {
                // Fallback to the legacy folder browser dialog if OpenFileDialog fails
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.SelectedPath = initialDirectory;
                    dlg.ShowNewFolderButton = true;
                    
                    var result = dlg.ShowDialog();
                    
                    if (result == System.Windows.Forms.DialogResult.OK || result == System.Windows.Forms.DialogResult.Yes)
                    {
                        return dlg.SelectedPath;
                    }
                    
                    return null;
                }
            }
        }
    }
}
