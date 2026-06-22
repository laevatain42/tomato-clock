using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TomatoClockApp
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TomatoForm());
        }
    }

    internal sealed class TomatoForm : Form
    {
        private const int SleepGapSeconds = 10;
        private const int WmPowerBroadcast = 0x0218;
        private const int PbtPowerSettingChange = 0x8013;
        private const int DeviceNotifyWindowHandle = 0;
        private static readonly Guid ConsoleDisplayState = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");

        private readonly string appDir;
        private readonly string settingsPath;
        private readonly string iconPath;
        private readonly Timer timer;
        private readonly NotifyIcon notifyIcon;

        private Label modeLabel;
        private Label timeLabel;
        private Label statusLabel;
        private Button startPauseButton;
        private Button startupOnButton;
        private Button startupOffButton;
        private TextBox workInput;
        private TextBox breakInput;

        private int workMinutes = 25;
        private int breakMinutes = 5;
        private bool topMostSetting = true;
        private bool isWorkMode = true;
        private bool isRunning;
        private double elapsedSeconds;
        private DateTime lastTick = DateTime.Now;
        private IntPtr powerNotificationHandle = IntPtr.Zero;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, int flags, UIntPtr extraInfo);

        private const byte VkLeftWindows = 0x5B;
        private const byte VkD = 0x44;
        private const int KeyEventKeyUp = 0x0002;

        public TomatoForm()
        {
            appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            settingsPath = Path.Combine(appDir, "settings.json");
            iconPath = Path.Combine(appDir, "tomato-clock.ico");

            LoadSettings();
            BuildUi();
            ApplyIcon();

            timer = new Timer { Interval = 500 };
            timer.Tick += OnTimerTick;

            notifyIcon = new NotifyIcon
            {
                Text = "Tomato Clock",
                Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
                Visible = true
            };
            notifyIcon.DoubleClick += delegate { ShowFromTray(); };

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            Shown += delegate
            {
                RegisterDisplayPowerNotification();
                UpdateStartupButtons();
                UpdateDisplay();
                timer.Start();
            };

            FormClosing += delegate
            {
                timer.Stop();
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                if (powerNotificationHandle != IntPtr.Zero)
                {
                    UnregisterPowerSettingNotification(powerNotificationHandle);
                    powerNotificationHandle = IntPtr.Zero;
                }
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            };
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmPowerBroadcast && message.WParam.ToInt32() == PbtPowerSettingChange)
            {
                Guid settingGuid = (Guid)Marshal.PtrToStructure(message.LParam, typeof(Guid));
                if (settingGuid == ConsoleDisplayState)
                {
                    int displayState = Marshal.ReadInt32(message.LParam, 20);
                    if (displayState == 0)
                    {
                        ResetTimer("Display off detected. Reset to zero.");
                    }
                }
            }

            base.WndProc(ref message);
        }

        private void BuildUi()
        {
            Text = "Tomato Clock";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Width = 360;
            Height = 560;
            MinimumSize = new Size(340, 520);
            FormBorderStyle = FormBorderStyle.Sizable;
            ControlBox = true;
            MinimizeBox = true;
            MaximizeBox = true;
            BackColor = Color.FromArgb(247, 242, 234);
            TopMost = topMostSetting;
            ShowInTaskbar = true;
            Font = new Font("Segoe UI", 9.5f);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 4,
                BackColor = BackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 138));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            Controls.Add(root);

            var displayPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                BackColor = BackColor,
                Margin = new Padding(0, 12, 0, 12)
            };
            displayPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            displayPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            displayPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.Controls.Add(displayPanel, 0, 0);

            modeLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(Font.FontFamily, 12f, FontStyle.Bold)
            };
            displayPanel.Controls.Add(modeLabel, 0, 0);

            timeLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(Font.FontFamily, 50f, FontStyle.Bold),
                ForeColor = Color.FromArgb(32, 32, 32)
            };
            displayPanel.Controls.Add(timeLabel, 0, 1);

            statusLabel = new Label
            {
                Text = "Ready",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(108, 98, 88)
            };
            displayPanel.Controls.Add(statusLabel, 0, 2);

            var actionRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = BackColor };
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.Controls.Add(actionRow, 0, 1);
            var resetButton = StandardButton("Reset", Color.White, Color.FromArgb(32, 32, 32));
            resetButton.Margin = new Padding(0, 0, 6, 12);
            resetButton.Click += delegate { ResetTimer("Reset to zero"); };
            actionRow.Controls.Add(resetButton, 0, 0);
            var switchButton = StandardButton("Switch", Color.FromArgb(239, 229, 216), Color.FromArgb(32, 32, 32));
            switchButton.Margin = new Padding(6, 0, 0, 12);
            switchButton.Click += delegate { SwitchMode(); };
            actionRow.Controls.Add(switchButton, 1, 0);

            var settingsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 2,
                RowCount = 3,
                BackColor = Color.FromArgb(255, 253, 249),
                Margin = new Padding(0, 0, 0, 12)
            };
            settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            settingsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            settingsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            settingsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.Controls.Add(settingsPanel, 0, 2);

            settingsPanel.Controls.Add(FormLabel("Focus minutes"), 0, 0);
            workInput = FormInput(workMinutes.ToString());
            settingsPanel.Controls.Add(workInput, 1, 0);
            settingsPanel.Controls.Add(FormLabel("Break minutes"), 0, 1);
            breakInput = FormInput(breakMinutes.ToString());
            settingsPanel.Controls.Add(breakInput, 1, 1);
            var applyButton = StandardButton("Apply settings", Color.FromArgb(32, 32, 32), Color.White);
            applyButton.Click += ApplySettings;
            settingsPanel.Controls.Add(applyButton, 0, 2);
            settingsPanel.SetColumnSpan(applyButton, 2);

            var bottomPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = BackColor };
            bottomPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            bottomPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.Controls.Add(bottomPanel, 0, 3);

            var startupRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = BackColor };
            startupRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            startupRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            startupRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            bottomPanel.Controls.Add(startupRow, 0, 0);

            var pinButton = StandardButton("Pin", Color.FromArgb(239, 229, 216), Color.FromArgb(32, 32, 32));
            pinButton.Margin = new Padding(0, 0, 6, 12);
            pinButton.Click += delegate
            {
                TopMost = !TopMost;
                SaveSettings();
                statusLabel.Text = TopMost ? "Always on top enabled" : "Always on top disabled";
            };
            startupRow.Controls.Add(pinButton, 0, 0);

            startupOnButton = StandardButton("Startup On", Color.White, Color.FromArgb(32, 32, 32));
            startupOnButton.Margin = new Padding(6, 0, 6, 12);
            startupOnButton.Click += delegate { EnableStartupFromUi(); };
            startupRow.Controls.Add(startupOnButton, 1, 0);
            startupOffButton = StandardButton("Startup Off", Color.FromArgb(239, 229, 216), Color.FromArgb(32, 32, 32));
            startupOffButton.Margin = new Padding(6, 0, 0, 12);
            startupOffButton.Click += delegate { DisableStartupFromUi(); };
            startupRow.Controls.Add(startupOffButton, 2, 0);

            startPauseButton = StandardButton("Start", Color.FromArgb(181, 59, 45), Color.White);
            startPauseButton.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
            startPauseButton.Click += delegate
            {
                isRunning = !isRunning;
                lastTick = DateTime.Now;
                statusLabel.Text = isRunning ? "Running" : "Paused";
                UpdateDisplay();
            };
            bottomPanel.Controls.Add(startPauseButton, 0, 1);

            SizeGripStyle = SizeGripStyle.Auto;
        }

        private void ApplyIcon()
        {
            if (File.Exists(iconPath))
            {
                Icon = new Icon(iconPath);
            }
        }

        private Button StandardButton(string text, Color back, Color fore)
        {
            return new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                BackColor = back,
                ForeColor = fore,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false
            };
        }

        private Label FormLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(32, 32, 32)
            };
        }

        private TextBox FormInput(string text)
        {
            return new TextBox
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = HorizontalAlignment.Center,
                Margin = new Padding(0, 4, 0, 4)
            };
        }

        private void OnTimerTick(object sender, EventArgs args)
        {
            DateTime now = DateTime.Now;
            double gap = (now - lastTick).TotalSeconds;

            if (gap >= SleepGapSeconds)
            {
                ResetTimer("Sleep, display off, or long pause detected. Reset to zero.");
                return;
            }

            if (isRunning)
            {
                elapsedSeconds += Math.Max(0, gap);
                if (elapsedSeconds >= GetCurrentDurationSeconds())
                {
                    CompleteRound();
                }
            }

            lastTick = now;
            UpdateDisplay();
        }

        private void ResetTimer(string reason)
        {
            isRunning = false;
            elapsedSeconds = 0;
            lastTick = DateTime.Now;
            statusLabel.Text = reason;
            UpdateDisplay();
        }

        private void SwitchMode()
        {
            isWorkMode = !isWorkMode;
            ResetTimer(isWorkMode ? "Switched to focus" : "Switched to break");
        }

        private void CompleteRound()
        {
            bool completedFocus = isWorkMode;
            System.Media.SystemSounds.Asterisk.Play();
            isWorkMode = !isWorkMode;
            elapsedSeconds = 0;
            lastTick = DateTime.Now;
            statusLabel.Text = isWorkMode ? "Break complete. Focus starts now." : "Focus complete. Break starts now.";
            UpdateDisplay();

            if (completedFocus)
            {
                ShowDesktop();
                WindowState = FormWindowState.Normal;
                Show();
                Activate();
            }
        }

        private void ShowDesktop()
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                object shell = Activator.CreateInstance(shellType);
                shellType.InvokeMember("MinimizeAll", BindingFlags.InvokeMethod, null, shell, null);
            }
            catch
            {
                keybd_event(VkLeftWindows, 0, 0, UIntPtr.Zero);
                keybd_event(VkD, 0, 0, UIntPtr.Zero);
                keybd_event(VkD, 0, KeyEventKeyUp, UIntPtr.Zero);
                keybd_event(VkLeftWindows, 0, KeyEventKeyUp, UIntPtr.Zero);
            }
        }

        private int GetCurrentDurationSeconds()
        {
            return (isWorkMode ? workMinutes : breakMinutes) * 60;
        }

        private void UpdateDisplay()
        {
            int remaining = (int)Math.Ceiling(GetCurrentDurationSeconds() - elapsedSeconds);
            if (remaining < 0)
            {
                remaining = 0;
            }

            timeLabel.Text = string.Format("{0:00}:{1:00}", remaining / 60, remaining % 60);
            modeLabel.Text = isWorkMode ? "Focus" : "Break";
            modeLabel.ForeColor = isWorkMode ? Color.FromArgb(181, 59, 45) : Color.FromArgb(34, 116, 91);
            startPauseButton.Text = isRunning ? "Pause" : "Start";
        }

        private void ApplySettings(object sender, EventArgs args)
        {
            try
            {
                workMinutes = ReadMinutes(workInput.Text, "Focus minutes");
                breakMinutes = ReadMinutes(breakInput.Text, "Break minutes");
                SaveSettings();
                ResetTimer("Settings applied. Reset to zero.");
            }
            catch (Exception ex)
            {
                statusLabel.Text = ex.Message;
            }
        }

        private int ReadMinutes(string text, string name)
        {
            int parsed;
            if (!int.TryParse(text.Trim(), out parsed) || parsed < 1 || parsed > 999)
            {
                throw new InvalidOperationException(name + " must be an integer from 1 to 999.");
            }

            return parsed;
        }

        private void LoadSettings()
        {
            if (!File.Exists(settingsPath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(settingsPath);
                workMinutes = ReadJsonInt(json, "WorkMinutes", workMinutes);
                breakMinutes = ReadJsonInt(json, "BreakMinutes", breakMinutes);
                topMostSetting = ReadJsonBool(json, "Topmost", true);
            }
            catch
            {
                workMinutes = 25;
                breakMinutes = 5;
                topMostSetting = true;
            }
        }

        private void SaveSettings()
        {
            string json = "{\r\n" +
                "  \"WorkMinutes\": " + workMinutes + ",\r\n" +
                "  \"BreakMinutes\": " + breakMinutes + ",\r\n" +
                "  \"Topmost\": " + (TopMost ? "true" : "false") + "\r\n" +
                "}\r\n";
            File.WriteAllText(settingsPath, json);
        }

        private int ReadJsonInt(string json, string name, int fallback)
        {
            Match match = Regex.Match(json, "\"" + name + "\"\\s*:\\s*(\\d+)");
            if (!match.Success)
            {
                return fallback;
            }

            int value;
            return int.TryParse(match.Groups[1].Value, out value) && value >= 1 ? value : fallback;
        }

        private bool ReadJsonBool(string json, string name, bool fallback)
        {
            Match match = Regex.Match(json, "\"" + name + "\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return fallback;
            }

            return string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private string GetStartupShortcutPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Tomato Clock.lnk");
        }

        private void UpdateStartupButtons()
        {
            bool enabled = File.Exists(GetStartupShortcutPath());
            startupOnButton.Enabled = !enabled;
            startupOffButton.Enabled = enabled;
        }

        private void EnableStartupFromUi()
        {
            try
            {
                EnableStartup();
                UpdateStartupButtons();
                statusLabel.Text = "Startup enabled";
            }
            catch (Exception ex)
            {
                statusLabel.Text = ex.Message;
            }
        }

        private void DisableStartupFromUi()
        {
            try
            {
                string path = GetStartupShortcutPath();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                UpdateStartupButtons();
                statusLabel.Text = "Startup disabled";
            }
            catch (Exception ex)
            {
                statusLabel.Text = ex.Message;
            }
        }

        private void EnableStartup()
        {
            string target = Path.Combine(appDir, "TomatoClock.exe");
            if (!File.Exists(target))
            {
                throw new InvalidOperationException("Launcher not found: " + target);
            }

            string shortcutPath = GetStartupShortcutPath();
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            Type shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { appDir });
            shortcutType.InvokeMember("WindowStyle", BindingFlags.SetProperty, null, shortcut, new object[] { 7 });
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Start Tomato Clock on login" });
            if (File.Exists(iconPath))
            {
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconPath });
            }
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }

        private void RegisterDisplayPowerNotification()
        {
            Guid displayGuid = ConsoleDisplayState;
            powerNotificationHandle = RegisterPowerSettingNotification(Handle, ref displayGuid, DeviceNotifyWindowHandle);
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
        {
            if (args.Mode == PowerModes.Suspend || args.Mode == PowerModes.Resume)
            {
                ResetTimer("Sleep or resume detected. Reset to zero.");
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs args)
        {
            if (args.Reason == SessionSwitchReason.SessionLock || args.Reason == SessionSwitchReason.SessionUnlock)
            {
                ResetTimer("Lock or unlock detected. Reset to zero.");
            }
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }
    }
}
