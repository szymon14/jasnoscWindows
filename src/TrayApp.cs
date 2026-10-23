using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Jasnosc
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (new Mutex(true, "Jasnosc_SingleInstance", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }

    static class Theme
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static bool AppsDark() { return ReadDword("AppsUseLightTheme", 1) == 0; }
        public static bool TaskbarDark() { return ReadDword("SystemUsesLightTheme", 0) == 0; }

        static int ReadDword(string name, int fallback)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
            {
                object v = k == null ? null : k.GetValue(name);
                return v is int ? (int)v : fallback;
            }
        }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Jasnosc";

        static string Command { get { return "\"" + Application.ExecutablePath + "\""; } }

        public static bool Enabled
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && string.Equals(k.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase);
            }
            set
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(Name, Command);
                    else k.DeleteValue(Name, false);
                }
            }
        }
    }

    class TrayApp : ApplicationContext
    {
        readonly BrightnessService service;
        readonly PopupForm popup;
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem autostartItem;

        public TrayApp()
        {
            service = new BrightnessService();
            popup = new PopupForm(service);
            IntPtr forceHandle = popup.Handle; // BeginInvoke wymaga utworzonego uchwytu

            service.Changed += delegate(DisplayInfo[] list)
            {
                try { popup.BeginInvoke((Action)delegate { OnChanged(list); }); }
                catch (InvalidOperationException) { } // zamykanie aplikacji
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Odśwież monitory", null, delegate { service.RequestRefresh(); });
            autostartItem = new ToolStripMenuItem("Uruchamiaj razem z Windows");
            autostartItem.Checked = Autostart.Enabled;
            autostartItem.Click += delegate
            {
                Autostart.Enabled = !autostartItem.Checked;
                autostartItem.Checked = Autostart.Enabled;
            };
            menu.Items.Add(autostartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Zamknij", null, delegate { ExitThread(); });

            tray = new NotifyIcon();
            tray.Icon = MakeSunIcon(Theme.TaskbarDark());
            tray.Text = "Jasność";
            tray.ContextMenuStrip = menu;
            tray.MouseClick += OnTrayClick;
            tray.Visible = true;

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            service.RequestRefresh();
        }

        void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            // Kliknięcie w ikonę, gdy okno jest otwarte, najpierw je chowa (Deactivate) - nie otwieraj go wtedy ponownie.
            if (popup.Visible || (DateTime.Now - popup.LastHidden).TotalMilliseconds < 300) return;
            popup.ShowPopup();
            // Odczyt na świeżo: łapie zmiany zrobione przyciskami na monitorze i monitory, które "odpadły".
            service.RequestRefresh();
        }

        void OnChanged(DisplayInfo[] list)
        {
            popup.SetDisplays(list);

            string text = "Jasność";
            foreach (DisplayInfo d in list)
                text += "\n" + d.Name + ": " + (d.Available ? d.Brightness + "%" : "—");
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        void OnDisplaySettingsChanged(object sender, EventArgs e) { service.RequestRefresh(1500); }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) service.RequestRefresh(4000);
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            Icon old = tray.Icon;
            tray.Icon = MakeSunIcon(Theme.TaskbarDark());
            old.Dispose();
            popup.ApplyTheme();
        }

        static Icon MakeSunIcon(bool darkTaskbar)
        {
            int s = SystemInformation.SmallIconSize.Width;
            using (var bmp = new Bitmap(s, s))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Color c = darkTaskbar ? Color.White : Color.Black;
                    float center = s / 2f;
                    float r = s * 0.2f;
                    using (var brush = new SolidBrush(c))
                        g.FillEllipse(brush, center - r, center - r, 2 * r, 2 * r);
                    using (var pen = new Pen(c, Math.Max(1.2f, s / 12f)))
                    {
                        pen.StartCap = pen.EndCap = LineCap.Round;
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            float inner = r * 1.55f, outer = s * 0.46f;
                            g.DrawLine(pen,
                                center + (float)Math.Cos(a) * inner, center + (float)Math.Sin(a) * inner,
                                center + (float)Math.Cos(a) * outer, center + (float)Math.Sin(a) * outer);
                        }
                    }
                }
                IntPtr h = bmp.GetHicon();
                using (Icon tmp = Icon.FromHandle(h))
                {
                    Icon icon = (Icon)tmp.Clone();
                    Native.DestroyIcon(h);
                    return icon;
                }
            }
        }

        protected override void ExitThreadCore()
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            tray.Visible = false;
            tray.Dispose();
            service.Dispose();
            popup.Dispose();
            base.ExitThreadCore();
        }
    }
}
