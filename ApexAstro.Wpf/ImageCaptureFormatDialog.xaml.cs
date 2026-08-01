using System.Windows;

namespace ApexAstro.Wpf
{
    public partial class ImageCaptureFormatDialog : Window
    {
        public SettingsResetPreset? SelectedPreset { get; private set; }

        public ImageCaptureFormatDialog()
        {
            InitializeComponent();
        }

        private void Nina_Click(object sender, RoutedEventArgs e)
        {
            SelectedPreset = SettingsResetPreset.Nina;
            DialogResult = true;
            Close();
        }

        private void Asiair_Click(object sender, RoutedEventArgs e)
        {
            SelectedPreset = SettingsResetPreset.Asiair;
            DialogResult = true;
            Close();
        }

        private void ChooseLater_Click(object sender, RoutedEventArgs e)
        {
            SelectedPreset = null;
            DialogResult = true;
            Close();
        }
    }
}
