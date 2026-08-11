using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SwiftControl
{
    internal sealed class MainWindow : Window
    {
        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupRegistryValue = "SwiftControl";

        private readonly Brush _background = Brush("#0B0E14");
        private readonly Brush _card = Brush("#151A22");
        private readonly Brush _cardBorder = Brush("#252C38");
        private readonly Brush _text = Brush("#F4F6F8");
        private readonly Brush _muted = Brush("#9AA4B2");
        private readonly Brush _accent = Brush("#7DD3A7");

        private TextBlock _percent;
        private TextBlock _powerState;
        private ProgressBar _progress;
        private CheckBox _limit;
        private ToggleButton _startup;
        private UniformGrid _powerModes;
        private int _currentPowerMode;
        private bool _optimizedCharging;
        private Button _refresh;
        private readonly DispatcherTimer _modePoll;
        private bool _loading;
        private bool _pollingMode;
        private bool _allowClose;
        private bool _suppressControlEvents;
        private bool _trayActionPending;
        private bool _positioning;

        public event Action<int> PowerModeObserved;
        public event Action<bool> ChargingLimitObserved;
        public event Action<bool> ChargingLimitChanged;
        public event Action<string> OperationFailed;

        public MainWindow()
        {
            Title = "SwiftControl";
            MinWidth = 320;
            MinHeight = 240;
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            AllowsTransparency = true;
            ShowInTaskbar = false;
            Background = Brushes.Transparent;
            Foreground = _text;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

            _modePoll = new DispatcherTimer();
            _modePoll.Interval = TimeSpan.FromSeconds(30);
            _modePoll.Tick += ModePollTick;

            Content = BuildLayout();
            Loaded += OnLoaded;
            SizeChanged += PanelSizeChanged;
            IsVisibleChanged += VisibilityChanged;
            Deactivated += PanelDeactivated;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Hide();
            };
        }

        private UIElement BuildLayout()
        {
            Border shell = new Border();
            shell.Background = _background;
            shell.BorderBrush = _cardBorder;
            shell.BorderThickness = new Thickness(1);
            shell.CornerRadius = new CornerRadius(18);

            Grid root = new Grid();
            root.Margin = new Thickness(20, 16, 20, 15);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shell.Child = root;

            Grid heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel identity = new StackPanel();
            identity.Orientation = Orientation.Horizontal;
            TextBlock title = Text(Environment.MachineName, 24, FontWeights.SemiBold, _text);
            title.VerticalAlignment = VerticalAlignment.Center;
            identity.Children.Add(title);
            TextBlock model = Text(SystemModelLabel(), 11, FontWeights.Normal, _muted);
            model.VerticalAlignment = VerticalAlignment.Center;
            model.Margin = new Thickness(11, 3, 0, 0);
            identity.Children.Add(model);
            identity.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            heading.Children.Add(identity);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;

            _startup = new ToggleButton();
            _startup.Content = "Launch at startup";
            _startup.Width = 116;
            _startup.Height = 28;
            _startup.Margin = new Thickness(0, 3, 9, 3);
            _startup.Padding = new Thickness(9, 0, 9, 1);
            _startup.FontSize = 11;
            _startup.FontWeight = FontWeights.SemiBold;
            _startup.BorderThickness = new Thickness(1);
            _startup.Cursor = Cursors.Hand;
            _startup.ToolTip = "Start SwiftControl with Windows";
            AutomationProperties.SetName(_startup, "Start SwiftControl with Windows");
            _startup.Template = CreateToggleButtonTemplate(14);
            _startup.Click += StartupClicked;
            SetStartupDisplay(false);
            actions.Children.Add(_startup);

            Button acerSense = Button("");
            acerSense.Width = 34;
            acerSense.Height = 34;
            acerSense.Padding = new Thickness(0);
            acerSense.Margin = new Thickness(0, 0, 8, 0);
            acerSense.Background = Brushes.Transparent;
            acerSense.BorderBrush = Brushes.Transparent;
            acerSense.BorderThickness = new Thickness(0);
            acerSense.Cursor = Cursors.Hand;
            acerSense.ToolTip = "Open AcerSense";
            AutomationProperties.SetName(acerSense, "Open AcerSense");
            ControlTemplate iconOnly = new ControlTemplate(typeof(Button));
            FrameworkElementFactory iconPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            iconPresenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            iconPresenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            iconOnly.VisualTree = iconPresenter;
            acerSense.Template = iconOnly;
            Image acerIcon = new Image();
            acerIcon.Source = LoadEmbeddedImage("SwiftControl.AcerSense.png");
            acerIcon.Width = 22;
            acerIcon.Height = 22;
            acerSense.Content = acerIcon;
            acerSense.Click += AcerSenseClicked;
            actions.Children.Add(acerSense);
            _refresh = Button("");
            _refresh.Width = 34;
            _refresh.Height = 34;
            _refresh.Padding = new Thickness(0);
            _refresh.Background = Brushes.Transparent;
            _refresh.BorderBrush = Brushes.Transparent;
            _refresh.BorderThickness = new Thickness(0);
            _refresh.Cursor = Cursors.Hand;
            _refresh.ToolTip = "Refresh";
            AutomationProperties.SetName(_refresh, "Refresh");
            _refresh.Template = iconOnly;
            TextBlock refreshIcon = Text("↻", 22, FontWeights.Normal, _text);
            refreshIcon.FontFamily = new FontFamily("Segoe UI Symbol");
            refreshIcon.Margin = new Thickness(0, -2, 0, 0);
            _refresh.Content = refreshIcon;
            _refresh.Click += RefreshClicked;
            actions.Children.Add(_refresh);
            Grid.SetColumn(actions, 1);
            heading.Children.Add(actions);
            root.Children.Add(heading);

            ScrollViewer bodyScroll = new ScrollViewer();
            bodyScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            bodyScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            bodyScroll.Margin = new Thickness(0, 14, 0, 0);
            Grid.SetRow(bodyScroll, 1);
            root.Children.Add(bodyScroll);

            Grid body = new Grid();
            bodyScroll.Content = body;

            Border batteryCard = Card();
            StackPanel battery = new StackPanel();
            batteryCard.Child = battery;

            Grid batteryHeader = new Grid();
            batteryHeader.ColumnDefinitions.Add(new ColumnDefinition());
            batteryHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock batteryHeading = Text("BATTERY", 11, FontWeights.SemiBold, _muted);
            batteryHeading.VerticalAlignment = VerticalAlignment.Center;
            batteryHeader.Children.Add(batteryHeading);

            _limit = new CheckBox();
            _limit.Content = "80% charge limit";
            _limit.FontSize = 12;
            _limit.FontWeight = FontWeights.SemiBold;
            _limit.Foreground = _text;
            _limit.VerticalContentAlignment = VerticalAlignment.Center;
            _limit.HorizontalAlignment = HorizontalAlignment.Right;
            _limit.Checked += LimitChanged;
            _limit.Unchecked += LimitChanged;
            Grid.SetColumn(_limit, 1);
            batteryHeader.Children.Add(_limit);
            battery.Children.Add(batteryHeader);

            Grid current = new Grid();
            current.Margin = new Thickness(0, 7, 0, 7);
            current.ColumnDefinitions.Add(new ColumnDefinition());
            current.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _percent = Text("--%", 38, FontWeights.SemiBold, _text);
            current.Children.Add(_percent);
            _powerState = Text("Connecting…", 13, FontWeights.Medium, _muted);
            _powerState.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_powerState, 1);
            current.Children.Add(_powerState);
            battery.Children.Add(current);

            _progress = new ProgressBar();
            _progress.Height = 6;
            _progress.Minimum = 0;
            _progress.Maximum = 100;
            _progress.Foreground = _accent;
            _progress.Background = Brush("#28303B");
            battery.Children.Add(_progress);

            TextBlock powerHeading = Text("POWER MODE", 11, FontWeights.SemiBold, _muted);
            powerHeading.Margin = new Thickness(0, 13, 0, 0);
            battery.Children.Add(powerHeading);

            _powerModes = new UniformGrid();
            _powerModes.Rows = 1;
            _powerModes.Margin = new Thickness(0, 8, 0, 0);
            battery.Children.Add(_powerModes);

            body.Children.Add(batteryCard);

            return shell;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateLayout();
            PositionBottomRight();
            RefreshStartupDisplay();
            await RefreshAsync();
            _modePoll.Start();
        }

        private void PanelSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // SizeToContent can change the panel after refreshed controls are
            // populated. Keep its bottom edge anchored instead of allowing the
            // newly added content to grow below the screen.
            if (IsVisible && !_positioning) PositionBottomRight();
        }

        public void StartHidden()
        {
            RefreshStartupDisplay();
            _modePoll.Interval = TimeSpan.FromSeconds(30);
            _modePoll.Start();
            RefreshTrayState();
        }

        private async void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                _modePoll.Interval = TimeSpan.FromSeconds(2);
                _modePoll.Start();
                if (IsLoaded) await RefreshPowerModeAsync();
            }
            else
            {
                _modePoll.Interval = TimeSpan.FromSeconds(30);
                if (IsLoaded) _modePoll.Start();
            }
        }

        private async void ModePollTick(object sender, EventArgs e)
        {
            await RefreshPowerModeAsync();
        }

        private async Task RefreshPowerModeAsync()
        {
            if (_loading || _pollingMode || _trayActionPending) return;
            _pollingMode = true;
            try
            {
                int mode = await Task.Run(new Func<int>(DashboardReader.ReadPowerMode));
                if (mode >= 0 && mode != _currentPowerMode)
                {
                    _currentPowerMode = mode;
                    UpdatePowerModeButtons(mode);
                    NotifyPowerModeObserved(mode);
                }
            }
            catch
            {
                // The main refresh surface reports connection failures. A
                // transient background poll should remain silent.
            }
            finally
            {
                _pollingMode = false;
            }
        }

        public void ShowFromTray()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            UpdateLayout();
            PositionBottomRight();
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void PanelDeactivated(object sender, EventArgs e)
        {
            if (_allowClose || !IsVisible) return;
            Hide();
        }

        public void ExitApplication()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            _modePoll.Stop();
            base.OnClosing(e);
        }

        private void PositionBottomRight()
        {
            const double edgePadding = 12;
            const double autoHideTaskbarReserve = 64;
            Rect area = SystemParameters.WorkArea;

            _positioning = true;
            try
            {
                // WPF reports the work area in device-independent units, so
                // these bounds also adapt when Windows display scaling changes.
                Width = Math.Max(MinWidth,
                    Math.Min(Math.Round(area.Width * 0.5), area.Width - (edgePadding * 2)));
                MaxHeight = Math.Max(MinHeight,
                    area.Height - (edgePadding * 2) - autoHideTaskbarReserve);

                Left = Math.Max(area.Left + edgePadding,
                    area.Right - ActualWidth - edgePadding);
                Top = Math.Max(area.Top + edgePadding,
                    area.Bottom - ActualHeight - edgePadding - autoHideTaskbarReserve);
            }
            finally
            {
                _positioning = false;
            }
        }

        private async void RefreshClicked(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async Task RefreshAsync(bool reportFailure = true)
        {
            if (_loading) return;
            _loading = true;
            SetControlsEnabled(false);

            try
            {
                DashboardSnapshot snapshot = await Task.Run(
                    new Func<DashboardSnapshot>(DashboardReader.Read));
                ApplySnapshot(snapshot);
            }
            catch (Exception exception)
            {
                if (reportFailure) ReportFailure("Could not read Acer services: " + exception.Message);
            }
            finally
            {
                _loading = false;
                SetControlsEnabled(true);
            }
        }

        private void ApplySnapshot(DashboardSnapshot snapshot)
        {
            _percent.Text = snapshot.BatteryPercent.ToString(CultureInfo.InvariantCulture) + "%";
            _progress.Value = snapshot.BatteryPercent;
            _powerState.Text = snapshot.OnAcPower ? "Plugged in" : "On battery";
            SetChargingLimitDisplay(snapshot.OptimizedCharging);

            _currentPowerMode = snapshot.CurrentPowerMode;
            _powerModes.Children.Clear();
            int modeIndex = 0;
            foreach (PowerModeOption option in snapshot.PowerModes)
            {
                ToggleButton button = new ToggleButton();
                button.Content = option.Name;
                button.Tag = option;
                button.MinHeight = 36;
                button.FontSize = 12;
                button.FontWeight = FontWeights.SemiBold;
                button.BorderThickness = new Thickness(1);
                button.Margin = new Thickness(modeIndex == 0 ? 0 : 3, 0,
                    modeIndex == snapshot.PowerModes.Count - 1 ? 0 : 3, 0);
                button.Click += PowerModeClicked;
                _powerModes.Children.Add(button);
                modeIndex++;
            }
            UpdatePowerModeButtons(_currentPowerMode);
            NotifyPowerModeObserved(_currentPowerMode);

        }

        private void AcerSenseClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = "explorer.exe";
                start.Arguments = "shell:AppsFolder\\ULICTekInc.AcerSense5.0_nt9dgb7efx6bt!AcerSense";
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch (Exception exception)
            {
                ReportFailure("Could not open AcerSense: " + exception.Message);
            }
        }

        private void StartupClicked(object sender, RoutedEventArgs e)
        {
            bool enabled = _startup.IsChecked == true;
            try
            {
                SetStartupEnabled(enabled);
                if (IsStartupEnabled() != enabled)
                    throw new InvalidOperationException("Windows did not retain the startup setting.");
                SetStartupDisplay(enabled);
            }
            catch (Exception exception)
            {
                SetStartupDisplay(!enabled);
                ReportFailure("Could not change launch-at-startup: " + exception.Message);
            }
        }

        private void RefreshStartupDisplay()
        {
            try
            {
                SetStartupDisplay(IsStartupEnabled());
            }
            catch (Exception exception)
            {
                ReportFailure("Could not read launch-at-startup: " + exception.Message);
            }
        }

        private void SetStartupDisplay(bool enabled)
        {
            _startup.IsChecked = enabled;
            _startup.Background = enabled ? _accent : Brush("#202733");
            _startup.Foreground = enabled ? _background : _text;
            _startup.BorderBrush = enabled ? _accent : _cardBorder;
        }

        private static bool IsStartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false))
            {
                if (key == null) return false;
                string value = Convert.ToString(key.GetValue(StartupRegistryValue, ""));
                return string.Equals(value, StartupCommand(), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void SetStartupEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath))
            {
                if (key == null) throw new InvalidOperationException("The Windows startup registry key is unavailable.");
                if (enabled)
                    key.SetValue(StartupRegistryValue, StartupCommand(), RegistryValueKind.String);
                else
                    key.DeleteValue(StartupRegistryValue, false);
            }
        }

        private static string StartupCommand()
        {
            return "\"" + Assembly.GetExecutingAssembly().Location + "\" --startup";
        }

        private async void LimitChanged(object sender, RoutedEventArgs e)
        {
            if (_loading || _suppressControlEvents) return;
            await ChangeOptimizedChargingAsync(_limit.IsChecked == true);
        }

        private async Task ChangeOptimizedChargingAsync(bool enabled)
        {
            if (_loading) return;
            bool previous = _optimizedCharging;
            bool refreshNeeded = false;
            _loading = true;
            SetControlsEnabled(false);
            try
            {
                bool verified = await Task.Run(new Func<bool>(
                    delegate { return DashboardReader.SetOptimizedCharging(enabled); }));
                if (!verified) throw new InvalidOperationException("The controller did not retain the new value.");
                SetChargingLimitDisplay(enabled);
                Action<bool> changed = ChargingLimitChanged;
                if (changed != null) changed(enabled);
            }
            catch (Exception exception)
            {
                SetChargingLimitDisplay(previous);
                ReportFailure("Charge-limit change failed: " + exception.Message);
                refreshNeeded = true;
            }
            finally
            {
                _loading = false;
                SetControlsEnabled(true);
            }
            if (refreshNeeded) await RefreshAsync(false);
        }

        private async void PowerModeClicked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            ToggleButton clicked = sender as ToggleButton;
            PowerModeOption option = clicked == null ? null : clicked.Tag as PowerModeOption;
            if (option == null) return;
            if (option.Value == _currentPowerMode)
            {
                UpdatePowerModeButtons(_currentPowerMode);
                return;
            }

            await ChangePowerModeAsync(option.Value, option.Name);
        }

        private async Task ChangePowerModeAsync(int value, string name)
        {
            if (_loading) return;
            int previous = _currentPowerMode;
            bool refreshNeeded = false;
            UpdatePowerModeButtons(value);
            _loading = true;
            SetControlsEnabled(false);
            try
            {
                bool verified = await Task.Run(new Func<bool>(
                    delegate { return DashboardReader.SetPowerMode(value); }));
                if (!verified) throw new InvalidOperationException("The firmware reported a different mode.");
                _currentPowerMode = value;
                UpdatePowerModeButtons(_currentPowerMode);
                NotifyPowerModeObserved(_currentPowerMode);
                ModeOsdWindow.Present(name);
            }
            catch (Exception exception)
            {
                UpdatePowerModeButtons(previous);
                ReportFailure("Power-mode change failed: " + exception.Message);
                refreshNeeded = true;
            }
            finally
            {
                _loading = false;
                SetControlsEnabled(true);
            }
            if (refreshNeeded) await RefreshAsync(false);
        }

        public async void CyclePowerModeFromTray()
        {
            if (_loading || _trayActionPending) return;
            _trayActionPending = true;
            try
            {
                int live = await Task.Run(new Func<int>(DashboardReader.ReadPowerMode));
                if (live < 0) throw new InvalidOperationException("Quick Access did not report a mode.");
                _currentPowerMode = live;
                UpdatePowerModeButtons(live);
                NotifyPowerModeObserved(live);
                int next = (live + 1) % 3;
                await ChangePowerModeAsync(next, ModeName(next));
            }
            catch (Exception exception)
            {
                ReportFailure("Power-mode change failed: " + exception.Message);
            }
            finally
            {
                _trayActionPending = false;
            }
        }

        public async void SetPowerModeFromTray(int mode)
        {
            if (_loading || _trayActionPending) return;
            _trayActionPending = true;
            try
            {
                await ChangePowerModeAsync(mode, ModeName(mode));
            }
            finally
            {
                _trayActionPending = false;
            }
        }

        public async void ToggleChargingLimitFromTray()
        {
            if (_loading || _trayActionPending) return;
            _trayActionPending = true;
            try
            {
                bool current = await Task.Run(new Func<bool>(DashboardReader.ReadOptimizedCharging));
                SetChargingLimitDisplay(current);
                await ChangeOptimizedChargingAsync(!current);
            }
            catch (Exception exception)
            {
                ReportFailure("Could not read the charging limit: " + exception.Message);
            }
            finally
            {
                _trayActionPending = false;
            }
        }

        public async void RefreshTrayState()
        {
            if (_loading || _trayActionPending) return;
            try
            {
                int mode = await Task.Run(new Func<int>(DashboardReader.ReadPowerMode));
                bool limit = await Task.Run(new Func<bool>(DashboardReader.ReadOptimizedCharging));
                if (mode >= 0)
                {
                    _currentPowerMode = mode;
                    UpdatePowerModeButtons(mode);
                    NotifyPowerModeObserved(mode);
                }
                SetChargingLimitDisplay(limit);
            }
            catch (Exception exception)
            {
                ReportFailure("Could not refresh tray controls: " + exception.Message);
            }
        }

        private void ReportFailure(string message)
        {
            Action<string> handler = OperationFailed;
            if (handler != null) handler(message);
        }

        private void UpdatePowerModeButtons(int selectedValue)
        {
            foreach (UIElement element in _powerModes.Children)
            {
                ToggleButton button = element as ToggleButton;
                PowerModeOption option = button == null ? null : button.Tag as PowerModeOption;
                if (option == null) continue;
                bool selected = option.Value == selectedValue;
                button.IsChecked = selected;
                button.Background = selected ? _accent : Brush("#202733");
                button.Foreground = selected ? _background : _text;
                button.BorderBrush = selected ? _accent : _cardBorder;
            }
        }

        private void NotifyPowerModeObserved(int mode)
        {
            Action<int> handler = PowerModeObserved;
            if (handler != null) handler(mode);
        }

        private void SetChargingLimitDisplay(bool enabled)
        {
            _optimizedCharging = enabled;
            _suppressControlEvents = true;
            try { _limit.IsChecked = enabled; }
            finally { _suppressControlEvents = false; }
            Action<bool> handler = ChargingLimitObserved;
            if (handler != null) handler(enabled);
        }

        private static string ModeName(int mode)
        {
            if (mode == 0) return "Silent";
            if (mode == 2) return "Performance";
            return "Normal";
        }

        private void SetControlsEnabled(bool enabled)
        {
            _refresh.IsEnabled = enabled;
            _limit.IsEnabled = enabled;
            foreach (UIElement element in _powerModes.Children)
            {
                element.IsEnabled = enabled;
            }
        }

        private Border Card()
        {
            Border border = new Border();
            border.Background = _card;
            border.BorderBrush = _cardBorder;
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(13);
            border.Padding = new Thickness(16);
            return border;
        }

        private Button Button(string content)
        {
            Button button = new Button();
            button.Content = content;
            button.Foreground = _text;
            button.Background = Brush("#202733");
            button.BorderBrush = _cardBorder;
            button.BorderThickness = new Thickness(1);
            button.Template = CreateButtonTemplate(9);
            return button;
        }

        private static ControlTemplate CreateButtonTemplate(double radius)
        {
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory chrome = new FrameworkElementFactory(typeof(Border));
            chrome.Name = "ButtonChrome";
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            chrome.SetValue(Border.BackgroundProperty,
                new TemplateBindingExtension(Control.BackgroundProperty));
            chrome.SetValue(Border.BorderBrushProperty,
                new TemplateBindingExtension(Control.BorderBrushProperty));
            chrome.SetValue(Border.BorderThicknessProperty,
                new TemplateBindingExtension(Control.BorderThicknessProperty));

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty,
                new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(FrameworkElement.MarginProperty,
                new TemplateBindingExtension(Control.PaddingProperty));
            presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            chrome.AppendChild(presenter);
            template.VisualTree = chrome;

            Trigger hover = new Trigger();
            hover.Property = IsMouseOverProperty;
            hover.Value = true;
            Setter hoverBackground = new Setter();
            hoverBackground.Property = Border.BackgroundProperty;
            hoverBackground.Value = Brush("#2B3442");
            hoverBackground.TargetName = "ButtonChrome";
            hover.Setters.Add(hoverBackground);
            template.Triggers.Add(hover);

            Trigger pressed = new Trigger();
            pressed.Property = ButtonBase.IsPressedProperty;
            pressed.Value = true;
            Setter pressedBackground = new Setter();
            pressedBackground.Property = Border.BackgroundProperty;
            pressedBackground.Value = Brush("#354153");
            pressedBackground.TargetName = "ButtonChrome";
            pressed.Setters.Add(pressedBackground);
            template.Triggers.Add(pressed);

            return template;
        }

        private static ControlTemplate CreateToggleButtonTemplate(double radius)
        {
            ControlTemplate template = new ControlTemplate(typeof(ToggleButton));
            FrameworkElementFactory chrome = new FrameworkElementFactory(typeof(Border));
            chrome.Name = "ToggleChrome";
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            chrome.SetValue(Border.BackgroundProperty,
                new TemplateBindingExtension(Control.BackgroundProperty));
            chrome.SetValue(Border.BorderBrushProperty,
                new TemplateBindingExtension(Control.BorderBrushProperty));
            chrome.SetValue(Border.BorderThicknessProperty,
                new TemplateBindingExtension(Control.BorderThicknessProperty));

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty,
                new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(FrameworkElement.MarginProperty,
                new TemplateBindingExtension(Control.PaddingProperty));
            presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            chrome.AppendChild(presenter);
            template.VisualTree = chrome;

            Trigger hover = new Trigger();
            hover.Property = IsMouseOverProperty;
            hover.Value = true;
            Setter hoverOpacity = new Setter();
            hoverOpacity.Property = UIElement.OpacityProperty;
            hoverOpacity.Value = 0.86;
            hoverOpacity.TargetName = "ToggleChrome";
            hover.Setters.Add(hoverOpacity);
            template.Triggers.Add(hover);

            Trigger pressed = new Trigger();
            pressed.Property = ButtonBase.IsPressedProperty;
            pressed.Value = true;
            Setter pressedOpacity = new Setter();
            pressedOpacity.Property = UIElement.OpacityProperty;
            pressedOpacity.Value = 0.68;
            pressedOpacity.TargetName = "ToggleChrome";
            pressed.Setters.Add(pressedOpacity);
            template.Triggers.Add(pressed);

            return template;
        }

        private static TextBlock Text(string content, double size, FontWeight weight, Brush color)
        {
            TextBlock block = new TextBlock();
            block.Text = content;
            block.FontSize = size;
            block.FontWeight = weight;
            block.Foreground = color;
            return block;
        }

        private static Brush Brush(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        private static ImageSource LoadEmbeddedImage(string resourceName)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream == null) throw new InvalidOperationException("Embedded image is missing: " + resourceName);
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

        private static string SystemModelLabel()
        {
            const string bios = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS";
            string maker = Convert.ToString(Registry.GetValue(bios, "SystemManufacturer", "Acer"));
            string model = Convert.ToString(Registry.GetValue(bios, "SystemProductName", "Swift"));
            return (maker + " " + model).Trim();
        }
    }
}
