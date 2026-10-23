using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Jasnosc
{
    class PopupForm : Form
    {
        readonly BrightnessService service;
        readonly TableLayoutPanel table;
        readonly Dictionary<string, TrackBar> bars = new Dictionary<string, TrackBar>();
        readonly Dictionary<string, Label> values = new Dictionary<string, Label>();
        string idsKey;
        float scale = 1f;

        public DateTime LastHidden = DateTime.MinValue;

        public PopupForm(BrightnessService service)
        {
            this.service = service;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = new Font("Segoe UI", 9.5f);
            KeyPreview = true;
            Text = "Jasność";

            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            Padding = new Padding(S(16), S(12), S(16), S(12));

            table = new TableLayoutPanel();
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.ColumnCount = 2;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Controls.Add(table);

            ShowMessage("Szukam monitorów…");
            ApplyTheme();

            Deactivate += delegate { HidePopup(); };
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) HidePopup(); };
        }

        int S(int px) { return (int)Math.Round(px * scale); }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW - nie pokazuj w Alt+Tab
                cp.ClassStyle |= 0x20000;  // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = Native.DWMWCP_ROUND;
            try { Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int)); }
            catch (Exception) { } // Windows 10 - zostają kwadratowe rogi
        }

        public void ShowPopup()
        {
            ApplyTheme();
            PlaceNearTray();
            Show();
            Activate();
        }

        void HidePopup()
        {
            if (!Visible) return;
            Hide();
            LastHidden = DateTime.Now;
        }

        void PlaceNearTray()
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int margin = S(12);
            Location = new Point(area.Right - Width - margin, area.Bottom - Height - margin);
        }

        public void ApplyTheme()
        {
            bool dark = Theme.AppsDark();
            Color back = dark ? Color.FromArgb(39, 39, 39) : Color.FromArgb(249, 249, 249);
            Color fore = dark ? Color.White : Color.FromArgb(26, 26, 26);
            BackColor = back;
            ForeColor = fore;
            SetColors(table, back, fore);
        }

        static void SetColors(Control parent, Color back, Color fore)
        {
            parent.BackColor = back;
            parent.ForeColor = fore;
            foreach (Control c in parent.Controls) SetColors(c, back, fore);
        }

        void ShowMessage(string text)
        {
            table.SuspendLayout();
            table.Controls.Clear();
            table.RowStyles.Clear();
            table.RowCount = 1;
            var label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Margin = new Padding(0, S(4), 0, S(4));
            table.Controls.Add(label, 0, 0);
            table.SetColumnSpan(label, 2);
            table.ResumeLayout();
            bars.Clear();
            values.Clear();
            idsKey = null;
        }

        public void SetDisplays(DisplayInfo[] displays)
        {
            if (displays.Length == 0)
            {
                ShowMessage("Nie znaleziono monitorów.");
                ApplyTheme();
                if (Visible) PlaceNearTray();
                return;
            }

            string key = string.Join("|", Array.ConvertAll(displays, d => d.Id + (d.Available ? "+" : "-")));
            if (key != idsKey) Rebuild(displays, key);

            foreach (DisplayInfo d in displays)
            {
                TrackBar bar = bars[d.Id];
                // Nie przestawiaj suwaka, który użytkownik właśnie trzyma myszą.
                if (!bar.Capture) bar.Value = Math.Max(0, Math.Min(100, d.Brightness));
                values[d.Id].Text = d.Available ? bar.Value + "%" : "brak DDC/CI";
            }
        }

        void Rebuild(DisplayInfo[] displays, string key)
        {
            table.SuspendLayout();
            table.Controls.Clear();
            table.RowStyles.Clear();
            bars.Clear();
            values.Clear();
            table.RowCount = displays.Length * 2;

            for (int i = 0; i < displays.Length; i++)
            {
                DisplayInfo d = displays[i];
                string id = d.Id;

                var name = new Label();
                name.Text = d.Name;
                name.AutoSize = true;
                name.Font = new Font(Font, FontStyle.Bold);
                name.Margin = new Padding(0, i == 0 ? 0 : S(10), 0, 0);

                var value = new Label();
                value.AutoSize = true;
                value.Anchor = AnchorStyles.Right;
                value.Margin = name.Margin;

                var bar = new TrackBar();
                bar.Minimum = 0;
                bar.Maximum = 100;
                bar.SmallChange = 1;
                bar.LargeChange = 10;
                bar.TickStyle = TickStyle.None;
                bar.Width = S(300);
                bar.Enabled = d.Available;
                bar.Margin = new Padding(0);
                bar.Scroll += delegate
                {
                    values[id].Text = bar.Value + "%";
                    service.RequestSet(id, bar.Value);
                };

                table.Controls.Add(name, 0, i * 2);
                table.Controls.Add(value, 1, i * 2);
                table.Controls.Add(bar, 0, i * 2 + 1);
                table.SetColumnSpan(bar, 2);

                bars[id] = bar;
                values[id] = value;
            }

            table.ResumeLayout();
            idsKey = key;
            ApplyTheme();
            if (Visible) PlaceNearTray();
        }
    }
}
