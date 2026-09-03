using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace SwiftControl
{
    internal sealed class TrustedWifiWindow : Window
    {
        private const int MonitorDefaultToNearest = 2;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const int DwmWindowCornerPreference = 33;
        private const int DwmWindowCornerRound = 2;

        private readonly LockAfterSuspendSettings _settings;
        private readonly Action _settingsChanged;
        private readonly StackPanel _content;

        private readonly Brush _background = Brush("#05080D");
        private readonly Brush _pageSurface = Brush("#101B29");
        private readonly Brush _card = Brush("#223247");
        private readonly Brush _border = Brush("#34465C");
        private readonly Brush _text = Brush("#F4F6F8");
        private readonly Brush _muted = Brush("#DEE3EA");
        private readonly Brush _accent = Brush("#7DD3A7");

        public event Action FocusLeft;

        public TrustedWifiWindow(
            LockAfterSuspendSettings settings, Action settingsChanged)
        {
            _settings = settings;
            _settingsChanged = settingsChanged;
            Title = "Trusted Wi-Fi";
            Width = 390;
            MaxHeight = 620;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = _background;
            Foreground = _text;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

            Border shell = new Border();
            shell.Background = _background;
            shell.BorderBrush = _border;
            shell.BorderThickness = new Thickness(1);
            shell.CornerRadius = new CornerRadius(16);
            shell.Padding = new Thickness(16);

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.Child = root;

            Grid header = new Grid();
            header.Margin = new Thickness(2, 0, 0, 12);
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock title = Text("Trusted Wi-Fi", 20, FontWeights.SemiBold, _text);
            title.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(title);
            Button close = Button("×");
            close.Width = 30;
            close.Height = 30;
            close.FontSize = 18;
            close.ToolTip = "Close trusted Wi-Fi manager";
            close.Click += delegate { CloseToOwner(); };
            Grid.SetColumn(close, 1);
            header.Children.Add(close);
            root.Children.Add(header);

            Border page = new Border();
            page.Background = _pageSurface;
            page.BorderBrush = _border;
            page.BorderThickness = new Thickness(1);
            page.CornerRadius = new CornerRadius(12);
            page.Padding = new Thickness(10);
            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.MaxHeight = 500;
            _content = new StackPanel();
            scroll.Content = _content;
            page.Child = scroll;
            Grid.SetRow(page, 1);
            root.Children.Add(page);
            Content = shell;

            SourceInitialized += RoundCorners;
            Deactivated += delegate
            {
                Action handler = FocusLeft;
                if (handler != null) handler();
            };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    CloseToOwner();
                }
            };
            RefreshNetworks();
        }

        public void RefreshNetworks()
        {
            _content.Children.Clear();
            WifiConnection current = WifiNetwork.Current();
            bool currentTrusted = _settings.IsTrusted(current);

            _content.Children.Add(SectionLabel("CURRENT NETWORK"));
            Border currentCard = Card();
            StackPanel currentContent = new StackPanel();
            currentCard.Child = currentContent;
            if (current == null)
            {
                currentContent.Children.Add(
                    Text("No Wi-Fi connected", 13, FontWeights.SemiBold, _text));
                TextBlock detail = Text(
                    "Connect to a network to add it as trusted.",
                    10, FontWeights.Normal, _muted);
                detail.Margin = new Thickness(0, 4, 0, 0);
                currentContent.Children.Add(detail);
            }
            else
            {
                Grid row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                StackPanel identity = new StackPanel();
                identity.Children.Add(Text(
                    current.Name, 14, FontWeights.SemiBold, _text));
                TextBlock status = Text(
                    currentTrusted ? "Trusted · resume stays unlocked" :
                        "Untrusted · next resume locks",
                    10, FontWeights.Normal, currentTrusted ? _accent : _muted);
                status.Margin = new Thickness(0, 3, 0, 0);
                identity.Children.Add(status);
                row.Children.Add(identity);
                Button toggle = Button(currentTrusted ? "Stop trusting" : "Trust");
                toggle.Tag = current;
                toggle.Height = 30;
                toggle.Padding = new Thickness(10, 0, 10, 1);
                toggle.Click += ToggleCurrentClicked;
                Grid.SetColumn(toggle, 1);
                row.Children.Add(toggle);
                currentContent.Children.Add(row);
            }
            _content.Children.Add(currentCard);

            WifiConnection[] trusted = _settings.TrustedWifiNetworks();
            TextBlock trustedLabel = SectionLabel(
                "TRUSTED NETWORKS · " + trusted.Length);
            trustedLabel.Margin = new Thickness(2, 14, 2, 7);
            _content.Children.Add(trustedLabel);

            if (trusted.Length == 0)
            {
                Border empty = Card();
                TextBlock message = Text(
                    "No trusted networks yet. Trust the current network above to add it.",
                    11, FontWeights.Normal, _muted);
                message.TextWrapping = TextWrapping.Wrap;
                empty.Child = message;
                _content.Children.Add(empty);
                return;
            }

            for (int index = 0; index < trusted.Length; index++)
            {
                WifiConnection network = trusted[index];
                Border networkCard = Card();
                if (index > 0) networkCard.Margin = new Thickness(0, 7, 0, 0);
                Grid row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                StackPanel identity = new StackPanel();
                identity.VerticalAlignment = VerticalAlignment.Center;
                identity.Children.Add(Text(
                    network.Name, 12, FontWeights.SemiBold, _text));
                if (current != null && String.Equals(
                    current.Id, network.Id, StringComparison.OrdinalIgnoreCase))
                {
                    TextBlock connected = Text(
                        "Currently connected", 10, FontWeights.Normal, _accent);
                    connected.Margin = new Thickness(0, 2, 0, 0);
                    identity.Children.Add(connected);
                }
                row.Children.Add(identity);
                Button remove = Button("Remove");
                remove.Tag = network.Id;
                remove.Height = 28;
                remove.Padding = new Thickness(9, 0, 9, 1);
                remove.Click += RemoveClicked;
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                networkCard.Child = row;
                _content.Children.Add(networkCard);
            }
        }

        public void PositionNextTo(FrameworkElement anchor, Window owner)
        {
            IntPtr ownerHandle = new WindowInteropHelper(owner).Handle;
            IntPtr handle = new WindowInteropHelper(this).Handle;
            MonitorInfo monitor = new MonitorInfo();
            monitor.Size = Marshal.SizeOf(typeof(MonitorInfo));
            IntPtr monitorHandle = MonitorFromWindow(ownerHandle, MonitorDefaultToNearest);
            if (ownerHandle == IntPtr.Zero || handle == IntPtr.Zero || anchor == null ||
                monitorHandle == IntPtr.Zero || !GetMonitorInfo(monitorHandle, ref monitor))
                return;

            uint dpi = 96;
            try { dpi = GetDpiForWindow(handle); }
            catch (EntryPointNotFoundException) { }
            if (dpi == 0) dpi = 96;
            double scale = dpi / 96.0;
            UpdateLayout();
            int width = Math.Max(1, (int)Math.Round(ActualWidth * scale));
            int height = Math.Max(1, (int)Math.Round(ActualHeight * scale));
            int gap = Math.Max(8, (int)Math.Round(10 * scale));
            Point anchorTopLeft = anchor.PointToScreen(new Point(0, 0));
            Point anchorBottomRight = anchor.PointToScreen(
                new Point(anchor.ActualWidth, anchor.ActualHeight));
            int anchorLeft = (int)Math.Round(anchorTopLeft.X);
            int anchorRight = (int)Math.Round(anchorBottomRight.X);
            int anchorCenterY = (int)Math.Round(
                (anchorTopLeft.Y + anchorBottomRight.Y) / 2.0);
            int left = anchorLeft - width - gap;
            if (left < monitor.Work.Left)
                left = anchorRight + gap;
            left = Math.Min(left, monitor.Work.Right - width);
            int top = anchorCenterY - (height / 2);
            top = Math.Max(monitor.Work.Top,
                Math.Min(top, monitor.Work.Bottom - height));
            SetWindowPos(handle, IntPtr.Zero, left, top, width, height,
                SwpNoZOrder | SwpNoActivate);
        }

        private void ToggleCurrentClicked(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            WifiConnection wifi = button == null ? null : button.Tag as WifiConnection;
            if (wifi == null) return;
            _settings.SetTrusted(wifi, !_settings.IsTrusted(wifi));
            SaveAndRefresh();
        }

        private void RemoveClicked(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            string id = button == null ? null : button.Tag as string;
            _settings.RemoveTrusted(id);
            SaveAndRefresh();
        }

        private void SaveAndRefresh()
        {
            try
            {
                _settings.Save();
                RefreshNetworks();
                if (_settingsChanged != null) _settingsChanged();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "Could not save trusted Wi-Fi: " + exception.Message,
                    "SwiftControl", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseToOwner()
        {
            Window owner = Owner;
            Close();
            if (owner != null && owner.IsVisible) owner.Activate();
        }

        private Border Card()
        {
            Border card = new Border();
            card.Background = _card;
            card.BorderBrush = _border;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(10);
            card.Padding = new Thickness(12);
            return card;
        }

        private TextBlock SectionLabel(string value)
        {
            TextBlock label = Text(value, 10, FontWeights.SemiBold, _muted);
            label.Margin = new Thickness(2, 0, 2, 7);
            return label;
        }

        private Button Button(string value)
        {
            Button button = new Button();
            button.Content = value;
            button.Foreground = _text;
            button.Background = Brush("#202D3C");
            button.BorderBrush = _border;
            button.BorderThickness = new Thickness(1);
            button.Padding = new Thickness(8, 0, 8, 1);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private static TextBlock Text(
            string value, double size, FontWeight weight, Brush foreground)
        {
            TextBlock text = new TextBlock();
            text.Text = value;
            text.FontSize = size;
            text.FontWeight = weight;
            text.Foreground = foreground;
            return text;
        }

        private static Brush Brush(string value)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }

        private void RoundCorners(object sender, EventArgs e)
        {
            try
            {
                int preference = DwmWindowCornerRound;
                DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,
                    DwmWindowCornerPreference, ref preference, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public int Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr window, int attribute, ref int value, int size);
    }
}
