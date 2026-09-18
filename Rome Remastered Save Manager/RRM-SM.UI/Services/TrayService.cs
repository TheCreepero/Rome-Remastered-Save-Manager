using System;
using System.Drawing;
using System.Windows.Forms;

namespace RRM_SM.UI.Services
{
    public class TrayService : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private ContextMenuStrip? _contextMenu;
        private ToolStripMenuItem? _watcherMenuItem;
        private bool _isDisposed;

        public event Action? OpenRequested;
        public event Action? LaunchGameRequested;
        public event Action? ToggleWatcherRequested;
        public event Action? QuickBackupRequested;
        public event Action? ExitRequested;

        public bool ShowNotifications { get; set; } = true;

        public void Initialize()
        {
            if (_notifyIcon != null) return;

            Icon? icon = null;
            try
            {
                if (!string.IsNullOrEmpty(Environment.ProcessPath))
                {
                    icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
                }
            }
            catch
            {
                // Fallback to system icon
            }

            icon ??= SystemIcons.Application;

            _contextMenu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem("Open Save Manager", null, (s, e) => OpenRequested?.Invoke());
            openItem.Font = new Font(openItem.Font, FontStyle.Bold);

            var launchGameItem = new ToolStripMenuItem("⚔ Launch Total War: ROME REMASTERED", null, (s, e) => LaunchGameRequested?.Invoke());

            var quickBackupItem = new ToolStripMenuItem("⚡ Quick Backup Now", null, (s, e) => QuickBackupRequested?.Invoke());

            _watcherMenuItem = new ToolStripMenuItem("🛡 Autosave Sentinel: OFF", null, (s, e) => ToggleWatcherRequested?.Invoke());

            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitRequested?.Invoke());

            _contextMenu.Items.Add(openItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(launchGameItem);
            _contextMenu.Items.Add(quickBackupItem);
            _contextMenu.Items.Add(_watcherMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon
            {
                Icon = icon,
                Text = "Rome Remastered Save Manager",
                ContextMenuStrip = _contextMenu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => OpenRequested?.Invoke();
        }

        public void UpdateWatcherState(bool isRunning)
        {
            if (_watcherMenuItem != null)
            {
                _watcherMenuItem.Text = isRunning ? "🛡 Autosave Sentinel: [ON]" : "🛡 Autosave Sentinel: [OFF]";
                _watcherMenuItem.Checked = isRunning;
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.Text = isRunning
                    ? "Rome Remastered Save Manager (Sentinel: Active)"
                    : "Rome Remastered Save Manager (Sentinel: Off)";
            }
        }

        public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            if (!ShowNotifications || _notifyIcon == null || !_notifyIcon.Visible) return;

            try
            {
                _notifyIcon.ShowBalloonTip(3000, title, message, icon);
            }
            catch
            {
                // Gracefully ignore balloon tip display errors
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            if (_contextMenu != null)
            {
                _contextMenu.Dispose();
                _contextMenu = null;
            }
        }
    }
}
