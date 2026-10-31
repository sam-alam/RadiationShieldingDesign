using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace RadiationShieldingDesign
{
    public partial class ExportReportWindow : Window
    {
        public ExportReportWindow()
        {
            InitializeComponent();
        }

        private MainViewModel VM => (MainViewModel)DataContext;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void LoadSignature_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "PNG Image (*.png)|*.png",
                Title = "Select signature image (PNG)"
            };
            if (dlg.ShowDialog(this) != true) return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(dlg.FileName);
            bmp.EndInit();
            bmp.Freeze();

            VM.SignatureImage = bmp;
        }

        private void GenerateWord_Click(object sender, RoutedEventArgs e)
        {
            var roomType = VM?.SelectedRoomCategory ?? "—";
            var roomName = VM?.RoomName ?? "";
            var datePart = DateTime.Now.ToString("MM-dd-yy");
            var suggested = string.IsNullOrWhiteSpace(roomName)
                ? $"ShieldingReport_{datePart}.docx"
                : $"ShieldingReport_{roomType}_{roomName}_{datePart}.docx";

            var dlg = new SaveFileDialog
            {
                Title = "Save Word report",
                Filter = "Word Document (*.docx)|*.docx",
                FileName = suggested
            };
            if (dlg.ShowDialog(this) != true) return;

            // Call MainWindow export method
            if (Owner is MainWindow mw)
            {
                mw.GenerateWordReport(dlg.FileName);
                MessageBox.Show("Report saved.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
