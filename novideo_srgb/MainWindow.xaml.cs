﻿using Microsoft.Win32;
using novideo_srgb.PowerBroadcast;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace novideo_srgb
{
    public partial class MainWindow
    {
        private readonly MainViewModel _viewModel;
        private System.Windows.Forms.NotifyIcon _trayIcon;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = (MainViewModel)DataContext;
            SystemEvents.DisplaySettingsChanged += _viewModel.OnDisplaySettingsChanged;
            InitializeTrayIcon();
        }

        private void AboutButton_Click(object sender, RoutedEventArgs o)
        {
            var window = new AboutWindow
            {
                Owner = this
            };
            window.ShowDialog();
        }

        private void AdvancedButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.Windows.Cast<Window>().Any(x => x is AdvancedWindow)) return;
            var monitor = ((FrameworkElement)sender).DataContext as MonitorData;
            var window = new AdvancedWindow(monitor)
            {
                Owner = this
            };
            if (window.ShowDialog() == false) return;
            if (window.ChangedCalibration)
            {
                try
                {
                    _viewModel.SaveConfig();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message + "\n\nTry extracting the program elsewhere.");
                    Environment.Exit(1);
                }

                monitor?.ReapplyClamp();
            }

            if (window.ChangedDither)
            {
                monitor?.ApplyDither(window.DitherState.SelectedIndex, Math.Max(window.DitherBits.SelectedIndex, 0),
                    Math.Max(window.DitherMode.SelectedIndex, 0));
            }
        }

        private async Task DeferAction(Action action, int seconds)
        {
            await Task.Delay(seconds);
            action();
        }

        private IntPtr HandleMessages(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) => PowerBroadcastNotificationHelpers.HandleBroadcastNotification(msg, lParam, (message) =>
        {
            if (((char)message.Data) == (char)ConsoleDisplayState.TurnedOn)
            {
                _ = DeferAction(ReapplyMonitorSettings, 150);
            }
        });

        private void MinimizeToTray()
        {
            Hide();
            ShowInTaskbar = false;
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "Novideo sRGB",
                Icon = Properties.Resources.icon,
                Visible = true
            };

            _trayIcon.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
            _trayIcon.ContextMenuStrip.Items.Add("Hide", null, TrayIcon_Hide_Click);
            _trayIcon.ContextMenuStrip.Items.Add("Restore", null, TrayIcon_Restore_Click);
            _trayIcon.ContextMenuStrip.Items.Add("Quit", null, TrayIcon_Quit_Click);

            _trayIcon.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(TrayIcon_DoubleClick);
        }

        private void MinimizeToTrayButton_Click(object sender, RoutedEventArgs e) => MinimizeToTray();

        protected override void OnClosed(EventArgs e)
        {
            _trayIcon.Dispose();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            base.OnClosed(e);
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            /*
             * TODO: This needs to work on a per-monitor basis.
             * GetDisplayHdrStatus loops through all monitors and returns the result only for the last one.
             * I'm not seeing an obvious way to tie monitor data grabbed from the Nvidia API to that gathered from the Windows API.
             * Maybe investigate whether the Nvidia API can tell us whether HDR is enabled on a display?
             * This might not be a problem, as I think Windows does not allow mixed SDR and HDR multi monitor configurations.
            */
            var hdrStatus = DisplayConfigManager.GetDisplayHdrStatus();
            var clamp = hdrStatus == DisplayHdrStatus.Disabled ? true : false;

            foreach (var monitor in _viewModel.Monitors)
            {
                monitor.Clamped = clamp;
                monitor.ReapplyClamp();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            SystemEvents.DisplaySettingsChanged += new EventHandler(OnDisplaySettingsChanged);
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            PowerBroadcastNotificationHelpers.RegisterPowerBroadcastNotification(handle, PowerSettingGuids.CONSOLE_DISPLAY_STATE);
            HwndSource.FromHwnd(handle)?.AddHook(HandleMessages);
        }

        private void ReapplyMonitorSettings()
        {
            foreach (var monitor in _viewModel.Monitors)
            {
                monitor.ReapplyClamp();
            }
        }

        private void Restore()
        {
            Show();
            WindowState = WindowState.Normal;
            ShowInTaskbar = true;
            Activate();
        }

        private void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
            if(ShowInTaskbar == false || WindowState == WindowState.Minimized)
            {
                Restore();
            }
            else
            {
                MinimizeToTray();
            }
        }

        private void TrayIcon_Hide_Click(object sender, EventArgs e) => MinimizeToTray();

        private void TrayIcon_Restore_Click(object sender, EventArgs e) => Restore();

        private void TrayIcon_Quit_Click(object sender, EventArgs e) => Application.Current.Shutdown();
    }
}