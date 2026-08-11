using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace SwiftControl
{
    internal sealed class ModeOsdWindow : Window
    {
        private const int GwlExStyle = -20;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;
        private static readonly ImageSource AcerArtwork = LoadAcerArtwork();
        private static ModeOsdWindow _current;

        private ModeOsdWindow(string modeName)
        {
            // Acer's native card is 178 x 128 logical pixels. The embedded
            // source is its matching 534 x 384 (300% DPI) variant.
            Width = 178;
            Height = 128;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Opacity = 0;

            Grid grid = new Grid();
            Image artwork = new Image();
            artwork.Source = AcerArtwork;
            artwork.Stretch = Stretch.Fill;
            grid.Children.Add(artwork);

            TextBlock label = new TextBlock();
            label.Text = modeName;
            label.FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
            label.FontSize = 11;
            label.FontWeight = FontWeights.SemiBold;
            label.Foreground = Brushes.White;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Bottom;
            label.Margin = new Thickness(8, 0, 8, 9);
            grid.Children.Add(label);

            Content = grid;
            SourceInitialized += MakeNonActivating;
            Closed += delegate
            {
                if (ReferenceEquals(_current, this)) _current = null;
            };
        }

        public static void Present(string modeName)
        {
            if (_current != null)
            {
                _current.Close();
            }

            ModeOsdWindow osd = new ModeOsdWindow(modeName);
            _current = osd;
            Rect area = SystemParameters.WorkArea;
            osd.Left = area.Right - osd.Width - 23;
            osd.Top = area.Top + 14;
            osd.Show();

            DoubleAnimationUsingKeyFrames visibility = new DoubleAnimationUsingKeyFrames();
            visibility.KeyFrames.Add(new LinearDoubleKeyFrame(0,
                KeyTime.FromTimeSpan(TimeSpan.Zero)));
            visibility.KeyFrames.Add(new LinearDoubleKeyFrame(1,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(130))));
            visibility.KeyFrames.Add(new DiscreteDoubleKeyFrame(1,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1550))));
            visibility.KeyFrames.Add(new LinearDoubleKeyFrame(0,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1770))));
            visibility.Completed += delegate
            {
                if (osd.IsVisible) osd.Close();
            };
            osd.BeginAnimation(OpacityProperty, visibility);
        }

        private void MakeNonActivating(object sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int style = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
        }

        private static ImageSource LoadAcerArtwork()
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                "SwiftControl.AcerSystemUsage.png");
            if (stream == null) throw new InvalidOperationException("Acer OSD artwork is missing.");
            using (stream)
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr window, int index, int value);
    }
}
