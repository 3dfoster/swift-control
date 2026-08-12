using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SwiftControl
{
    internal sealed class TrayController : IDisposable
    {
        private readonly MainWindow _window;
        private readonly NotifyIcon _notify;
        private readonly Icon[] _modeIcons;
        private readonly ToolStripMenuItem[] _profileItems;
        private readonly ToolStripMenuItem _chargeItem;
        private bool _disposed;

        public TrayController(MainWindow window)
        {
            _window = window;
            _modeIcons = new[] { CreateModeIcon(0), CreateModeIcon(1), CreateModeIcon(2) };
            _window.PowerModeObserved += ModeObserved;
            _window.PowerProfileObserved += ProfileObserved;
            _window.ChargingLimitObserved += ChargingLimitObserved;
            _window.ChargingLimitChanged += ChargingLimitChanged;
            _window.OperationFailed += OperationFailed;

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem open = new ToolStripMenuItem("Open SwiftControl");
            open.Font = new Font(open.Font, FontStyle.Bold);
            open.Click += delegate { _window.ShowFromTray(); };
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());

            PowerProfileOption[] profiles = PowerProfiles.All();
            _profileItems = new ToolStripMenuItem[profiles.Length];
            for (int profileIndex = 0; profileIndex < profiles.Length; profileIndex++)
            {
                PowerProfileOption profile = profiles[profileIndex];
                ToolStripMenuItem item = new ToolStripMenuItem(profile.Name);
                item.ToolTipText = profile.Description;
                item.Tag = profile.Value;
                item.Click += ProfileMenuClicked;
                _profileItems[profileIndex] = item;
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());
            _chargeItem = new ToolStripMenuItem("Charging limit: 100%");
            _chargeItem.Click += ChargeMenuClicked;
            menu.Items.Add(_chargeItem);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
            exit.Click += ExitClicked;
            menu.Items.Add(exit);
            menu.Opening += delegate { _window.RefreshTrayState(); };

            _notify = new NotifyIcon();
            _notify.Text = "SwiftControl · Normal";
            _notify.Icon = _modeIcons[1];
            _notify.ContextMenuStrip = menu;
            _notify.MouseClick += TrayClicked;
            _notify.Visible = true;
        }

        private void TrayClicked(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _window.CyclePowerProfileFromTray();
            }
        }

        private void ProfileMenuClicked(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item != null) _window.SetPowerProfileFromTray((int)item.Tag);
        }

        private void ChargeMenuClicked(object sender, EventArgs e)
        {
            _window.ToggleChargingLimitFromTray();
        }

        private void ExitClicked(object sender, EventArgs e)
        {
            _window.ExitApplication();
            System.Windows.Application.Current.Shutdown();
        }

        private void ModeObserved(int mode)
        {
            if (_disposed || mode < 0 || mode >= _modeIcons.Length) return;
            string[] names = { "Silent", "Normal", "Performance" };
            _notify.Icon = _modeIcons[mode];
            _notify.Text = "SwiftControl · " + names[mode];
        }

        private void ProfileObserved(int profileValue)
        {
            if (_disposed) return;
            PowerProfileOption profile = PowerProfiles.IsValid(profileValue)
                ? PowerProfiles.Get(profileValue) : null;
            _notify.Text = "SwiftControl · " +
                (profile == null ? "Custom" : profile.Name);
            for (int index = 0; index < _profileItems.Length; index++)
            {
                _profileItems[index].Checked = profile != null &&
                    Convert.ToInt32(_profileItems[index].Tag) == profile.Value;
            }
        }

        private void ChargingLimitObserved(bool optimized)
        {
            if (_disposed) return;
            _chargeItem.Text = "Charging limit: " + (optimized ? "80%" : "100%");
        }

        private void ChargingLimitChanged(bool optimized)
        {
            if (_disposed) return;
            _notify.ShowBalloonTip(
                2500,
                "SwiftControl",
                optimized ? "Charging limit set to 80%." : "Full charging enabled (100%).",
                ToolTipIcon.Info);
        }

        private void OperationFailed(string message)
        {
            if (_disposed) return;
            _notify.ShowBalloonTip(4000, "SwiftControl error", message, ToolTipIcon.Error);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _window.PowerModeObserved -= ModeObserved;
            _window.PowerProfileObserved -= ProfileObserved;
            _window.ChargingLimitObserved -= ChargingLimitObserved;
            _window.ChargingLimitChanged -= ChargingLimitChanged;
            _window.OperationFailed -= OperationFailed;
            _notify.Visible = false;
            _notify.Dispose();
            foreach (Icon icon in _modeIcons) icon.Dispose();
        }

        private static Icon CreateModeIcon(int mode)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                Color color = mode == 0
                    ? Color.FromArgb(85, 112, 168)
                    : mode == 2 ? Color.FromArgb(217, 105, 74) : Color.FromArgb(79, 131, 184);
                using (SolidBrush background = new SolidBrush(color))
                {
                    graphics.FillEllipse(background, 1.5f, 1.5f, 29, 29);

                    if (mode == 0)
                    {
                        graphics.FillEllipse(Brushes.White, 8, 6, 16, 20);
                        graphics.FillEllipse(background, 13, 3, 15, 19);
                    }
                    else if (mode == 1)
                    {
                        using (Pen ring = new Pen(Color.White, 2.5f))
                        {
                            graphics.DrawEllipse(ring, 8, 8, 16, 16);
                        }
                        graphics.FillEllipse(Brushes.White, 13, 13, 6, 6);
                    }
                    else
                    {
                        PointF[] bolt =
                        {
                            new PointF(17, 4), new PointF(8, 17),
                            new PointF(14, 17), new PointF(11, 28),
                            new PointF(24, 13), new PointF(18, 13),
                            new PointF(22, 4)
                        };
                        graphics.FillPolygon(Brushes.White, bolt);
                    }
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
