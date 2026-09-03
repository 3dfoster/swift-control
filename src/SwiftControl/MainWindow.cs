using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
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
        private const int MonitorDefaultToNearest = 2;
        private const int WmSettingChange = 0x001A;
        private const int WmDisplayChange = 0x007E;
        private const int WmDpiChanged = 0x02E0;
        private const int WmPowerBroadcast = 0x0218;
        private const int PbtApmSuspend = 0x0004;
        private const int PbtApmResumeSuspend = 0x0007;
        private const int PbtApmResumeAutomatic = 0x0012;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const int DwmWindowCornerPreference = 33;
        private const int DwmWindowCornerRound = 2;
        private const int StartupServiceWaitSeconds = 90;
        private const int StartupServiceRetrySeconds = 3;
        // Windows 11 can leave an auto-hidden taskbar visible while still
        // reporting the full monitor as usable. Always reserve its normal
        // height so the flyout cannot be covered during that transition.
        private const double TaskbarReserve = 48;

        private readonly Brush _background = Brush("#05080D");
        private readonly Brush _pageSurface = Brush("#101B29");
        private readonly Brush _card = Brush("#223247");
        private readonly Brush _cardBorder = Brush("#34465C");
        private readonly Brush _text = Brush("#F4F6F8");
        private readonly Brush _muted = Brush("#DEE3EA");
        private readonly Brush _accent = Brush("#7DD3A7");

        private TextBlock _percent;
        private TextBlock _powerState;
        private ProgressBar _progress;
        private CheckBox _limit;
        private ToggleButton _startup;
        private UniformGrid _quickProfiles;
        private UniformGrid _powerModes;
        private UniformGrid _windowsPowerModes;
        private ToggleButton _batteryHibernate;
        private TextBlock _batteryHibernateStatus;
        private ToggleButton _standbyNetwork;
        private TextBlock _standbyNetworkStatus;
        private readonly ToggleButton[] _lockAfterSuspendModes =
            new ToggleButton[3];
        private Button _trustCurrentWifi;
        private Button _manageTrustedWifi;
        private TextBlock _lockAfterSuspendStatus;
        private ToggleButton _batteryTab;
        private ToggleButton _touchpadTab;
        private FrameworkElement _batteryPage;
        private FrameworkElement _touchpadPage;
        private ToggleButton _touchpadGlobal;
        private TextBlock _touchpadStatus;
        private ToggleButton _codexLighting;
        private UniformGrid _codexEffects;
        private TextBlock _profileTitle;
        private TextBlock _profileSummary;
        private ToggleButton _advancedToggle;
        private StackPanel _advancedPanel;
        private TextBlock _acerProfileDescription;
        private TextBlock _windowsPolicyDescription;
        private ToggleButton _automationEnabled;
        private WrapPanel _automationAssignmentRow;
        private Grid _automationStatusRow;
        private readonly ToggleButton[] _conditionButtons = new ToggleButton[3];
        private Border _lowBatteryCondition;
        private TextBox _lowBatteryThreshold;
        private TextBlock _lowBatteryPercentLabel;
        private TextBlock _automationStatus;
        private Button _resumeAutomation;
        private Button _closeAcerSense;
        private readonly PowerAutomationSettings _automationSettings;
        private readonly TouchpadLightingSettings _lightingSettings;
        private readonly LockAfterSuspendSettings _lockSettings;
        private WifiConnection _currentWifi;
        private bool _lockOnNextResume;
        private int _currentPowerMode = -1;
        private int _currentWindowsPowerMode = -1;
        private int _lastAutomationCondition = -1;
        private int _selectedAutomationCondition = -1;
        private int _dragCondition = -1;
        private Point _conditionDragStart;
        private bool _conditionDragCompleted;
        private bool _onAcPower;
        private bool _optimizedCharging;
        private readonly DispatcherTimer _modePoll;
        private readonly DispatcherTimer _dashboardPoll;
        private bool _loading;
        private bool _pollingMode;
        private bool _allowClose;
        private bool _suppressControlEvents;
        private bool _trayActionPending;
        private bool _positioning;
        private bool _automationApplying;
        private bool _changingWindowsPowerMode;
        private bool _changingBatteryHibernate;
        private bool _changingStandbyNetwork;
        private bool _changingTouchpadGlobal;
        private bool _refreshingTouchpad;
        private bool _refreshingWindowsPowerMode;
        private bool _refreshingBatteryHibernate;
        private bool _refreshingStandbyNetwork;
        private bool _refreshingTrayState;
        private bool _visibleRefreshRunning;
        private bool _visibleRefreshRequested;
        private int _lightingPreviewGeneration;
        private IntPtr _targetMonitor;
        private HwndSource _windowSource;
        private IntPtr _suspendResumeRegistration;
        private bool _suppressMonitorTracking;
        private bool _focusDismissQueued;
        private bool _openingTrustedWifiWindow;
        private TrustedWifiWindow _trustedWifiWindow;

        public event Action<int> PowerModeObserved;
        public event Action<int> PowerProfileObserved;
        public event Action<bool> ChargingLimitObserved;
        public event Action<bool> ChargingLimitChanged;
        public event Action<string> OperationFailed;

        public MainWindow()
        {
            _automationSettings = PowerAutomationSettings.Load();
            _lightingSettings = TouchpadLightingSettings.Load();
            _lockSettings = LockAfterSuspendSettings.Load();
            Title = "SwiftControl";
            MinWidth = 320;
            MinHeight = 240;
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            // A transparent WPF window becomes a large software-composited
            // surface on a scaled display and can disrupt hardware video.
            // Keep the window accelerated and let DWM round it instead.
            AllowsTransparency = false;
            ShowInTaskbar = false;
            Background = _background;
            Foreground = _text;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

            _modePoll = new DispatcherTimer();
            _modePoll.Interval = TimeSpan.FromSeconds(30);
            _modePoll.Tick += ModePollTick;

            _dashboardPoll = new DispatcherTimer();
            _dashboardPoll.Interval = TimeSpan.FromSeconds(30);
            _dashboardPoll.Tick += DashboardPollTick;

            Content = BuildLayout();
            Loaded += OnLoaded;
            SizeChanged += PanelSizeChanged;
            LocationChanged += PanelLocationChanged;
            IsVisibleChanged += VisibilityChanged;
            Deactivated += PanelDeactivated;
            SystemEvents.PowerModeChanged += SystemPowerModeChanged;
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
            Grid.SetColumn(actions, 1);
            heading.Children.Add(actions);
            root.Children.Add(heading);

            Border tabChrome = new Border();
            tabChrome.Margin = new Thickness(0, 13, 0, 0);
            tabChrome.Padding = new Thickness(2, 2, 2, 0);
            tabChrome.Background = _background;
            tabChrome.BorderBrush = _cardBorder;
            tabChrome.BorderThickness = new Thickness(1, 1, 1, 0);
            tabChrome.CornerRadius = new CornerRadius(9, 9, 0, 0);
            UniformGrid tabs = new UniformGrid { Rows = 1 };
            _batteryTab = CreatePanelTab("Battery", "battery");
            _touchpadTab = CreatePanelTab("Touchpad light", "touchpad");
            tabs.Children.Add(_batteryTab);
            tabs.Children.Add(_touchpadTab);
            tabChrome.Child = tabs;
            Grid.SetRow(tabChrome, 1);
            root.Children.Add(tabChrome);

            ScrollViewer bodyScroll = new ScrollViewer();
            bodyScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            bodyScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            bodyScroll.Margin = new Thickness(0);
            Grid.SetRow(bodyScroll, 2);
            root.Children.Add(bodyScroll);

            Grid body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bodyScroll.Content = body;

            Border batteryPage = TabPageSurface();
            _batteryPage = batteryPage;
            StackPanel batteryPageContent = new StackPanel();
            batteryPage.Child = batteryPageContent;
            Border batteryCard = Card();
            StackPanel battery = new StackPanel();
            batteryCard.Child = battery;
            batteryPageContent.Children.Add(batteryCard);

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

            Border lockCard = Card();
            lockCard.Margin = new Thickness(0, 8, 0, 0);
            StackPanel lockSection = new StackPanel();
            lockCard.Child = lockSection;
            batteryPageContent.Children.Add(lockCard);
            battery = lockSection;

            Grid lockHeader = new Grid();
            TextBlock lockHeading = Text(
                "LOCK AFTER SUSPEND", 11, FontWeights.SemiBold, _muted);
            lockHeading.VerticalAlignment = VerticalAlignment.Center;
            lockHeader.Children.Add(lockHeading);
            battery.Children.Add(lockHeader);

            UniformGrid lockModes = new UniformGrid();
            lockModes.Rows = 1;
            lockModes.Margin = new Thickness(0, 7, 0, 0);
            string[] lockModeNames = { "Off", "Smart", "Always" };
            string[] lockModeTips =
            {
                "Do not lock after suspend",
                "Lock unless the connected Wi-Fi is trusted",
                "Always show the Windows lock screen after suspend or hibernate"
            };
            for (int mode = 0; mode < lockModeNames.Length; mode++)
            {
                ToggleButton button = new ToggleButton();
                button.Content = lockModeNames[mode];
                button.Tag = mode;
                button.MinHeight = 34;
                button.FontSize = 11;
                button.FontWeight = FontWeights.SemiBold;
                button.BorderThickness = new Thickness(1);
                button.Margin = new Thickness(mode == 0 ? 0 : 3, 0,
                    mode == lockModeNames.Length - 1 ? 0 : 3, 0);
                button.Cursor = Cursors.Hand;
                button.ToolTip = lockModeTips[mode];
                AutomationProperties.SetName(button,
                    lockModeNames[mode] + " lock after suspend");
                button.Click += LockAfterSuspendModeClicked;
                _lockAfterSuspendModes[mode] = button;
                lockModes.Children.Add(button);
            }
            battery.Children.Add(lockModes);

            _trustCurrentWifi = Button("Trust current Wi-Fi");
            _trustCurrentWifi.Height = 29;
            _trustCurrentWifi.Margin = new Thickness(0, 7, 0, 0);
            _trustCurrentWifi.HorizontalAlignment = HorizontalAlignment.Left;
            _trustCurrentWifi.Padding = new Thickness(10, 0, 10, 1);
            _trustCurrentWifi.FontSize = 11;
            _trustCurrentWifi.ToolTip =
                "Toggle whether the current Windows Wi-Fi profile is trusted";
            _trustCurrentWifi.Click += TrustCurrentWifiClicked;

            StackPanel wifiActions = new StackPanel();
            wifiActions.Orientation = Orientation.Horizontal;
            wifiActions.Margin = new Thickness(0, 7, 0, 0);
            _trustCurrentWifi.Margin = new Thickness(0);
            wifiActions.Children.Add(_trustCurrentWifi);
            _manageTrustedWifi = Button("Manage trusted Wi-Fi  ›");
            _manageTrustedWifi.Height = 29;
            _manageTrustedWifi.Margin = new Thickness(7, 0, 0, 0);
            _manageTrustedWifi.Padding = new Thickness(10, 0, 10, 1);
            _manageTrustedWifi.FontSize = 11;
            _manageTrustedWifi.ToolTip = "View and remove trusted Wi-Fi networks";
            _manageTrustedWifi.Click += ManageTrustedWifiClicked;
            wifiActions.Children.Add(_manageTrustedWifi);
            battery.Children.Add(wifiActions);

            _lockAfterSuspendStatus = Text(
                "Reading current Wi-Fi…", 10, FontWeights.Normal, _muted);
            _lockAfterSuspendStatus.Margin = new Thickness(0, 5, 0, 0);
            _lockAfterSuspendStatus.TextWrapping = TextWrapping.Wrap;
            battery.Children.Add(_lockAfterSuspendStatus);

            Border profileCard = Card();
            profileCard.Margin = new Thickness(0, 8, 0, 0);
            StackPanel profileSection = new StackPanel();
            profileCard.Child = profileSection;
            batteryPageContent.Children.Add(profileCard);
            battery = profileSection;

            Grid profileHeader = new Grid();
            profileHeader.ColumnDefinitions.Add(new ColumnDefinition());
            profileHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock profileHeading = Text("POWER PROFILE", 11, FontWeights.SemiBold, _muted);
            profileHeading.VerticalAlignment = VerticalAlignment.Center;
            profileHeader.Children.Add(profileHeading);

            _automationEnabled = new ToggleButton();
            _automationEnabled.Content = "Auto  ▾";
            _automationEnabled.Height = 29;
            _automationEnabled.Padding = new Thickness(10, 0, 10, 1);
            _automationEnabled.FontSize = 11;
            _automationEnabled.FontWeight = FontWeights.SemiBold;
            _automationEnabled.Foreground = _text;
            _automationEnabled.Background = Brush("#202733");
            _automationEnabled.BorderBrush = _cardBorder;
            _automationEnabled.BorderThickness = new Thickness(1);
            _automationEnabled.Cursor = Cursors.Hand;
            _automationEnabled.Template = CreateToggleButtonTemplate(9);
            _automationEnabled.ToolTip = "Apply the assigned profile when power conditions change";
            _automationEnabled.IsChecked = _automationSettings.Enabled;
            _automationEnabled.Checked += AutomationEnabledChanged;
            _automationEnabled.Unchecked += AutomationEnabledChanged;
            Grid.SetColumn(_automationEnabled, 1);
            profileHeader.Children.Add(_automationEnabled);
            battery.Children.Add(profileHeader);

            Grid selectedProfileRow = new Grid();
            selectedProfileRow.Margin = new Thickness(0, 7, 0, 0);
            selectedProfileRow.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });
            selectedProfileRow.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });
            selectedProfileRow.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 0 });
            TextBlock selectedLabel = Text("Selected:", 11, FontWeights.SemiBold, _muted);
            selectedLabel.VerticalAlignment = VerticalAlignment.Center;
            selectedProfileRow.Children.Add(selectedLabel);

            _profileTitle = Text("Reading…", 13, FontWeights.SemiBold, _text);
            _profileTitle.Margin = new Thickness(6, 0, 0, 0);
            _profileTitle.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_profileTitle, 1);
            selectedProfileRow.Children.Add(_profileTitle);
            _profileSummary = Text("", 11, FontWeights.Normal, _muted);
            _profileSummary.Margin = new Thickness(12, 0, 0, 0);
            _profileSummary.VerticalAlignment = VerticalAlignment.Center;
            _profileSummary.MinWidth = 0;
            _profileSummary.TextWrapping = TextWrapping.NoWrap;
            _profileSummary.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(_profileSummary, 2);
            selectedProfileRow.Children.Add(_profileSummary);
            battery.Children.Add(selectedProfileRow);

            _automationAssignmentRow = new WrapPanel();
            _automationAssignmentRow.Margin = new Thickness(0, 9, 0, 0);
            TextBlock assignLabel = Text("ASSIGN", 10, FontWeights.SemiBold, _muted);
            assignLabel.VerticalAlignment = VerticalAlignment.Center;
            assignLabel.Margin = new Thickness(0, 0, 7, 0);
            _automationAssignmentRow.Children.Add(assignLabel);
            string[] conditionNames = { "Plugged", "Battery" };
            string[] conditionIcons = { "⚡", "\uE856" };
            for (int condition = 0; condition < 2; condition++)
            {
                ToggleButton button = CreateConditionButton(
                    condition, conditionIcons[condition], conditionNames[condition]);
                _conditionButtons[condition] = button;
                _automationAssignmentRow.Children.Add(button);
            }

            _lowBatteryCondition = new Border();
            _lowBatteryCondition.Height = 29;
            _lowBatteryCondition.Margin = new Thickness(0, 0, 5, 0);
            _lowBatteryCondition.Background = Brush("#202733");
            _lowBatteryCondition.BorderBrush = _cardBorder;
            _lowBatteryCondition.BorderThickness = new Thickness(1);
            _lowBatteryCondition.CornerRadius = new CornerRadius(9);
            StackPanel lowBatteryContent = new StackPanel();
            lowBatteryContent.Orientation = Orientation.Horizontal;
            ToggleButton lowBatteryButton = CreateConditionButton(2, "\uE851", "Below");
            lowBatteryButton.Height = 27;
            lowBatteryButton.Margin = new Thickness(0);
            lowBatteryButton.Padding = new Thickness(8, 0, 4, 1);
            lowBatteryButton.Background = Brushes.Transparent;
            lowBatteryButton.BorderThickness = new Thickness(0);
            lowBatteryButton.Template = CreateContentOnlyToggleTemplate();
            _conditionButtons[2] = lowBatteryButton;
            lowBatteryContent.Children.Add(lowBatteryButton);

            _lowBatteryThreshold = new TextBox();
            _lowBatteryThreshold.Width = 27;
            _lowBatteryThreshold.Height = 17;
            _lowBatteryThreshold.Margin = new Thickness(0, 0, 1, 0);
            _lowBatteryThreshold.Padding = new Thickness(0);
            _lowBatteryThreshold.TextAlignment = TextAlignment.Center;
            _lowBatteryThreshold.VerticalAlignment = VerticalAlignment.Center;
            _lowBatteryThreshold.VerticalContentAlignment = VerticalAlignment.Center;
            _lowBatteryThreshold.FontSize = 11;
            _lowBatteryThreshold.FontWeight = FontWeights.SemiBold;
            _lowBatteryThreshold.Foreground = _text;
            _lowBatteryThreshold.Background = Brushes.Transparent;
            _lowBatteryThreshold.BorderThickness = new Thickness(0);
            _lowBatteryThreshold.Text = _automationSettings.LowBatteryThreshold
                .ToString(CultureInfo.InvariantCulture);
            _lowBatteryThreshold.ToolTip = "Low-battery threshold";
            AutomationProperties.SetName(_lowBatteryThreshold, "Low-battery percentage");
            _lowBatteryThreshold.PreviewMouseLeftButtonDown += LowBatteryThresholdMouseDown;
            _lowBatteryThreshold.LostKeyboardFocus += AutomationThresholdLostFocus;
            _lowBatteryThreshold.KeyDown += AutomationThresholdKeyDown;
            lowBatteryContent.Children.Add(_lowBatteryThreshold);
            _lowBatteryPercentLabel = Text("%", 11, FontWeights.SemiBold, _muted);
            _lowBatteryPercentLabel.Margin = new Thickness(0, 0, 7, 0);
            _lowBatteryPercentLabel.VerticalAlignment = VerticalAlignment.Center;
            lowBatteryContent.Children.Add(_lowBatteryPercentLabel);
            _lowBatteryCondition.Child = lowBatteryContent;
            _automationAssignmentRow.Children.Add(_lowBatteryCondition);
            battery.Children.Add(_automationAssignmentRow);

            _quickProfiles = new UniformGrid();
            _quickProfiles.Rows = 1;
            _quickProfiles.Margin = new Thickness(0, 9, 0, 0);
            PowerProfileOption[] profiles = PowerProfiles.All();
            for (int index = 0; index < profiles.Length; index++)
            {
                PowerProfileOption profile = profiles[index];
                ToggleButton button = new ToggleButton();
                button.Content = QuickProfileContent(profile);
                button.Tag = profile;
                button.MinHeight = 66;
                button.BorderThickness = new Thickness(1);
                button.Margin = new Thickness(index == 0 ? 0 : 3, 0,
                    index == profiles.Length - 1 ? 0 : 3, 0);
                button.ToolTip = profile.Description;
                button.Click += QuickProfileClicked;
                button.AllowDrop = true;
                button.DragOver += ProfileDragOver;
                button.DragLeave += ProfileDragLeave;
                button.Drop += ProfileDrop;
                _quickProfiles.Children.Add(button);
            }
            battery.Children.Add(_quickProfiles);

            _automationStatusRow = new Grid();
            _automationStatusRow.Margin = new Thickness(0, 8, 0, 0);
            _automationStatusRow.ColumnDefinitions.Add(new ColumnDefinition());
            _automationStatusRow.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });
            _automationStatus = Text("", 10, FontWeights.Normal, _muted);
            _automationStatus.TextWrapping = TextWrapping.Wrap;
            _automationStatus.VerticalAlignment = VerticalAlignment.Center;
            _automationStatusRow.Children.Add(_automationStatus);
            _resumeAutomation = Button("Resume auto");
            _resumeAutomation.Height = 28;
            _resumeAutomation.Margin = new Thickness(9, 0, 0, 0);
            _resumeAutomation.Padding = new Thickness(9, 0, 9, 1);
            _resumeAutomation.FontSize = 11;
            _resumeAutomation.Visibility = Visibility.Collapsed;
            _resumeAutomation.Click += ResumeAutomationClicked;
            Grid.SetColumn(_resumeAutomation, 1);
            _automationStatusRow.Children.Add(_resumeAutomation);
            battery.Children.Add(_automationStatusRow);

            Grid advancedActions = new Grid();
            advancedActions.Margin = new Thickness(0, 10, 0, 0);
            advancedActions.ColumnDefinitions.Add(new ColumnDefinition());
            advancedActions.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            _advancedToggle = new ToggleButton();
            _advancedToggle.Content = "Advanced controls  ▾";
            _advancedToggle.Height = 30;
            _advancedToggle.HorizontalAlignment = HorizontalAlignment.Left;
            _advancedToggle.Padding = new Thickness(10, 0, 10, 1);
            _advancedToggle.FontSize = 11;
            _advancedToggle.FontWeight = FontWeights.SemiBold;
            _advancedToggle.Foreground = _text;
            _advancedToggle.Background = Brush("#202733");
            _advancedToggle.BorderBrush = _cardBorder;
            _advancedToggle.BorderThickness = new Thickness(1);
            _advancedToggle.Cursor = Cursors.Hand;
            _advancedToggle.Template = CreateToggleButtonTemplate(9);
            _advancedToggle.Click += AdvancedToggleClicked;
            advancedActions.Children.Add(_advancedToggle);

            _closeAcerSense = Button("Close AcerSense");
            _closeAcerSense.Height = 30;
            _closeAcerSense.Padding = new Thickness(11, 0, 11, 1);
            _closeAcerSense.Margin = new Thickness(10, 0, 0, 0);
            _closeAcerSense.HorizontalAlignment = HorizontalAlignment.Right;
            _closeAcerSense.FontSize = 11;
            _closeAcerSense.FontWeight = FontWeights.SemiBold;
            _closeAcerSense.Background = Brush("#3A2028");
            _closeAcerSense.BorderBrush = Brush("#79404C");
            _closeAcerSense.Cursor = Cursors.Hand;
            _closeAcerSense.ToolTip =
                "Force-close the AcerSense UI without stopping Acer background services";
            AutomationProperties.SetName(_closeAcerSense, "Close AcerSense");
            _closeAcerSense.Visibility = Visibility.Collapsed;
            _closeAcerSense.Click += CloseAcerSenseClicked;
            Grid.SetColumn(_closeAcerSense, 1);
            advancedActions.Children.Add(_closeAcerSense);
            battery.Children.Add(advancedActions);

            _advancedPanel = new StackPanel();
            _advancedPanel.Visibility = Visibility.Collapsed;
            battery.Children.Add(_advancedPanel);

            TextBlock acerHeading = Text(
                "ACER SYSTEM PROFILE", 11, FontWeights.SemiBold, _muted);
            acerHeading.Margin = new Thickness(0, 13, 0, 0);
            _advancedPanel.Children.Add(acerHeading);

            _powerModes = new UniformGrid();
            _powerModes.Rows = 1;
            _powerModes.Margin = new Thickness(0, 8, 0, 0);
            _advancedPanel.Children.Add(_powerModes);
            _acerProfileDescription = Text("", 10, FontWeights.Normal, _muted);
            _acerProfileDescription.Margin = new Thickness(0, 5, 0, 0);
            _acerProfileDescription.TextWrapping = TextWrapping.Wrap;
            _advancedPanel.Children.Add(_acerProfileDescription);

            TextBlock windowsPowerHeading = Text(
                "WINDOWS PERFORMANCE POLICY", 11, FontWeights.SemiBold, _muted);
            windowsPowerHeading.Margin = new Thickness(0, 13, 0, 0);
            _advancedPanel.Children.Add(windowsPowerHeading);

            _windowsPowerModes = new UniformGrid();
            _windowsPowerModes.Rows = 1;
            _windowsPowerModes.Margin = new Thickness(0, 8, 0, 0);
            string[] windowsModeNames = { "Efficiency", "Balanced", "Performance" };
            for (int mode = 0; mode < windowsModeNames.Length; mode++)
            {
                ToggleButton button = new ToggleButton();
                button.Content = windowsModeNames[mode];
                button.Tag = mode;
                button.MinHeight = 36;
                button.FontSize = 12;
                button.FontWeight = FontWeights.SemiBold;
                button.BorderThickness = new Thickness(1);
                button.Margin = new Thickness(mode == 0 ? 0 : 3, 0,
                    mode == windowsModeNames.Length - 1 ? 0 : 3, 0);
                button.ToolTip = "Set Windows responsiveness for the current power source";
                button.Click += WindowsPowerModeClicked;
                _windowsPowerModes.Children.Add(button);
            }
            _windowsPolicyDescription = Text("", 10, FontWeights.Normal, _muted);
            _windowsPolicyDescription.Margin = new Thickness(0, 5, 0, 0);
            _windowsPolicyDescription.TextWrapping = TextWrapping.Wrap;
            _advancedPanel.Children.Add(_windowsPowerModes);
            _advancedPanel.Children.Add(_windowsPolicyDescription);

            Grid batteryHibernateHeader = new Grid();
            batteryHibernateHeader.Margin = new Thickness(0, 13, 0, 0);
            batteryHibernateHeader.ColumnDefinitions.Add(new ColumnDefinition());
            batteryHibernateHeader.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });
            TextBlock batteryHibernateHeading = Text(
                "HIBERNATE ON BATTERY", 11, FontWeights.SemiBold, _muted);
            batteryHibernateHeading.VerticalAlignment = VerticalAlignment.Center;
            batteryHibernateHeader.Children.Add(batteryHibernateHeading);

            _batteryHibernate = new ToggleButton();
            _batteryHibernate.Content = "After 30 min";
            _batteryHibernate.Height = 29;
            _batteryHibernate.Padding = new Thickness(10, 0, 10, 1);
            _batteryHibernate.FontSize = 11;
            _batteryHibernate.FontWeight = FontWeights.SemiBold;
            _batteryHibernate.Foreground = _text;
            _batteryHibernate.Background = Brush("#202733");
            _batteryHibernate.BorderBrush = _cardBorder;
            _batteryHibernate.BorderThickness = new Thickness(1);
            _batteryHibernate.Cursor = Cursors.Hand;
            _batteryHibernate.Template = CreateToggleButtonTemplate(9);
            _batteryHibernate.ToolTip =
                "Set only the unplugged hibernate timeout; plugged-in policy is unchanged";
            AutomationProperties.SetName(
                _batteryHibernate, "Hibernate after 30 minutes on battery");
            _batteryHibernate.Click += BatteryHibernateClicked;
            Grid.SetColumn(_batteryHibernate, 1);
            batteryHibernateHeader.Children.Add(_batteryHibernate);
            _advancedPanel.Children.Add(batteryHibernateHeader);

            _batteryHibernateStatus = Text(
                "Reading Windows battery policy…", 10, FontWeights.Normal, _muted);
            _batteryHibernateStatus.Margin = new Thickness(0, 5, 0, 0);
            _batteryHibernateStatus.TextWrapping = TextWrapping.Wrap;
            _advancedPanel.Children.Add(_batteryHibernateStatus);

            Grid standbyNetworkHeader = new Grid();
            standbyNetworkHeader.Margin = new Thickness(0, 13, 0, 0);
            standbyNetworkHeader.ColumnDefinitions.Add(new ColumnDefinition());
            standbyNetworkHeader.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });
            TextBlock standbyNetworkHeading = Text(
                "MODERN STANDBY NETWORK", 11, FontWeights.SemiBold, _muted);
            standbyNetworkHeading.VerticalAlignment = VerticalAlignment.Center;
            standbyNetworkHeader.Children.Add(standbyNetworkHeading);

            _standbyNetwork = new ToggleButton();
            _standbyNetwork.Content = "Disconnect";
            _standbyNetwork.Height = 29;
            _standbyNetwork.Padding = new Thickness(10, 0, 10, 1);
            _standbyNetwork.FontSize = 11;
            _standbyNetwork.FontWeight = FontWeights.SemiBold;
            _standbyNetwork.Foreground = _text;
            _standbyNetwork.Background = Brush("#202733");
            _standbyNetwork.BorderBrush = _cardBorder;
            _standbyNetwork.BorderThickness = new Thickness(1);
            _standbyNetwork.Cursor = Cursors.Hand;
            _standbyNetwork.Template = CreateToggleButtonTemplate(9);
            _standbyNetwork.ToolTip =
                "Disconnect networking only during unplugged Modern Standby";
            AutomationProperties.SetName(
                _standbyNetwork, "Disconnect network during battery standby");
            _standbyNetwork.Click += StandbyNetworkClicked;
            Grid.SetColumn(_standbyNetwork, 1);
            standbyNetworkHeader.Children.Add(_standbyNetwork);
            _advancedPanel.Children.Add(standbyNetworkHeader);

            _standbyNetworkStatus = Text(
                "Reading Windows standby policy…", 10, FontWeights.Normal, _muted);
            _standbyNetworkStatus.Margin = new Thickness(0, 5, 0, 0);
            _standbyNetworkStatus.TextWrapping = TextWrapping.Wrap;
            _advancedPanel.Children.Add(_standbyNetworkStatus);
            UpdatePowerProfileDisplay();
            UpdateAutomationVisuals();

            body.Children.Add(batteryPage);
            _touchpadPage = BuildTouchpadPage();
            body.Children.Add(_touchpadPage);
            SelectPanelTab("battery");

            return shell;
        }

        private ToggleButton CreatePanelTab(string label, string tag)
        {
            ToggleButton button = new ToggleButton();
            button.Content = label;
            button.Tag = tag;
            button.Height = 40;
            button.Margin = new Thickness(0);
            button.Padding = new Thickness(10, 0, 10, 2);
            button.FontSize = 12;
            button.FontWeight = FontWeights.SemiBold;
            button.BorderThickness = new Thickness(0);
            button.Cursor = Cursors.Hand;
            button.Template = CreatePanelTabTemplate();
            button.Click += PanelTabClicked;
            return button;
        }

        private async void PanelTabClicked(object sender, RoutedEventArgs e)
        {
            ToggleButton button = sender as ToggleButton;
            if (button == null) return;
            SelectPanelTab(Convert.ToString(button.Tag));
            UpdateLayout();
            PositionBottomRight();
            if (button == _touchpadTab) await RefreshTouchpadAsync(false);
        }

        private void SelectPanelTab(string tab)
        {
            bool touchpad = String.Equals(tab, "touchpad", StringComparison.Ordinal);
            if (_batteryPage != null)
                _batteryPage.Visibility = touchpad ? Visibility.Collapsed : Visibility.Visible;
            if (_touchpadPage != null)
                _touchpadPage.Visibility = touchpad ? Visibility.Visible : Visibility.Collapsed;
            SetTabDisplay(_batteryTab, !touchpad);
            SetTabDisplay(_touchpadTab, touchpad);
        }

        private void SetTabDisplay(ToggleButton button, bool selected)
        {
            if (button == null) return;
            button.IsChecked = selected;
            button.Background = selected ? _pageSurface : Brushes.Transparent;
            button.Foreground = selected ? _accent : _muted;
            button.BorderBrush = selected ? _accent : Brushes.Transparent;
        }

        private Border BuildTouchpadPage()
        {
            Border page = TabPageSurface();
            Border card = Card();
            page.Child = card;
            StackPanel panel = new StackPanel();
            card.Child = panel;

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock heading = Text("ACTIVITY INDICATOR", 11, FontWeights.SemiBold, _muted);
            heading.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(heading);
            _touchpadGlobal = CreateLightingToggle("Enabled");
            _touchpadGlobal.ToolTip = "Enable or disable Acer's global touchpad light switch";
            _touchpadGlobal.Click += TouchpadGlobalClicked;
            Grid.SetColumn(_touchpadGlobal, 1);
            header.Children.Add(_touchpadGlobal);
            panel.Children.Add(header);

            _touchpadStatus = Text(
                "Reading Acer lighting service…", 10, FontWeights.Normal, _muted);
            _touchpadStatus.Margin = new Thickness(0, 6, 0, 0);
            _touchpadStatus.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(_touchpadStatus);

            panel.Children.Add(CreateLightingSectionHeader(
                "CODEX COMPLETION", "Play an effect when an agent turn completes",
                out _codexLighting, LightingOptionClicked));
            TextBlock codexDescription = Text(
                "Uses Codex's supported agent-turn-complete notify hook. " +
                "Choosing an effect previews it immediately.",
                10, FontWeights.Normal, _muted);
            codexDescription.Margin = new Thickness(0, 5, 0, 0);
            panel.Children.Add(codexDescription);
            _codexEffects = CreateEffectPicker(_lightingSettings.CodexEffect);
            panel.Children.Add(_codexEffects);

            TextBlock previewHeading = Text("PREVIEW", 11, FontWeights.SemiBold, _muted);
            previewHeading.Margin = new Thickness(0, 16, 0, 0);
            panel.Children.Add(previewHeading);
            UniformGrid previews = new UniformGrid { Rows = 1 };
            previews.Margin = new Thickness(0, 8, 0, 0);
            Button previewCodex = CreatePreviewButton("Replay selected", "codex");
            Button stop = CreatePreviewButton("Stop", "stop");
            stop.Background = Brush("#3A2028");
            stop.BorderBrush = Brush("#79404C");
            previews.Children.Add(previewCodex);
            previews.Children.Add(stop);
            panel.Children.Add(previews);

            UpdateLightingSettingsVisuals();
            return page;
        }

        private Grid CreateLightingSectionHeader(
            string title, string tooltip, out ToggleButton toggle,
            RoutedEventHandler handler)
        {
            Grid header = new Grid();
            header.Margin = new Thickness(0, 16, 0, 0);
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock heading = Text(title, 11, FontWeights.SemiBold, _muted);
            heading.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(heading);
            toggle = CreateLightingToggle("On");
            toggle.ToolTip = tooltip;
            toggle.Click += handler;
            Grid.SetColumn(toggle, 1);
            header.Children.Add(toggle);
            return header;
        }

        private ToggleButton CreateLightingToggle(string content)
        {
            ToggleButton toggle = new ToggleButton();
            toggle.Content = content;
            toggle.Height = 29;
            toggle.Padding = new Thickness(11, 0, 11, 1);
            toggle.FontSize = 11;
            toggle.FontWeight = FontWeights.SemiBold;
            toggle.BorderThickness = new Thickness(1);
            toggle.Cursor = Cursors.Hand;
            toggle.Template = CreateToggleButtonTemplate(9);
            return toggle;
        }

        private UniformGrid CreateEffectPicker(string selectedEffect)
        {
            UniformGrid picker = new UniformGrid { Rows = 1 };
            picker.Margin = new Thickness(0, 8, 0, 0);
            string[] effects =
            {
                AcerLightingEffects.Blink,
                AcerLightingEffects.Breath,
                AcerLightingEffects.Circle,
                AcerLightingEffects.Twinkle
            };
            string[] labels = { "Blink", "Breath", "Circle", "Twinkle" };
            for (int index = 0; index < effects.Length; index++)
            {
                ToggleButton button = new ToggleButton();
                button.Content = labels[index];
                button.Tag = effects[index];
                button.Height = 34;
                button.FontSize = 11;
                button.FontWeight = FontWeights.SemiBold;
                button.BorderThickness = new Thickness(1);
                button.Margin = new Thickness(index == 0 ? 0 : 3, 0,
                    index == effects.Length - 1 ? 0 : 3, 0);
                button.Cursor = Cursors.Hand;
                button.Template = CreateToggleButtonTemplate(8);
                button.Click += LightingEffectClicked;
                picker.Children.Add(button);
            }
            return picker;
        }

        private Button CreatePreviewButton(string label, string target)
        {
            Button button = Button(label);
            button.Tag = target;
            button.Height = 34;
            button.Margin = new Thickness(3, 0, 3, 0);
            button.FontSize = 11;
            button.FontWeight = FontWeights.SemiBold;
            button.Cursor = Cursors.Hand;
            button.Click += LightingPreviewClicked;
            return button;
        }

        private void LightingOptionClicked(object sender, RoutedEventArgs e)
        {
            _lightingSettings.CodexEnabled = _codexLighting.IsChecked == true;
            try
            {
                _lightingSettings.Save();
            }
            catch (Exception exception)
            {
                ReportFailure("Could not save touchpad-light settings: " + exception.Message);
            }
            UpdateLightingSettingsVisuals();
        }

        private async void LightingEffectClicked(object sender, RoutedEventArgs e)
        {
            ToggleButton button = sender as ToggleButton;
            if (button == null) return;
            _lightingSettings.CodexEffect = Convert.ToString(button.Tag);
            try { _lightingSettings.Save(); }
            catch (Exception exception)
            {
                ReportFailure("Could not save touchpad-light effect: " + exception.Message);
            }
            UpdateLightingSettingsVisuals();
            await PreviewLightingAsync(_lightingSettings.CodexEffect);
        }

        private void UpdateLightingSettingsVisuals()
        {
            SetLightingToggleDisplay(_codexLighting, _lightingSettings.CodexEnabled);
            SetEffectPickerDisplay(_codexEffects, _lightingSettings.CodexEffect);
        }

        private void SetLightingToggleDisplay(ToggleButton toggle, bool enabled)
        {
            if (toggle == null) return;
            toggle.IsChecked = enabled;
            toggle.Content = enabled ? "On" : "Off";
            toggle.Background = enabled ? _accent : Brush("#202733");
            toggle.Foreground = enabled ? _background : _text;
            toggle.BorderBrush = enabled ? _accent : _cardBorder;
        }

        private void SetEffectPickerDisplay(UniformGrid picker, string selectedEffect)
        {
            if (picker == null) return;
            foreach (UIElement element in picker.Children)
            {
                ToggleButton button = element as ToggleButton;
                if (button == null) continue;
                string tag = Convert.ToString(button.Tag);
                bool selected = String.Equals(tag, selectedEffect, StringComparison.Ordinal);
                button.IsChecked = selected;
                button.Background = selected ? _accent : Brush("#202733");
                button.Foreground = selected ? _background : _text;
                button.BorderBrush = selected ? _accent : _cardBorder;
            }
        }

        private async void TouchpadGlobalClicked(object sender, RoutedEventArgs e)
        {
            if (_changingTouchpadGlobal) return;
            bool enabled = _touchpadGlobal.IsChecked == true;
            Exception failure = null;
            _changingTouchpadGlobal = true;
            _touchpadGlobal.IsEnabled = false;
            try
            {
                Tuple<int, bool> status = await Task.Run(new Func<Tuple<int, bool>>(delegate
                {
                    using (AcerLightingClient lighting = new AcerLightingClient())
                    {
                        lighting.Connect();
                        lighting.SetEnabled(5, enabled);
                        return Tuple.Create(lighting.Port, lighting.GetEnabled(5));
                    }
                }));
                SetTouchpadGlobalDisplay(status.Item2, status.Item1);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                _changingTouchpadGlobal = false;
                _touchpadGlobal.IsEnabled = true;
            }
            if (failure != null)
            {
                await RefreshTouchpadAsync(false);
                ReportFailure("Could not change the touchpad light: " + failure.Message);
            }
        }

        private async Task RefreshTouchpadAsync(bool reportFailure)
        {
            if (_touchpadGlobal == null || _changingTouchpadGlobal ||
                _refreshingTouchpad) return;
            _refreshingTouchpad = true;
            try
            {
                Tuple<int, bool> status = await Task.Run(new Func<Tuple<int, bool>>(delegate
                {
                    using (AcerLightingClient lighting = new AcerLightingClient())
                    {
                        lighting.Connect();
                        int[] devices = lighting.GetDevices();
                        bool found = Array.IndexOf(devices, 5) >= 0;
                        if (!found)
                            throw new InvalidOperationException("Touchpad lighting device 5 was not found.");
                        return Tuple.Create(lighting.Port, lighting.GetEnabled(5));
                    }
                }));
                SetTouchpadGlobalDisplay(status.Item2, status.Item1);
            }
            catch (Exception exception)
            {
                _touchpadStatus.Text = "Acer touchpad lighting is unavailable.";
                _touchpadGlobal.IsEnabled = false;
                if (reportFailure)
                    ReportFailure("Could not read the touchpad light: " + exception.Message);
            }
            finally
            {
                _refreshingTouchpad = false;
            }
        }

        private void SetTouchpadGlobalDisplay(bool enabled, int port)
        {
            _touchpadGlobal.IsEnabled = true;
            _touchpadGlobal.IsChecked = enabled;
            _touchpadGlobal.Content = enabled ? "Enabled" : "Disabled";
            _touchpadGlobal.Background = enabled ? _accent : Brush("#202733");
            _touchpadGlobal.Foreground = enabled ? _background : _text;
            _touchpadGlobal.BorderBrush = enabled ? _accent : _cardBorder;
            _touchpadStatus.Text = "Connected on localhost:" +
                port.ToString(CultureInfo.InvariantCulture) +
                ". Touchpad activity indicator is " + (enabled ? "enabled." : "disabled.");
        }

        private async void LightingPreviewClicked(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            string target = button == null ? "" : Convert.ToString(button.Tag);
            if (target == "stop")
            {
                int generation = Interlocked.Increment(ref _lightingPreviewGeneration);
                await TerminateLightingPreviewAsync(generation);
                return;
            }
            await PreviewLightingAsync(_lightingSettings.CodexEffect);
        }

        private async Task PreviewLightingAsync(string effect)
        {
            int generation = Interlocked.Increment(ref _lightingPreviewGeneration);
            Exception failure = null;
            try
            {
                _touchpadStatus.Text = "Previewing " + effect.Replace("_R", "") + "…";
                await Task.Run(new Action(delegate
                {
                    lock (AcerLightingClient.Synchronization)
                    {
                        if (generation != Volatile.Read(ref _lightingPreviewGeneration))
                            return;
                        using (AcerLightingClient lighting = new AcerLightingClient())
                        {
                            lighting.Connect();
                            // Acer's animation engine does not reliably replace a
                            // running effect. Stop the prior effect before every
                            // preview so rapidly cycling choices cannot leave the
                            // first animation playing.
                            lighting.TerminateEffect();
                            Thread.Sleep(75);
                            if (generation != Volatile.Read(ref _lightingPreviewGeneration))
                                return;
                            lighting.PlayEffect(effect);
                        }
                    }
                }));
                await Task.Delay(TouchpadLightingSettings.PreviewDuration(effect));
                if (generation != _lightingPreviewGeneration) return;
                await TerminateLightingPreviewAsync(generation);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            if (failure != null)
            {
                ReportFailure("Touchpad-light preview failed: " + failure.Message);
                await RefreshTouchpadAsync(false);
            }
        }

        private async Task TerminateLightingPreviewAsync(int generation)
        {
            try
            {
                await Task.Run(new Action(delegate
                {
                    lock (AcerLightingClient.Synchronization)
                    {
                        if (generation != Volatile.Read(ref _lightingPreviewGeneration))
                            return;
                        using (AcerLightingClient lighting = new AcerLightingClient())
                        {
                            lighting.Connect();
                            lighting.TerminateEffect();
                        }
                    }
                }));
                if (generation == _lightingPreviewGeneration)
                    await RefreshTouchpadAsync(false);
            }
            catch (Exception exception)
            {
                ReportFailure("Could not stop the touchpad-light effect: " + exception.Message);
            }
        }

        private StackPanel QuickProfileContent(PowerProfileOption profile)
        {
            StackPanel content = new StackPanel();
            content.VerticalAlignment = VerticalAlignment.Center;
            TextBlock name = Text(profile.Name, 11, FontWeights.SemiBold, _text);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(name);
            TextBlock caption = Text(profile.Caption, 10, FontWeights.Normal, _muted);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.Margin = new Thickness(0, 2, 0, 0);
            content.Children.Add(caption);
            TextBlock assignments = Text("", 10, FontWeights.SemiBold, _accent);
            assignments.HorizontalAlignment = HorizontalAlignment.Center;
            assignments.Margin = new Thickness(0, 4, 0, 0);
            assignments.ToolTip = "Automatic conditions assigned to this profile";
            content.Children.Add(assignments);
            return content;
        }

        private ToggleButton CreateConditionButton(int condition, string icon, string label)
        {
            ToggleButton button = new ToggleButton();
            StackPanel content = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock iconText = new TextBlock
            {
                Text = icon,
                FontFamily = new FontFamily(condition == 0
                    ? "Segoe UI Symbol" : "Segoe MDL2 Assets"),
                FontSize = condition == 0 ? 13 : 14,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(iconText);
            TextBlock labelText = new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(labelText);
            button.Content = content;
            button.Tag = condition;
            button.Height = 29;
            button.Padding = new Thickness(8, 0, 8, 1);
            button.Margin = new Thickness(0, 0, 5, 0);
            button.Foreground = _text;
            button.Background = Brush("#202733");
            button.BorderBrush = _cardBorder;
            button.BorderThickness = new Thickness(1);
            button.Cursor = Cursors.Hand;
            button.Template = CreateToggleButtonTemplate(9);
            button.ToolTip = "Select, then click a profile; or drag onto a profile";
            AutomationProperties.SetName(button, "Assign " + ConditionName(condition));
            button.Click += ConditionButtonClicked;
            button.PreviewMouseLeftButtonDown += ConditionMouseDown;
            button.MouseMove += ConditionMouseMove;
            return button;
        }

        private static ControlTemplate CreateContentOnlyToggleTemplate()
        {
            ControlTemplate template = new ControlTemplate(typeof(ToggleButton));
            FrameworkElementFactory presenter =
                new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            template.VisualTree = presenter;
            return template;
        }

        private void AdvancedToggleClicked(object sender, RoutedEventArgs e)
        {
            bool expanded = _advancedToggle.IsChecked == true;
            _advancedPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            _advancedToggle.Content = expanded ? "Advanced controls  ▴" : "Advanced controls  ▾";
            UpdateLayout();
            PositionBottomRight();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            SelectMonitorAtCursor();
            UpdateLayout();
            PositionBottomRight();
            RefreshStartupDisplay();
            await RefreshVisiblePanelAsync(true);
            UpdateAcerSenseCloseButton();
            _modePoll.Start();
            _dashboardPoll.Start();
        }

        private void PanelSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // SizeToContent can change the panel after refreshed controls are
            // populated. Keep its bottom edge anchored instead of allowing the
            // newly added content to grow below the screen.
            if (IsVisible && !_positioning) PositionBottomRight();
        }

        private void PanelLocationChanged(object sender, EventArgs e)
        {
            if (_positioning || _suppressMonitorTracking) return;
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
                _targetMonitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        }

        public async void StartHidden()
        {
            // Create the invisible top-level HWND so registered Modern Standby
            // notifications work even if the panel is never opened.
            new WindowInteropHelper(this).EnsureHandle();
            RefreshStartupDisplay();
            _modePoll.Interval = TimeSpan.FromSeconds(30);
            DateTime deadline = DateTime.UtcNow.AddSeconds(StartupServiceWaitSeconds);
            bool ready = false;
            do
            {
                ready = await RefreshTrayStateAsync(false, false);
                if (ready) break;
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) break;
                TimeSpan delay = TimeSpan.FromSeconds(StartupServiceRetrySeconds);
                if (delay > remaining) delay = remaining;
                await Task.Delay(delay);
            }
            while (DateTime.UtcNow < deadline);

            if (ready)
            {
                await EvaluateAutomationAsync(true, true);
            }
            else
            {
                ReportFailure(
                    "Acer services were still unavailable 90 seconds after sign-in. " +
                    "Open SwiftControl to retry.");
            }
            _modePoll.Start();
        }

        private async void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                _modePoll.Interval = TimeSpan.FromSeconds(5);
                _modePoll.Start();
                _dashboardPoll.Start();
                if (IsLoaded)
                {
                    UpdateAcerSenseCloseButton();
                    await RefreshVisiblePanelAsync(false);
                }
            }
            else
            {
                CloseTrustedWifiManager();
                _modePoll.Interval = TimeSpan.FromSeconds(30);
                if (IsLoaded) _modePoll.Start();
                _dashboardPoll.Stop();
            }
        }

        private async Task RefreshVisiblePanelAsync(bool reportFailure)
        {
            _visibleRefreshRequested = true;
            if (_visibleRefreshRunning) return;

            _visibleRefreshRunning = true;
            try
            {
                while (_visibleRefreshRequested && IsVisible)
                {
                    _visibleRefreshRequested = false;
                    await RefreshAsync(reportFailure, false);
                    if (!IsVisible) break;
                    await RefreshWindowsPowerModeAsync(reportFailure);
                    if (!IsVisible) break;
                    await RefreshBatteryHibernateAsync(reportFailure);
                    if (!IsVisible) break;
                    await RefreshStandbyNetworkAsync(reportFailure);
                    if (!IsVisible) break;
                    RefreshLockAfterSuspendDisplay();
                    if (!IsVisible) break;
                    if (_touchpadPage != null &&
                        _touchpadPage.Visibility == Visibility.Visible)
                        await RefreshTouchpadAsync(reportFailure);
                    if (!IsVisible) break;
                    await EvaluateAutomationAsync(true, true);
                }
            }
            finally
            {
                _visibleRefreshRunning = false;
            }
        }

        private async void ModePollTick(object sender, EventArgs e)
        {
            UpdateAcerSenseCloseButton();
            await RefreshPowerModeAsync();
        }

        private async void DashboardPollTick(object sender, EventArgs e)
        {
            if (IsVisible)
            {
                await RefreshAsync(false, false);
                RefreshLockAfterSuspendDisplay();
            }
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
            await RefreshWindowsPowerModeAsync(false);
            await EvaluateAutomationAsync(false, false);
        }

        private void SystemPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend)
            {
                CaptureLockAfterSuspendDecision();
                return;
            }
            if (e.Mode == PowerModes.Resume)
            {
                ApplyLockAfterResume();
                return;
            }
            if (e.Mode != PowerModes.StatusChange) return;
            try
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    EvaluateAutomationFromPowerEvent();
                }));
            }
            catch (InvalidOperationException) { }
        }

        private void CaptureLockAfterSuspendDecision()
        {
            _lockOnNextResume = _lockSettings.EvaluateCurrent().ShouldLock;
        }

        private void ApplyLockAfterResume()
        {
            bool shouldLock = _lockOnNextResume;
            _lockOnNextResume = false;
            if (!shouldLock) return;
            try { LockWorkStation(); }
            catch { }
        }

        private async void EvaluateAutomationFromPowerEvent()
        {
            await RefreshWindowsPowerModeAsync(false);
            await EvaluateAutomationAsync(false, false);
        }

        public void ShowFromTray()
        {
            // Initial WPF placement can raise LocationChanged before our first
            // explicit position. Preserve the monitor where the tray action
            // occurred instead of replacing it with those default coordinates.
            _suppressMonitorTracking = true;
            try
            {
                SelectMonitorAtCursor();
                if (!IsVisible) Show();
                if (WindowState == WindowState.Minimized)
                    WindowState = WindowState.Normal;
                UpdateLayout();
                PositionBottomRight();
            }
            finally
            {
                _suppressMonitorTracking = false;
            }
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void PanelDeactivated(object sender, EventArgs e)
        {
            if (_allowClose || !IsVisible || _openingTrustedWifiWindow) return;
            QueueFocusDismiss();
        }

        private void QueueFocusDismiss()
        {
            if (_focusDismissQueued || _openingTrustedWifiWindow) return;
            _focusDismissQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(delegate
                {
                    _focusDismissQueued = false;
                    if (_allowClose || !IsVisible || IsActive ||
                        _openingTrustedWifiWindow) return;
                    if (_trustedWifiWindow != null && _trustedWifiWindow.IsActive) return;
                    CloseTrustedWifiManager();
                    Hide();
                }));
        }

        private void CloseTrustedWifiManager()
        {
            TrustedWifiWindow manager = _trustedWifiWindow;
            _trustedWifiWindow = null;
            if (manager != null)
            {
                manager.FocusLeft -= QueueFocusDismiss;
                manager.Close();
            }
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
            _dashboardPoll.Stop();
            CloseTrustedWifiManager();
            SystemEvents.PowerModeChanged -= SystemPowerModeChanged;
            base.OnClosing(e);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            try
            {
                int preference = DwmWindowCornerRound;
                DwmSetWindowAttribute(handle, DwmWindowCornerPreference,
                    ref preference, Marshal.SizeOf(typeof(int)));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            _windowSource = HwndSource.FromHwnd(handle);
            if (_windowSource != null) _windowSource.AddHook(WindowMessageHook);
            try
            {
                PowerRegisterSuspendResumeNotification(
                    handle, 0, out _suspendResumeRegistration);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_windowSource != null)
            {
                _windowSource.RemoveHook(WindowMessageHook);
                _windowSource = null;
            }
            if (_suspendResumeRegistration != IntPtr.Zero)
            {
                try
                {
                    PowerUnregisterSuspendResumeNotification(
                        _suspendResumeRegistration);
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                _suspendResumeRegistration = IntPtr.Zero;
            }
            base.OnClosed(e);
        }

        private IntPtr WindowMessageHook(IntPtr window, int message,
            IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WmDpiChanged)
            {
                _targetMonitor = MonitorFromWindow(window, MonitorDefaultToNearest);
                QueueReposition();
            }
            else if (message == WmDisplayChange || message == WmSettingChange)
            {
                _targetMonitor = MonitorFromWindow(window, MonitorDefaultToNearest);
                QueueReposition();
            }
            else if (message == WmPowerBroadcast)
            {
                int powerEvent = wParam.ToInt32();
                if (powerEvent == PbtApmSuspend)
                    CaptureLockAfterSuspendDecision();
                else if (powerEvent == PbtApmResumeSuspend ||
                    powerEvent == PbtApmResumeAutomatic)
                    ApplyLockAfterResume();
            }
            return IntPtr.Zero;
        }

        private void QueueReposition()
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(delegate
                {
                    if (IsVisible) PositionBottomRight();
                }));
        }

        private void SelectMonitorAtCursor()
        {
            NativePoint cursor;
            if (GetCursorPos(out cursor))
                _targetMonitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        }

        private void PositionBottomRight()
        {
            const double edgePadding = 12;
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            IntPtr monitor = _targetMonitor;
            if (monitor == IntPtr.Zero)
                monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);

            MonitorInfo monitorInfo = new MonitorInfo();
            monitorInfo.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo)) return;

            uint dpi = DpiForMonitor(monitor, handle);
            double pixelsPerDip = dpi / 96.0;
            double areaWidth = (monitorInfo.Monitor.Right - monitorInfo.Monitor.Left) / pixelsPerDip;
            double areaHeight =
                (monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top) / pixelsPerDip -
                TaskbarReserve;

            _positioning = true;
            try
            {
                // Keep the compact flyout proportions on ordinary displays while
                // retaining the half-screen layout used by the highly scaled TV.
                double compactWidth = areaWidth <= 1440
                    ? Math.Round(areaWidth * 0.5)
                    : 680;
                Width = Math.Max(MinWidth,
                    Math.Min(compactWidth, areaWidth - (edgePadding * 2)));
                MaxHeight = Math.Max(MinHeight,
                    areaHeight - (edgePadding * 2));

                UpdateLayout();

                int width = Math.Max(1, (int)Math.Round(ActualWidth * pixelsPerDip));
                int height = Math.Max(1, (int)Math.Round(ActualHeight * pixelsPerDip));
                int padding = Math.Max(1, (int)Math.Round(edgePadding * pixelsPerDip));
                int taskbarReserve = (int)Math.Round(TaskbarReserve * pixelsPerDip);
                int left = monitorInfo.Monitor.Right - width - padding;
                int top = monitorInfo.Monitor.Bottom - taskbarReserve - height - padding;

                // SetWindowPos consumes physical desktop coordinates. This avoids
                // WPF interpreting Left and Top through the previous monitor's DPI
                // during a per-monitor scale transition.
                SetWindowPos(handle, IntPtr.Zero, left, top, width, height,
                    SwpNoZOrder | SwpNoActivate);
            }
            finally
            {
                _positioning = false;
            }
        }

        private static uint DpiForMonitor(IntPtr monitor, IntPtr window)
        {
            try
            {
                uint horizontal;
                uint vertical;
                if (GetDpiForMonitor(monitor, 0, out horizontal, out vertical) == 0 &&
                    horizontal > 0)
                    return horizontal;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            try
            {
                uint dpi = GetDpiForWindow(window);
                if (dpi > 0) return dpi;
            }
            catch (EntryPointNotFoundException) { }
            return 96;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public int Flags;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(
            NativePoint point, int flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(
            IntPtr window, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(
            IntPtr monitor, ref MonitorInfo info);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(
            IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window,
            int attribute, ref int value, int valueSize);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();

        [DllImport("powrprof.dll")]
        private static extern uint PowerRegisterSuspendResumeNotification(
            IntPtr recipient, uint flags, out IntPtr registrationHandle);

        [DllImport("powrprof.dll")]
        private static extern uint PowerUnregisterSuspendResumeNotification(
            IntPtr registrationHandle);

        private async Task RefreshAsync(bool reportFailure = true, bool showBusyState = true)
        {
            if (_loading) return;
            _loading = true;
            if (showBusyState) SetControlsEnabled(false);

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
                if (showBusyState) SetControlsEnabled(true);
            }
        }

        private void ApplySnapshot(DashboardSnapshot snapshot)
        {
            _percent.Text = snapshot.BatteryPercent.ToString(CultureInfo.InvariantCulture) + "%";
            _progress.Value = snapshot.BatteryPercent;
            _onAcPower = snapshot.OnAcPower;
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
            UpdatePowerProfileDisplay();

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

        private void UpdateAcerSenseCloseButton()
        {
            if (_closeAcerSense == null) return;
            bool isOpen = false;
            Process[] processes = Process.GetProcessesByName("AcerSense");
            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        process.Refresh();
                        if (IsAcerSensePackageProcess(process) &&
                            process.MainWindowHandle != IntPtr.Zero &&
                            IsWindowVisible(process.MainWindowHandle))
                        {
                            isOpen = true;
                            break;
                        }
                    }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                }
            }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }
            _closeAcerSense.Visibility = isOpen
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CloseAcerSenseClicked(object sender, RoutedEventArgs e)
        {
            _closeAcerSense.IsEnabled = false;
            int targets = 0;
            int stopped = 0;
            Exception failure = null;
            Process[] processes = Process.GetProcessesByName("AcerSense");
            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        if (!IsAcerSensePackageProcess(process)) continue;
                        targets++;
                        if (!process.HasExited)
                        {
                            process.Kill();
                            stopped++;
                        }
                    }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception exception)
                    {
                        failure = exception;
                    }
                }
            }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }

            _closeAcerSense.IsEnabled = true;
            _closeAcerSense.Visibility = Visibility.Collapsed;
            if (targets > 0 && stopped == 0 && failure != null)
                ReportFailure("Could not close AcerSense: " + failure.Message);
        }

        private static bool IsAcerSensePackageProcess(Process process)
        {
            try
            {
                string path = process.MainModule.FileName;
                return path != null &&
                    path.IndexOf(
                        @"\WindowsApps\ULICTekInc.AcerSense5.0_",
                        StringComparison.OrdinalIgnoreCase) >= 0 &&
                    path.EndsWith(
                        @"\app\AcerSense.exe",
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (InvalidOperationException) { return false; }
            catch (Win32Exception) { return false; }
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

        private async void BatteryHibernateClicked(object sender, RoutedEventArgs e)
        {
            if (_changingBatteryHibernate) return;
            bool enabled = _batteryHibernate.IsChecked == true;
            Exception failure = null;
            _changingBatteryHibernate = true;
            _batteryHibernate.IsEnabled = false;
            try
            {
                BatteryHibernateStatus status = await Task.Run(
                    new Func<BatteryHibernateStatus>(delegate
                    {
                        return BatteryHibernate.SetManaged(enabled);
                    }));
                SetBatteryHibernateDisplay(status);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                _changingBatteryHibernate = false;
                _batteryHibernate.IsEnabled = true;
            }
            if (failure != null)
            {
                await RefreshBatteryHibernateAsync(false);
                ReportFailure("Battery hibernate change failed: " + failure.Message);
            }
        }

        private async Task RefreshBatteryHibernateAsync(bool reportFailure)
        {
            if (_changingBatteryHibernate || _refreshingBatteryHibernate) return;
            _refreshingBatteryHibernate = true;
            try
            {
                BatteryHibernateStatus status = await Task.Run(
                    new Func<BatteryHibernateStatus>(BatteryHibernate.ReadStatus));
                SetBatteryHibernateDisplay(status);
            }
            catch (Exception exception)
            {
                if (_batteryHibernateStatus != null)
                    _batteryHibernateStatus.Text =
                        "Windows battery hibernate policy is unavailable.";
                if (reportFailure)
                    ReportFailure("Could not read the battery hibernate timeout: " +
                        exception.Message);
            }
            finally
            {
                _refreshingBatteryHibernate = false;
            }
        }

        private void SetBatteryHibernateDisplay(BatteryHibernateStatus status)
        {
            if (_batteryHibernate == null || status == null) return;
            _batteryHibernate.IsChecked = status.Managed;
            _batteryHibernate.Background = status.Managed ? _accent : Brush("#202733");
            _batteryHibernate.Foreground = status.Managed ? _background : _text;
            _batteryHibernate.BorderBrush = status.Managed ? _accent : _cardBorder;
            if (_batteryHibernateStatus == null) return;

            string current = BatteryHibernate.FormatTimeout(status.TimeoutSeconds);
            _batteryHibernateStatus.Text = status.Managed
                ? "Battery timeout: 30 min. Plugged-in timeout is unchanged."
                : "Battery timeout: " + current +
                    ". Enable to use 30 min; plugged-in timeout is unchanged.";
        }

        private async void StandbyNetworkClicked(object sender, RoutedEventArgs e)
        {
            if (_changingStandbyNetwork) return;
            bool disconnected = _standbyNetwork.IsChecked == true;
            Exception failure = null;
            _changingStandbyNetwork = true;
            _standbyNetwork.IsEnabled = false;
            try
            {
                ModernStandbyNetworkStatus status = await Task.Run(
                    new Func<ModernStandbyNetworkStatus>(delegate
                    {
                        return ModernStandbyNetwork.SetDisconnected(disconnected);
                    }));
                SetStandbyNetworkDisplay(status);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                _changingStandbyNetwork = false;
                _standbyNetwork.IsEnabled = true;
            }
            if (failure != null)
            {
                await RefreshStandbyNetworkAsync(false);
                ReportFailure("Standby-network change failed: " + failure.Message);
            }
        }

        private async Task RefreshStandbyNetworkAsync(bool reportFailure)
        {
            if (_changingStandbyNetwork || _refreshingStandbyNetwork) return;
            _refreshingStandbyNetwork = true;
            try
            {
                ModernStandbyNetworkStatus status = await Task.Run(
                    new Func<ModernStandbyNetworkStatus>(
                        ModernStandbyNetwork.ReadStatus));
                SetStandbyNetworkDisplay(status);
            }
            catch (Exception exception)
            {
                if (_standbyNetworkStatus != null)
                    _standbyNetworkStatus.Text =
                        "Windows standby-network policy is unavailable.";
                if (reportFailure)
                    ReportFailure("Could not read the standby-network policy: " +
                        exception.Message);
            }
            finally
            {
                _refreshingStandbyNetwork = false;
            }
        }

        private void SetStandbyNetworkDisplay(ModernStandbyNetworkStatus status)
        {
            if (_standbyNetwork == null || status == null) return;
            bool disconnected = status.Disconnected;
            _standbyNetwork.IsChecked = disconnected;
            _standbyNetwork.Background = disconnected ? _accent : Brush("#202733");
            _standbyNetwork.Foreground = disconnected ? _background : _text;
            _standbyNetwork.BorderBrush = disconnected ? _accent : _cardBorder;
            if (_standbyNetworkStatus == null) return;

            _standbyNetworkStatus.Text = "Battery standby: " +
                ModernStandbyNetwork.FormatPolicy(status.BatteryPolicy) +
                ". Plugged in: " +
                ModernStandbyNetwork.FormatPolicy(status.PluggedInPolicy) + ".";
        }

        private void LockAfterSuspendModeClicked(object sender, RoutedEventArgs e)
        {
            ToggleButton button = sender as ToggleButton;
            if (button == null || button.Tag == null) return;
            _lockSettings.Mode = Convert.ToInt32(
                button.Tag, CultureInfo.InvariantCulture);
            SaveLockAfterSuspendSettings();
            RefreshLockAfterSuspendDisplay();
            UpdateLayout();
            PositionBottomRight();
        }

        private void TrustCurrentWifiClicked(object sender, RoutedEventArgs e)
        {
            WifiConnection wifi = WifiNetwork.Current();
            if (wifi == null)
            {
                RefreshLockAfterSuspendDisplay();
                ReportFailure("No connected Wi-Fi is available to trust.");
                return;
            }
            try
            {
                _lockSettings.SetTrusted(wifi, !_lockSettings.IsTrusted(wifi));
                _lockSettings.Save();
            }
            catch (Exception exception)
            {
                ReportFailure("Could not save the trusted Wi-Fi: " + exception.Message);
            }
            RefreshLockAfterSuspendDisplay();
        }

        private void ManageTrustedWifiClicked(object sender, RoutedEventArgs e)
        {
            if (_trustedWifiWindow != null)
            {
                _trustedWifiWindow.RefreshNetworks();
                _trustedWifiWindow.Activate();
                return;
            }

            TrustedWifiWindow manager = new TrustedWifiWindow(
                _lockSettings, delegate { RefreshLockAfterSuspendDisplay(); });
            _trustedWifiWindow = manager;
            manager.Owner = this;
            manager.ShowActivated = true;
            manager.FocusLeft += QueueFocusDismiss;
            manager.Closed += delegate
            {
                manager.FocusLeft -= QueueFocusDismiss;
                if (ReferenceEquals(_trustedWifiWindow, manager))
                    _trustedWifiWindow = null;
            };
            _openingTrustedWifiWindow = true;
            manager.Show();
            manager.PositionNextTo(_manageTrustedWifi, this);
            manager.Activate();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(delegate { _openingTrustedWifiWindow = false; }));
        }

        private void SaveLockAfterSuspendSettings()
        {
            try { _lockSettings.Save(); }
            catch (Exception exception)
            {
                ReportFailure("Could not save the lock-after-suspend setting: " +
                    exception.Message);
            }
        }

        private void RefreshLockAfterSuspendDisplay()
        {
            if (_lockAfterSuspendModes[0] == null) return;
            _currentWifi = WifiNetwork.Current();
            int selectedMode = _lockSettings.Mode;
            bool always = selectedMode == LockAfterSuspendSettings.AlwaysMode;
            bool smart = selectedMode == LockAfterSuspendSettings.SmartMode;
            bool trusted = _lockSettings.IsTrusted(_currentWifi);

            for (int mode = 0; mode < _lockAfterSuspendModes.Length; mode++)
            {
                ToggleButton button = _lockAfterSuspendModes[mode];
                if (button == null) continue;
                bool selected = mode == selectedMode;
                button.IsChecked = selected;
                button.Background = selected ? _accent : Brush("#202733");
                button.Foreground = selected ? _background : _text;
                button.BorderBrush = selected ? _accent : _cardBorder;
            }

            _trustCurrentWifi.Visibility = smart
                ? Visibility.Visible : Visibility.Collapsed;
            _manageTrustedWifi.Visibility = smart
                ? Visibility.Visible : Visibility.Collapsed;
            _trustCurrentWifi.IsEnabled = _currentWifi != null;
            if (_currentWifi != null)
                _trustCurrentWifi.Content = trusted
                    ? "Forget " + _currentWifi.Name
                    : "Trust " + _currentWifi.Name;
            else
                _trustCurrentWifi.Content = "No Wi-Fi connected";

            if (_lockAfterSuspendStatus == null) return;
            if (always)
            {
                _lockAfterSuspendStatus.Text =
                    "Always: the next resume locks.";
            }
            else if (!smart)
            {
                _lockAfterSuspendStatus.Text =
                    "Off: suspend and resume without locking.";
            }
            else if (_currentWifi == null)
            {
                _lockAfterSuspendStatus.Text =
                    "Smart: no connected Wi-Fi is trusted. The next resume locks.";
            }
            else if (trusted)
            {
                _lockAfterSuspendStatus.Text = "Smart: " + _currentWifi.Name +
                    " is trusted. Resume stays unlocked.";
            }
            else
            {
                _lockAfterSuspendStatus.Text = "Smart: " + _currentWifi.Name +
                    " is untrusted. The next resume locks.";
            }
            if (_trustedWifiWindow != null)
                _trustedWifiWindow.RefreshNetworks();
        }

        private async void AutomationEnabledChanged(object sender, RoutedEventArgs e)
        {
            _automationSettings.Enabled = _automationEnabled.IsChecked == true;
            if (!_automationSettings.Enabled) _selectedAutomationCondition = -1;
            UpdateAutomationVisuals();
            UpdateLayout();
            PositionBottomRight();
            await SaveAutomationSettingsAsync();
        }

        private void ConditionButtonClicked(object sender, RoutedEventArgs e)
        {
            if (_conditionDragCompleted)
            {
                _conditionDragCompleted = false;
                UpdateAutomationVisuals();
                return;
            }

            ToggleButton button = sender as ToggleButton;
            if (button == null || button.Tag == null) return;
            int condition = Convert.ToInt32(button.Tag, CultureInfo.InvariantCulture);
            _selectedAutomationCondition = button.IsChecked == true
                ? condition : -1;
            UpdateAutomationVisuals();
        }

        private void LowBatteryThresholdMouseDown(object sender, MouseButtonEventArgs e)
        {
            _selectedAutomationCondition = 2;
            UpdateAutomationVisuals();
        }

        private void ConditionMouseDown(object sender, MouseButtonEventArgs e)
        {
            ToggleButton button = sender as ToggleButton;
            if (button == null || button.Tag == null) return;
            _dragCondition = Convert.ToInt32(button.Tag, CultureInfo.InvariantCulture);
            _conditionDragStart = e.GetPosition(this);
            _conditionDragCompleted = false;
        }

        private void ConditionMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragCondition < 0 || e.LeftButton != MouseButtonState.Pressed) return;
            Point current = e.GetPosition(this);
            if (Math.Abs(current.X - _conditionDragStart.X) <
                    SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - _conditionDragStart.Y) <
                    SystemParameters.MinimumVerticalDragDistance) return;

            int condition = _dragCondition;
            _dragCondition = -1;
            _conditionDragCompleted = true;
            DataObject data = new DataObject("SwiftControl.PowerCondition", condition);
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
            UpdateAutomationVisuals();
        }

        private void ProfileDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("SwiftControl.PowerCondition")) return;
            ToggleButton button = sender as ToggleButton;
            if (button != null) button.BorderBrush = _accent;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void ProfileDragLeave(object sender, DragEventArgs e)
        {
            UpdatePowerProfileDisplay();
        }

        private async void ProfileDrop(object sender, DragEventArgs e)
        {
            ToggleButton button = sender as ToggleButton;
            PowerProfileOption profile = button == null
                ? null : button.Tag as PowerProfileOption;
            if (profile == null ||
                !e.Data.GetDataPresent("SwiftControl.PowerCondition")) return;
            int condition = Convert.ToInt32(
                e.Data.GetData("SwiftControl.PowerCondition"), CultureInfo.InvariantCulture);
            e.Handled = true;
            await AssignConditionAsync(condition, profile.Value);
        }

        private async Task AssignConditionAsync(int condition, int profileValue)
        {
            if (!PowerProfiles.IsValid(profileValue) || condition < 0 || condition > 2) return;
            _automationSettings.SetProfileForCondition(condition, profileValue);
            _selectedAutomationCondition = -1;
            _dragCondition = -1;
            UpdateAutomationVisuals();
            await SaveAutomationSettingsAsync();
        }

        private async void ResumeAutomationClicked(object sender, RoutedEventArgs e)
        {
            if (!_automationSettings.Enabled) return;
            await EvaluateAutomationAsync(true, true);
        }

        private async void AutomationThresholdLostFocus(
            object sender, KeyboardFocusChangedEventArgs e)
        {
            int value;
            if (!Int32.TryParse(_lowBatteryThreshold.Text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value))
            {
                _lowBatteryThreshold.Text = _automationSettings.LowBatteryThreshold
                    .ToString(CultureInfo.InvariantCulture);
                return;
            }

            value = PowerAutomationSettings.ValidThreshold(value);
            _lowBatteryThreshold.Text = value.ToString(CultureInfo.InvariantCulture);
            if (value == _automationSettings.LowBatteryThreshold) return;
            _automationSettings.LowBatteryThreshold = value;
            UpdateAutomationVisuals();
            await SaveAutomationSettingsAsync();
        }

        private void AutomationThresholdKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _automationEnabled.Focus();
            e.Handled = true;
        }

        private async Task SaveAutomationSettingsAsync()
        {
            try
            {
                _automationSettings.Save();
                _lastAutomationCondition = -1;
                UpdateAutomationVisuals();
                if (_automationSettings.Enabled)
                    await EvaluateAutomationAsync(true, true);
            }
            catch (Exception exception)
            {
                ReportFailure("Could not save automatic power modes: " + exception.Message);
            }
        }

        private async Task EvaluateAutomationAsync(bool force, bool reportFailure)
        {
            if (!_automationSettings.Enabled || _automationApplying || _loading ||
                _trayActionPending || _currentPowerMode < 0 ||
                _currentWindowsPowerMode < 0) return;

            _automationApplying = true;
            try
            {
                PowerSourceSnapshot source = await Task.Run(new Func<PowerSourceSnapshot>(
                    DashboardReader.ReadPowerSource));
                if (_loading || _trayActionPending) return;

                if (source.BatteryPercent >= 0)
                {
                    _percent.Text = source.BatteryPercent.ToString(CultureInfo.InvariantCulture) + "%";
                    _progress.Value = source.BatteryPercent;
                }
                _powerState.Text = source.OnAcPower ? "Plugged in" : "On battery";
                _onAcPower = source.OnAcPower;
                UpdatePowerProfileDisplay();

                int condition = _automationSettings.ConditionFor(
                    source.OnAcPower, source.BatteryPercent, _lastAutomationCondition);
                if (!force && condition == _lastAutomationCondition)
                {
                    UpdateAutomationVisuals();
                    return;
                }

                _lastAutomationCondition = condition;
                UpdateAutomationVisuals();
                PowerProfileOption profile = _automationSettings.ProfileForCondition(condition);
                if (profile.AcerMode == _currentPowerMode &&
                    profile.WindowsMode == _currentWindowsPowerMode) return;
                await ApplyPowerProfileAsync(profile, true, "Automatic profile");
            }
            catch (Exception exception)
            {
                if (reportFailure)
                    ReportFailure("Could not evaluate automatic power mode: " + exception.Message);
            }
            finally
            {
                _automationApplying = false;
                UpdateAutomationVisuals();
            }
        }

        private void UpdateAutomationStatus(int condition)
        {
            if (_automationStatus == null) return;
            if (_resumeAutomation != null)
                _resumeAutomation.Visibility = Visibility.Collapsed;

            if (_selectedAutomationCondition >= 0)
            {
                _automationStatus.Text = "Choose a profile for " +
                    ConditionName(_selectedAutomationCondition) +
                    ", or drag its chip onto one.";
                return;
            }

            if (!_automationSettings.Enabled)
            {
                _automationStatus.Text = "Auto is off. Assignments are saved.";
                return;
            }

            if (condition < 0)
            {
                _automationStatus.Text =
                    "Waiting for power status. Manual changes last until the condition changes.";
                return;
            }

            PowerProfileOption profile = _automationSettings.ProfileForCondition(condition);
            bool expectedActive = profile.AcerMode == _currentPowerMode &&
                profile.WindowsMode == _currentWindowsPowerMode;
            if (!expectedActive && !_automationApplying)
            {
                PowerProfileOption current = PowerProfiles.Match(
                    _currentPowerMode, _currentWindowsPowerMode);
                _automationStatus.Text = "Manual override: " +
                    (current == null ? "Custom" : current.Name) + " · Auto expects " +
                    profile.Name + " for " + ConditionName(condition) + ".";
                if (_resumeAutomation != null)
                    _resumeAutomation.Visibility = Visibility.Visible;
                return;
            }

            _automationStatus.Text = _automationApplying
                ? "Applying " + profile.Name + " for " + ConditionName(condition) + "…"
                : ConditionName(condition) + " → " + profile.Name + " is active.";
            if (condition == 2)
            {
                _automationStatus.Text += " Clears at " +
                    Math.Min(100, _automationSettings.LowBatteryThreshold + 5) + "%.";
            }
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

        private async void QuickProfileClicked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            ToggleButton clicked = sender as ToggleButton;
            PowerProfileOption profile = clicked == null
                ? null : clicked.Tag as PowerProfileOption;
            if (profile == null) return;
            if (_selectedAutomationCondition >= 0)
            {
                await AssignConditionAsync(_selectedAutomationCondition, profile.Value);
                UpdatePowerProfileDisplay();
                return;
            }
            if (profile.AcerMode == _currentPowerMode &&
                profile.WindowsMode == _currentWindowsPowerMode)
            {
                UpdatePowerProfileDisplay();
                return;
            }

            await ApplyPowerProfileAsync(profile, true, "Profile");
        }

        private async Task ApplyPowerProfileAsync(
            PowerProfileOption profile, bool showOsd, string operationName)
        {
            if (_loading || profile == null) return;
            bool refreshAcer = false;
            bool refreshWindows = false;
            System.Collections.Generic.List<string> failures =
                new System.Collections.Generic.List<string>();

            _loading = true;
            _changingWindowsPowerMode = true;
            SetControlsEnabled(false);
            UpdateQuickProfileButtons(profile.Value);
            _profileTitle.Text = "Applying " + profile.Name + "…";
            _profileSummary.Text =
                "Updating the Acer envelope and this power source's Windows policy.";
            try
            {
                PowerSourceSnapshot source = await Task.Run(
                    new Func<PowerSourceSnapshot>(DashboardReader.ReadPowerSource));
                _onAcPower = source.OnAcPower;

                if (_currentPowerMode != profile.AcerMode)
                {
                    try
                    {
                        bool acerVerified = await Task.Run(new Func<bool>(delegate
                        {
                            return DashboardReader.SetPowerMode(profile.AcerMode);
                        }));
                        if (!acerVerified)
                            throw new InvalidOperationException(
                                "the firmware retained a different profile");
                        _currentPowerMode = profile.AcerMode;
                        UpdatePowerModeButtons(_currentPowerMode);
                        NotifyPowerModeObserved(_currentPowerMode);
                    }
                    catch (Exception exception)
                    {
                        failures.Add("Acer: " + exception.Message);
                        refreshAcer = true;
                    }
                }

                if (_currentWindowsPowerMode != profile.WindowsMode)
                {
                    try
                    {
                        bool windowsVerified = await Task.Run(new Func<bool>(delegate
                        {
                            return WindowsPowerMode.Set(source.OnAcPower, profile.WindowsMode);
                        }));
                        if (!windowsVerified)
                            throw new InvalidOperationException(
                                "Windows retained a different policy");
                        _currentWindowsPowerMode = profile.WindowsMode;
                        UpdateWindowsPowerModeButtons(_currentWindowsPowerMode);
                    }
                    catch (Exception exception)
                    {
                        failures.Add("Windows: " + exception.Message);
                        refreshWindows = true;
                    }
                }

                UpdatePowerProfileDisplay();
                if (failures.Count == 0 && showOsd)
                    ModeOsdWindow.Present(profile.Name);
            }
            catch (Exception exception)
            {
                failures.Add(exception.Message);
                refreshAcer = true;
                refreshWindows = true;
            }
            finally
            {
                _changingWindowsPowerMode = false;
                _loading = false;
                SetControlsEnabled(true);
                UpdatePowerProfileDisplay();
            }

            if (failures.Count > 0)
                ReportFailure(operationName + " incomplete: " +
                    String.Join("; ", failures.ToArray()));
            if (refreshAcer) await RefreshAsync(false);
            if (refreshWindows) await RefreshWindowsPowerModeAsync(false);
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

        private async void WindowsPowerModeClicked(object sender, RoutedEventArgs e)
        {
            if (_loading || _changingWindowsPowerMode) return;
            ToggleButton clicked = sender as ToggleButton;
            if (clicked == null || clicked.Tag == null) return;
            int targetMode = Convert.ToInt32(clicked.Tag, CultureInfo.InvariantCulture);
            if (targetMode == _currentWindowsPowerMode)
            {
                UpdateWindowsPowerModeButtons(_currentWindowsPowerMode);
                return;
            }

            int previous = _currentWindowsPowerMode;
            _changingWindowsPowerMode = true;
            _loading = true;
            UpdateWindowsPowerModeButtons(targetMode);
            SetControlsEnabled(false);
            try
            {
                PowerSourceSnapshot source = await Task.Run(
                    new Func<PowerSourceSnapshot>(DashboardReader.ReadPowerSource));
                _onAcPower = source.OnAcPower;
                bool verified = await Task.Run(new Func<bool>(delegate
                {
                    return WindowsPowerMode.Set(source.OnAcPower, targetMode);
                }));
                if (!verified)
                    throw new InvalidOperationException("Windows did not retain the selected mode.");
                _currentWindowsPowerMode = targetMode;
                UpdateWindowsPowerModeButtons(targetMode);
                ModeOsdWindow.Present("Windows · " + WindowsPowerMode.Name(targetMode));
            }
            catch (Exception exception)
            {
                UpdateWindowsPowerModeButtons(previous);
                ReportFailure("Windows power-mode change failed: " + exception.Message);
            }
            finally
            {
                _loading = false;
                _changingWindowsPowerMode = false;
                SetControlsEnabled(true);
            }
        }

        private async Task RefreshWindowsPowerModeAsync(bool reportFailure)
        {
            if (_changingWindowsPowerMode || _refreshingWindowsPowerMode) return;
            _refreshingWindowsPowerMode = true;
            try
            {
                PowerSourceSnapshot source = await Task.Run(
                    new Func<PowerSourceSnapshot>(DashboardReader.ReadPowerSource));
                _onAcPower = source.OnAcPower;
                int mode = await Task.Run(new Func<int>(delegate
                {
                    return WindowsPowerMode.Read(source.OnAcPower);
                }));
                _currentWindowsPowerMode = mode;
                UpdateWindowsPowerModeButtons(mode);
            }
            catch (Exception exception)
            {
                if (reportFailure)
                    ReportFailure("Could not read the Windows power mode: " + exception.Message);
            }
            finally
            {
                _refreshingWindowsPowerMode = false;
            }
        }

        public async void CyclePowerProfileFromTray()
        {
            if (_loading || _trayActionPending) return;
            _trayActionPending = true;
            try
            {
                PowerSourceSnapshot source = await Task.Run(
                    new Func<PowerSourceSnapshot>(DashboardReader.ReadPowerSource));
                int acerMode = await Task.Run(new Func<int>(DashboardReader.ReadPowerMode));
                int windowsMode = await Task.Run(new Func<int>(delegate
                {
                    return WindowsPowerMode.Read(source.OnAcPower);
                }));
                if (acerMode < 0)
                    throw new InvalidOperationException("Quick Access did not report a mode.");
                _onAcPower = source.OnAcPower;
                _currentPowerMode = acerMode;
                _currentWindowsPowerMode = windowsMode;
                UpdatePowerModeButtons(acerMode);
                UpdateWindowsPowerModeButtons(windowsMode);
                NotifyPowerModeObserved(acerMode);
                PowerProfileOption current = PowerProfiles.Match(acerMode, windowsMode);
                int next = current == null
                    ? PowerProfiles.Everyday
                    : (current.Value + 1) % PowerProfiles.All().Length;
                await ApplyPowerProfileAsync(PowerProfiles.Get(next), true, "Profile");
            }
            catch (Exception exception)
            {
                ReportFailure("Power-profile change failed: " + exception.Message);
            }
            finally
            {
                _trayActionPending = false;
            }
        }

        public async void SetPowerProfileFromTray(int profileValue)
        {
            if (_loading || _trayActionPending || !PowerProfiles.IsValid(profileValue)) return;
            _trayActionPending = true;
            try
            {
                await ApplyPowerProfileAsync(
                    PowerProfiles.Get(profileValue), true, "Profile");
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

        public async void RefreshTrayState(bool evaluateAutomation = false)
        {
            await RefreshTrayStateAsync(evaluateAutomation, true);
        }

        private async Task<bool> RefreshTrayStateAsync(
            bool evaluateAutomation, bool reportFailure)
        {
            if (_loading || _trayActionPending || _refreshingTrayState) return false;
            _refreshingTrayState = true;
            bool refreshed = false;
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
                refreshed = true;
            }
            catch (Exception exception)
            {
                if (reportFailure)
                    ReportFailure("Could not refresh tray controls: " + exception.Message);
            }
            try
            {
                await RefreshWindowsPowerModeAsync(false);
                if (refreshed && evaluateAutomation)
                    await EvaluateAutomationAsync(true, true);
                return refreshed;
            }
            finally
            {
                _refreshingTrayState = false;
            }
        }

        private void ReportFailure(string message)
        {
            Action<string> handler = OperationFailed;
            if (handler != null) handler(message);
        }

        private void UpdatePowerModeButtons(int selectedValue)
        {
            if (_powerModes == null) return;
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
            UpdatePowerProfileDisplay();
        }

        private void UpdateWindowsPowerModeButtons(int selectedValue)
        {
            if (_windowsPowerModes == null) return;
            foreach (UIElement element in _windowsPowerModes.Children)
            {
                ToggleButton button = element as ToggleButton;
                if (button == null || button.Tag == null) continue;
                int mode = Convert.ToInt32(button.Tag, CultureInfo.InvariantCulture);
                bool selected = mode == selectedValue;
                button.IsChecked = selected;
                button.Background = selected ? _accent : Brush("#202733");
                button.Foreground = selected ? _background : _text;
                button.BorderBrush = selected ? _accent : _cardBorder;
            }
            UpdatePowerProfileDisplay();
        }

        private void UpdateQuickProfileButtons(int selectedValue)
        {
            if (_quickProfiles == null) return;
            foreach (UIElement element in _quickProfiles.Children)
            {
                ToggleButton button = element as ToggleButton;
                PowerProfileOption profile = button == null
                    ? null : button.Tag as PowerProfileOption;
                if (profile == null) continue;
                bool selected = profile.Value == selectedValue;
                button.IsChecked = selected;
                button.Background = selected ? _accent : Brush("#202733");
                button.Foreground = selected ? _background : _text;
                button.BorderBrush = selected ? _accent : _cardBorder;

                StackPanel content = button.Content as StackPanel;
                if (content == null) continue;
                for (int index = 0; index < content.Children.Count; index++)
                {
                    TextBlock label = content.Children[index] as TextBlock;
                    if (label == null) continue;
                    label.Foreground = selected
                        ? _background
                        : index == 0 ? _text : _muted;
                }
            }
        }

        private void UpdateAutomationVisuals()
        {
            bool enabled = _automationSettings.Enabled;
            if (_automationAssignmentRow != null)
                _automationAssignmentRow.Visibility = enabled
                    ? Visibility.Visible : Visibility.Collapsed;
            if (_automationStatusRow != null)
                _automationStatusRow.Visibility = enabled
                    ? Visibility.Visible : Visibility.Collapsed;
            if (_automationEnabled != null)
            {
                _automationEnabled.Content = enabled ? "Auto on  ▴" : "Auto  ▾";
                _automationEnabled.Background = enabled ? _accent : Brush("#202733");
                _automationEnabled.Foreground = enabled ? _background : _text;
                _automationEnabled.BorderBrush = enabled ? _accent : _cardBorder;
                _automationEnabled.ToolTip = enabled
                    ? "Automatic switching is on; click to turn it off"
                    : "Turn on automatic switching and show its assignments";
            }
            UpdateConditionButtons();
            UpdateAssignmentBadges();
            UpdateAutomationStatus(_lastAutomationCondition);
        }

        private void UpdateConditionButtons()
        {
            for (int condition = 0; condition < _conditionButtons.Length; condition++)
            {
                ToggleButton button = _conditionButtons[condition];
                if (button == null) continue;
                bool selected = condition == _selectedAutomationCondition;
                bool active = _automationSettings.Enabled &&
                    condition == _lastAutomationCondition;
                button.IsChecked = selected;
                if (condition == 2 && _lowBatteryCondition != null)
                {
                    button.Background = Brushes.Transparent;
                    button.Foreground = selected
                        ? _background : active ? _accent : _text;
                    button.BorderBrush = Brushes.Transparent;
                    button.BorderThickness = new Thickness(0);
                    button.Opacity = 1.0;
                    _lowBatteryCondition.Background = selected
                        ? _accent : Brush("#202733");
                    _lowBatteryCondition.BorderBrush = selected || active
                        ? _accent : _cardBorder;
                    _lowBatteryCondition.BorderThickness =
                        new Thickness(active && !selected ? 2 : 1);
                    _lowBatteryCondition.Opacity =
                        _automationSettings.Enabled ? 1.0 : 0.90;
                    if (_lowBatteryThreshold != null)
                        _lowBatteryThreshold.Foreground = selected ? _background : _text;
                    if (_lowBatteryPercentLabel != null)
                    {
                        _lowBatteryPercentLabel.Foreground = selected
                            ? _background : active ? _accent : _muted;
                    }
                    continue;
                }
                button.Background = selected ? _accent : Brush("#202733");
                button.Foreground = selected ? _background : active ? _accent : _text;
                button.BorderBrush = selected || active ? _accent : _cardBorder;
                button.BorderThickness = new Thickness(active && !selected ? 2 : 1);
                button.Opacity = _automationSettings.Enabled ? 1.0 : 0.90;
            }
        }

        private void UpdateAssignmentBadges()
        {
            if (_quickProfiles == null) return;
            foreach (UIElement element in _quickProfiles.Children)
            {
                ToggleButton button = element as ToggleButton;
                PowerProfileOption profile = button == null
                    ? null : button.Tag as PowerProfileOption;
                StackPanel content = button == null ? null : button.Content as StackPanel;
                if (profile == null || content == null || content.Children.Count < 3) continue;
                TextBlock assignments = content.Children[2] as TextBlock;
                if (assignments == null) continue;
                assignments.Visibility = _automationSettings.Enabled
                    ? Visibility.Visible : Visibility.Collapsed;
                button.MinHeight = _automationSettings.Enabled ? 66 : 56;
                assignments.Text = AssignmentBadges(profile.Value);
                bool selected = profile.AcerMode == _currentPowerMode &&
                    profile.WindowsMode == _currentWindowsPowerMode;
                assignments.Foreground = selected
                    ? _background
                    : _automationSettings.Enabled ? _accent : _muted;
                assignments.Opacity = _automationSettings.Enabled ? 1.0 : 0.82;
                button.ToolTip = profile.Description + "\n" +
                    (assignments.Text.Length == 0
                        ? "No automatic conditions assigned."
                        : "Assigned: " + AssignmentDescription(profile.Value));
            }
        }

        private string AssignmentBadges(int profileValue)
        {
            System.Collections.Generic.List<string> badges =
                new System.Collections.Generic.List<string>();
            if (_automationSettings.PluggedInProfile == profileValue) badges.Add("⚡");
            if (_automationSettings.UnpluggedProfile == profileValue) badges.Add("BAT");
            if (_automationSettings.LowBatteryProfile == profileValue)
                badges.Add("LOW " + _automationSettings.LowBatteryThreshold + "%");
            return String.Join("  ", badges.ToArray());
        }

        private string AssignmentDescription(int profileValue)
        {
            System.Collections.Generic.List<string> assignments =
                new System.Collections.Generic.List<string>();
            if (_automationSettings.PluggedInProfile == profileValue)
                assignments.Add("plugged in");
            if (_automationSettings.UnpluggedProfile == profileValue)
                assignments.Add("on battery");
            if (_automationSettings.LowBatteryProfile == profileValue)
                assignments.Add("battery at or below " +
                    _automationSettings.LowBatteryThreshold + "%");
            return String.Join(", ", assignments.ToArray());
        }

        private string ConditionName(int condition)
        {
            if (condition == 0) return "plugged in";
            if (condition == 2)
                return "battery ≤ " + _automationSettings.LowBatteryThreshold + "%";
            return "on battery";
        }

        private void UpdatePowerProfileDisplay()
        {
            if (_profileTitle == null || _profileSummary == null) return;
            PowerProfileOption match = PowerProfiles.Match(
                _currentPowerMode, _currentWindowsPowerMode);
            if (_currentPowerMode < 0 || _currentWindowsPowerMode < 0)
            {
                _profileTitle.Text = "Reading current profile…";
                _profileSummary.Text = "Waiting for Acer and Windows policy state.";
                UpdateQuickProfileButtons(-1);
            }
            else
            {
                _profileTitle.Text = match == null ? "Custom" : match.Name;
                _profileSummary.Text = PowerProfiles.CurrentDescription(
                    _currentPowerMode, _currentWindowsPowerMode, _onAcPower);
                UpdateQuickProfileButtons(match == null ? -1 : match.Value);
            }
            _profileSummary.ToolTip = _profileSummary.Text;

            if (_acerProfileDescription != null)
            {
                _acerProfileDescription.Text = _currentPowerMode < 0
                    ? "Reading Acer firmware state…"
                    : PowerProfiles.AcerDescription(_currentPowerMode, _onAcPower);
            }
            if (_windowsPolicyDescription != null)
            {
                _windowsPolicyDescription.Text = _currentWindowsPowerMode < 0
                    ? "Reading Windows policy…"
                    : PowerProfiles.WindowsDescription(_currentWindowsPowerMode);
            }
            Action<int> profileHandler = PowerProfileObserved;
            if (profileHandler != null)
                profileHandler(match == null ? -1 : match.Value);
            UpdateAutomationVisuals();
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
            if (_limit != null) _limit.IsEnabled = enabled;
            if (_closeAcerSense != null) _closeAcerSense.IsEnabled = enabled;
            if (_advancedToggle != null) _advancedToggle.IsEnabled = enabled;
            if (_automationEnabled != null) _automationEnabled.IsEnabled = enabled;
            if (_lowBatteryThreshold != null) _lowBatteryThreshold.IsEnabled = enabled;
            for (int condition = 0; condition < _conditionButtons.Length; condition++)
            {
                if (_conditionButtons[condition] != null)
                    _conditionButtons[condition].IsEnabled = enabled;
            }
            if (_resumeAutomation != null) _resumeAutomation.IsEnabled = enabled;
            if (_quickProfiles != null)
            {
                foreach (UIElement element in _quickProfiles.Children)
                    element.IsEnabled = enabled;
            }
            if (_windowsPowerModes != null) _windowsPowerModes.IsEnabled = enabled;
            if (_batteryHibernate != null && !_changingBatteryHibernate)
                _batteryHibernate.IsEnabled = enabled;
            if (_standbyNetwork != null && !_changingStandbyNetwork)
                _standbyNetwork.IsEnabled = enabled;
            for (int mode = 0; mode < _lockAfterSuspendModes.Length; mode++)
            {
                if (_lockAfterSuspendModes[mode] != null)
                    _lockAfterSuspendModes[mode].IsEnabled = enabled;
            }
            if (_trustCurrentWifi != null)
                _trustCurrentWifi.IsEnabled = enabled && _currentWifi != null;
            if (_manageTrustedWifi != null)
                _manageTrustedWifi.IsEnabled = enabled;
            if (_powerModes == null) return;
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

        private Border TabPageSurface()
        {
            Border page = new Border();
            page.Background = _pageSurface;
            page.BorderBrush = _cardBorder;
            page.BorderThickness = new Thickness(1, 0, 1, 1);
            page.CornerRadius = new CornerRadius(0, 0, 13, 13);
            page.Padding = new Thickness(10);
            return page;
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

        private static ControlTemplate CreatePanelTabTemplate()
        {
            ControlTemplate template = new ControlTemplate(typeof(ToggleButton));
            FrameworkElementFactory layout = new FrameworkElementFactory(typeof(Grid));

            FrameworkElementFactory chrome = new FrameworkElementFactory(typeof(Border));
            chrome.Name = "TabChrome";
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(7, 7, 0, 0));
            chrome.SetValue(Border.BackgroundProperty,
                new TemplateBindingExtension(Control.BackgroundProperty));

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty,
                new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(FrameworkElement.MarginProperty,
                new TemplateBindingExtension(Control.PaddingProperty));
            presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            chrome.AppendChild(presenter);
            layout.AppendChild(chrome);

            FrameworkElementFactory indicator = new FrameworkElementFactory(typeof(Border));
            indicator.Name = "TabIndicator";
            indicator.SetValue(FrameworkElement.HeightProperty, 3.0);
            indicator.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 12, 0));
            indicator.SetValue(FrameworkElement.HorizontalAlignmentProperty,
                HorizontalAlignment.Stretch);
            indicator.SetValue(FrameworkElement.VerticalAlignmentProperty,
                VerticalAlignment.Bottom);
            indicator.SetValue(Border.CornerRadiusProperty, new CornerRadius(2, 2, 0, 0));
            indicator.SetValue(Border.BackgroundProperty,
                new TemplateBindingExtension(Control.BorderBrushProperty));
            layout.AppendChild(indicator);
            template.VisualTree = layout;

            MultiTrigger hover = new MultiTrigger();
            hover.Conditions.Add(new Condition(IsMouseOverProperty, true));
            hover.Conditions.Add(new Condition(ToggleButton.IsCheckedProperty, false));
            Setter hoverBackground = new Setter();
            hoverBackground.Property = Border.BackgroundProperty;
            hoverBackground.Value = Brush("#202833");
            hoverBackground.TargetName = "TabChrome";
            hover.Setters.Add(hoverBackground);
            template.Triggers.Add(hover);

            Trigger pressed = new Trigger();
            pressed.Property = ButtonBase.IsPressedProperty;
            pressed.Value = true;
            Setter pressedOpacity = new Setter();
            pressedOpacity.Property = UIElement.OpacityProperty;
            pressedOpacity.Value = 0.7;
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
