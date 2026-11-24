using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;

namespace KarincaKokpit
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread] static int Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string root = AppDomain.CurrentDomain.BaseDirectory;
            if (args.Length > 0 && args[0] == "--check")
                return CoreChecks.Run(Path.Combine(root, "checks", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
            if (args.Length > 0 && args[0] == "--ui-check")
                return UiChecks.Run(root);
            if (args.Length > 0 && args[0] == "--wheel-check")
                return RadialChecks.Run(root);
            if (args.Length > 0 && args[0] == "--context-check")
                return RadialChecks.RunContext(root);
            if (args.Length > 1 && args[0] == "--render-wheel")
            { RadialForm.Render(Path.GetFullPath(args[1])); return 0; }
            if (args.Length > 1 && args[0] == "--render")
            {
                string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
                var preview = new CockpitCore(Path.Combine(output, "preview-data"));
                foreach (string theme in new[] { "dark", "light" })
                {
                    preview.State.Theme = theme;
                    using (var app = new CockpitForm(preview, true))
                    {
                        app.Show(); Application.DoEvents();
                        using (var bmp = new Bitmap(app.Width, app.Height))
                        { app.DrawToBitmap(bmp, new Rectangle(Point.Empty, app.Size)); bmp.Save(Path.Combine(output, "kokpit-" + theme + ".png")); }
                        app.Hide();
                    }
                    using (var cap = new CapsuleForm(preview))
                    {
                        cap.Show(); Application.DoEvents();
                        using (var bmp = new Bitmap(cap.Width, cap.Height))
                        { cap.DrawToBitmap(bmp, new Rectangle(Point.Empty, cap.Size)); bmp.Save(Path.Combine(output, "kapsul-" + theme + ".png")); }
                        cap.Hide();
                    }
                }
                return 0;
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\KarincaKokpitMvp-20261018", out created))
            {
                if (!created) { MessageBox.Show("Bir Karınca Kokpit zaten açık. Eski panel sürümü açıksa Çıkış düğmesiyle kapatıp çarkı yeniden aç. Çark zaten açıksa Ctrl+Alt+K ile çağırabilirsin.", "Karınca Kokpit"); return 0; }
                try { Application.Run(new CockpitContext(new CockpitCore(Path.Combine(root, "data")))); }
                catch (Exception e) { MessageBox.Show("Kokpit açılamadı. " + e.Message, "Karınca Kokpit"); return 1; }
            }
            return 0;
        }
    }

    sealed class Palette
    {
        public Color Bg, Surface, Elevated, Text, Muted, Border, Accent, OnAccent;
        public Palette(string theme)
        {
            bool light = theme == "light";
            Bg = C(light ? "F1ECE3" : "181614"); Surface = C(light ? "F9F6F0" : "201D1A");
            Elevated = C(light ? "EAE1D5" : "2A2622"); Text = C(light ? "241E19" : "EDE6DC");
            Muted = C(light ? "665A4F" : "BDB2A5"); Border = C(light ? "D5C8B8" : "433B34");
            Accent = C(light ? "A85F2F" : "E5925F"); OnAccent = C(light ? "FFFFFF" : "24180F");
        }
        static Color C(string s) { return ColorTranslator.FromHtml("#" + s); }
    }

    static class Shape
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath(); int d = radius * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        public static void Region(Form f, int radius)
        {
            if (f.Width < 1 || f.Height < 1) return;
            using (var p = Round(new Rectangle(0, 0, f.Width, f.Height), radius))
            { var old = f.Region; f.Region = new Region(p); if (old != null) old.Dispose(); }
        }
    }

    sealed class ActionButton : Button
    {
        public Palette Colors;
        public bool Primary;
        bool hover;
        public ActionButton(string text, Palette colors, bool primary)
        {
            Text = text; AccessibleName = text; Colors = colors; Primary = primary;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Font = new Font("Segoe UI", 10, FontStyle.Regular); Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            MouseEnter += delegate { hover = true; Invalidate(); }; MouseLeave += delegate { hover = false; Invalidate(); };
            GotFocus += delegate { Invalidate(); }; LostFocus += delegate { Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? Colors.Bg : Parent.BackColor);
            Color fill = Primary ? Colors.Accent : hover ? Colors.Elevated : Colors.Surface;
            using (var p = Shape.Round(new Rectangle(1, 1, Width - 3, Height - 3), 9))
            using (var b = new SolidBrush(fill))
            using (var pen = new Pen(Focused ? Colors.Accent : Primary ? Colors.Accent : Colors.Border, Focused ? 2 : 1))
            { e.Graphics.FillPath(b, p); e.Graphics.DrawPath(pen, p); }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Primary ? Colors.OnAccent : Colors.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    sealed class CapsuleForm : Form
    {
        readonly CockpitCore core;
        Palette colors;
        Point down, origin;
        bool moving, moved;
        readonly float scale;
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
        bool hotkey;
        readonly System.Windows.Forms.Timer hoverTimer = new System.Windows.Forms.Timer();
        readonly ToolTip feedback = new ToolTip();
        public bool HoverAllowed;
        public event EventHandler HoverRequested;
        public event EventHandler RevealRequested;
        public bool HotkeyAvailable { get { return hotkey; } }
        public event EventHandler ToggleRequested;
        public event EventHandler QuitRequested;
        public CapsuleForm(CockpitCore core)
        {
            this.core = core; colors = new Palette(core.State.Theme);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            Size = new Size(D(76), D(76));
            Text = "Karınca kapsülü"; AccessibleName = "Karınca kokpitini aç";
            StartPosition = FormStartPosition.Manual; DoubleBuffered = true; Cursor = Cursors.Hand;
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(core.State.CapsuleX < 0 ? area.Right - Width - 28 : core.State.CapsuleX,
                core.State.CapsuleY < 0 ? area.Bottom - Height - 24 : core.State.CapsuleY);
            ClampToScreen();
            using(var p=new GraphicsPath()) { p.AddEllipse(0,0,Width,Height); Region=new Region(p); }
            hoverTimer.Interval=350;
            hoverTimer.Tick+=delegate { hoverTimer.Stop(); if(HoverAllowed && !moving && Visible && ClientRectangle.Contains(PointToClient(Cursor.Position)) && HoverRequested!=null) HoverRequested(this,EventArgs.Empty); };
            MouseEnter+=delegate { if(HoverAllowed) hoverTimer.Start(); };
            MouseLeave+=delegate { hoverTimer.Stop(); };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Kokpiti aç", null, delegate { RaiseToggle(); });
            menu.Items.Add("Kokpitten çık", null, delegate { if (QuitRequested != null) QuitRequested(this, EventArgs.Empty); });
            ContextMenuStrip = menu;
            MouseDown += delegate(object sender, MouseEventArgs e) { hoverTimer.Stop(); if (e.Button == MouseButtons.Left) { down = Cursor.Position; origin = Location; moving = true; moved = false; Capture = true; } };
            MouseMove += delegate { if (moving) { var d = new Size(Cursor.Position.X - down.X, Cursor.Position.Y - down.Y); if (Math.Abs(d.Width) + Math.Abs(d.Height) > 5) moved = true; if (moved) Location = origin + d; } };
            MouseUp += delegate(object sender, MouseEventArgs e) {
                if (!moving || e.Button != MouseButtons.Left) return; moving = false; Capture = false;
                if (moved) { ClampToScreen(); core.State.CapsuleX = Left; core.State.CapsuleY = Top; core.Save(); }
                else RaiseToggle();
            };
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { RaiseToggle(); e.Handled = true; } };
        }
        public void ShowNotice(string text) { feedback.SetToolTip(this,text); feedback.Show(text,this,0,-55,6000); }
        protected override void Dispose(bool disposing) { if(disposing) { hoverTimer.Dispose(); feedback.Dispose(); } base.Dispose(disposing); }
        int D(int value) { return (int)Math.Round(value * scale); }
        protected override void OnShown(EventArgs e) { base.OnShown(e); hotkey = RegisterHotKey(Handle, 4817, 0x4003, (uint)Keys.K); }
        protected override void OnFormClosed(FormClosedEventArgs e) { if (hotkey) UnregisterHotKey(Handle, 4817); base.OnFormClosed(e); }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x8000+410) { if(RevealRequested!=null) RevealRequested(this,EventArgs.Empty); return; }
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 4817) { RaiseToggle(); return; } base.WndProc(ref m);
        }
        void RaiseToggle() { if (ToggleRequested != null) ToggleRequested(this, EventArgs.Empty); }
        public void RefreshTheme() { colors = new Palette(core.State.Theme); Invalidate(); }
        void ClampToScreen()
        {
            var area = Screen.FromRectangle(Bounds).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(colors.Surface);
            using (var pen = new Pen(colors.Accent)) g.DrawEllipse(pen,2,2,Width-5,Height-5);
            using (var b = new SolidBrush(colors.Accent)) g.FillEllipse(b,D(34),D(17),D(8),D(8));
            using (var font = new Font("Segoe UI",9,FontStyle.Bold))
                TextRenderer.DrawText(g,"Karınca",font,new Rectangle(0,D(30),Width,D(30)),colors.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
    }

    sealed class CockpitContext : ApplicationContext
    {
        readonly CapsuleForm capsule;
        readonly CockpitForm panel;
        readonly RadialForm wheel;
        bool exiting;
        DateTime lastDismiss=DateTime.MinValue;
        public CockpitContext(CockpitCore core)
        {
            capsule = new CapsuleForm(core); panel = new CockpitForm(core, false); wheel=new RadialForm(core,false);
            capsule.HoverAllowed=true;
            capsule.ToggleRequested += delegate { if(wheel.Visible) wheel.Dismiss(); else { panel.Hide(); ShowWheel(true); } };
            capsule.RevealRequested+=delegate { panel.Hide(); if(wheel.Visible) wheel.Activate(); else ShowWheel(true); };
            capsule.HoverRequested += delegate { if(!panel.Visible && !wheel.Visible && (DateTime.UtcNow-lastDismiss).TotalMilliseconds>600) ShowWheel(false); };
            capsule.QuitRequested += delegate { Quit(); };
            panel.CollapseRequested += delegate { panel.Hide(); ShowWheel(true); };
            panel.QuitRequested += delegate { Quit(); };
            panel.ThemeChanged += delegate { capsule.RefreshTheme(); wheel.ResetRoot(); };
            panel.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; panel.Hide(); capsule.Show(); lastDismiss=DateTime.UtcNow; } };
            wheel.DismissRequested+=delegate { lastDismiss=DateTime.UtcNow; if(!exiting) capsule.Show(); };
            wheel.FormClosing+=delegate(object sender,FormClosingEventArgs e) {
                if(!exiting && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; wheel.Dismiss(); }
            };
            capsule.FormClosing+=delegate(object sender,FormClosingEventArgs e) {
                if(!exiting && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; capsule.BeginInvoke((Action)Quit); }
            };
            wheel.ActionRequested+=delegate(string id) {
                wheel.Dismiss();
                if(id=="settings" || id=="add") { ShowPanel(); if(id=="add") panel.RequestAddTool(); return; }
                string result=id=="workspace"?core.OpenWorkspace():id.StartsWith("tool:")?core.Launch(id.Substring(5)):"Bu seçenek henüz bağlı değil.";
                panel.ShowNotice(result); capsule.ShowNotice(result);
            };
            capsule.Show();
            if (!capsule.HotkeyAvailable) { panel.ShowNotice("Klavye kısayolu başka bir uygulamada. Kapsüle tıklayarak açabilirsin."); capsule.ShowNotice("Ctrl+Alt+K kullanımda. Kapsüle tıklayarak açabilirsin."); }
            ShowWheel(true);
        }
        void ShowWheel(bool activate)
        {
            Point anchor=new Point(capsule.Left+capsule.Width/2,capsule.Top+capsule.Height/2);
            capsule.Hide(); wheel.OpenAt(anchor,activate);
        }
        void ShowPanel()
        {
            var area = Screen.FromControl(capsule).WorkingArea;
            panel.Location = new Point(Math.Max(area.Left, Math.Min(capsule.Right - panel.Width, area.Right - panel.Width)),
                Math.Max(area.Top, Math.Min(capsule.Top - panel.Height - 12, area.Bottom - panel.Height)));
            panel.RefreshTools(); panel.Show(); panel.Activate();
        }
        void Quit() { if (exiting) return; exiting = true; wheel.Close(); panel.Close(); capsule.Close(); wheel.Dispose(); panel.Dispose(); capsule.Dispose(); ExitThread(); }
    }

    sealed class CockpitForm : Form
    {
        readonly CockpitCore core;
        readonly bool preview;
        Palette colors;
        FlowLayoutPanel toolList;
        Label status;
        bool sleeping;
        string statusText;
        readonly float layoutScale;
        public event EventHandler CollapseRequested;
        public event EventHandler QuitRequested;
        public event EventHandler ThemeChanged;
        public CockpitForm(CockpitCore core, bool preview) : this(core, preview, 0) { }
        internal CockpitForm(CockpitCore core, bool preview, float testScale)
        {
            this.core = core; this.preview = preview; colors = new Palette(core.State.Theme);
            Text = "Karınca · Düzenle"; FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            using (var g = CreateGraphics()) layoutScale = testScale > 0 ? testScale : g.DpiX / 96f;
            Size = new Size((int)Math.Round(504 * layoutScale), (int)Math.Round(662 * layoutScale));
            var area = Screen.PrimaryScreen.WorkingArea;
            if (Height > area.Height - 88) { Height = Math.Max(300, area.Height - 88); AutoScroll = true; AutoScrollMinSize = new Size(0, (int)Math.Round(662 * layoutScale)); }
            ShowInTaskbar = true; TopMost = false; DoubleBuffered = true; KeyPreview = true;
            Font = new Font("Segoe UI", 10); BackColor = colors.Bg; ForeColor = colors.Text;
            statusText = String.IsNullOrEmpty(core.Notice) ? "Çalışma alanın, ilk açılışta hazırlanır." : core.Notice;
            Build(); Shape.Region(this, (int)(20 * layoutScale));
            SizeChanged += delegate { Shape.Region(this, (int)(20 * layoutScale)); };
            AllowDrop = true;
            DragEnter += delegate(object sender, DragEventArgs e) { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            DragDrop += delegate(object sender, DragEventArgs e) { var paths = e.Data.GetData(DataFormats.FileDrop) as string[]; if (paths != null && paths.Length == 1) AddPath(paths[0]); else SetStatus("Bir seferde bir program veya kısayol bırakabilirsin."); };
        }
        Label TextAt(Control parent, string text, int x, int y, int w, int h, float size, bool bold, Color color)
        {
            var l = new Label { Text = text, Location = new Point(x,y), Size = new Size(w,h), AutoSize = false,
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color,
                BackColor = Color.Transparent, UseMnemonic = false };
            parent.Controls.Add(l); return l;
        }
        ActionButton ButtonAt(Control parent, string text, int x, int y, int w, int h, bool primary, Action action)
        {
            var b = new ActionButton(text, colors, primary) { Location = new Point(x,y), Size = new Size(w,h) };
            b.Click += delegate { action(); }; parent.Controls.Add(b); return b;
        }
        void Build()
        {
            SuspendLayout();
            while (Controls.Count > 0) { var c = Controls[0]; Controls.Remove(c); c.Dispose(); }
            BackColor = colors.Bg; ForeColor = colors.Text;
            var title = TextAt(this, "Karınca", 26, 22, 210, 34, 20, true, colors.Text);
            TextAt(this, "Araçlar ve ayarlar", 28, 61, 250, 23, 10, false, colors.Muted);
            // Dragging the title uses only this app's own coordinates.
            Point drag = Point.Empty; bool dragging = false;
            title.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; drag = e.Location; title.Capture = true; } };
            title.MouseMove += delegate(object s, MouseEventArgs e) { if (dragging) Location = new Point(Left + e.X - drag.X, Top + e.Y - drag.Y); };
            title.MouseUp += delegate { dragging = false; title.Capture = false; };
            ButtonAt(this, core.State.Theme == "light" ? "Koyu" : "Kâğıt", 340, 29, 76, 36, false, ToggleTheme);
            var collapse = ButtonAt(this, "⌄", 426, 29, 48, 36, false, Collapse);
            collapse.AccessibleName = "Kokpiti küçült; kapsül açık kalır";
            TextAt(this, "Kokpitini düzenle", 26, 107, 430, 33, 17, true, colors.Text);
            TextAt(this, "Çarkta yer alacak araçlarını buradan seç.", 28, 145, 446, 26, 11, false, colors.Muted);
            ButtonAt(this, "Araçlarım", 26, 191, 111, 36, !sleeping, delegate { sleeping = false; Build(); });
            int count = core.State.Tools.Count(t => t.Sleeping);
            ButtonAt(this, "Uyuyanlar" + (count > 0 ? " · " + count : ""), 145, 191, 141, 36, sleeping, delegate { sleeping = true; Build(); });
            ButtonAt(this, "+ Araç ekle", 342, 191, 132, 36, false, PickTool);
            toolList = new FlowLayoutPanel { Location = new Point(26, 244), Size = new Size(452, 216),
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = colors.Bg, Padding = Padding.Empty };
            Controls.Add(toolList); FillTools();
            var dictation = new Panel { Location = new Point(26, 476), Size = new Size(448, 78), BackColor = colors.Surface };
            Controls.Add(dictation);
            TextAt(dictation, "Dikte", 16, 12, 75, 24, 11, true, colors.Text);
            TextAt(dictation, "Henüz bağlı değil", 233, 13, 199, 23, 10, false, colors.Muted).TextAlign = ContentAlignment.TopRight;
            TextAt(dictation, "Konuşma alanı da bu kokpitte yerini alacak.", 16, 43, 422, 23, 10, false, colors.Muted);
            status = TextAt(this, statusText, 28, 566, 446, 40, 10, false, colors.Muted);
            ButtonAt(this, "Çalışma klasörü", 26, 610, 162, 32, false, delegate { RunAction(delegate { return core.OpenWorkspace(); }); });
            TextAt(this, "Ctrl + Alt + K", 211, 617, 157, 23, 9, false, colors.Muted);
            ButtonAt(this, "Çıkış", 398, 610, 76, 32, false, delegate { if (QuitRequested != null) QuitRequested(this, EventArgs.Empty); });
            ScaleLayout(this, layoutScale);
            ResumeLayout();
        }
        static void ScaleLayout(Control parent, float factor)
        {
            if (Math.Abs(factor - 1) < 0.001) return;
            foreach (Control child in parent.Controls)
            {
                child.SuspendLayout();
                var r = child.Bounds;
                child.Bounds = new Rectangle((int)Math.Round(r.X * factor), (int)Math.Round(r.Y * factor), (int)Math.Round(r.Width * factor), (int)Math.Round(r.Height * factor));
                var m = child.Margin; child.Margin = new Padding((int)(m.Left*factor), (int)(m.Top*factor), (int)(m.Right*factor), (int)(m.Bottom*factor));
                ScaleLayout(child, factor); child.ResumeLayout();
            }
        }
        void FillTools()
        {
            foreach (var tool in core.State.Tools.Where(t => t.Sleeping == sleeping))
            {
                ToolEntry entry = tool;
                var row = new Panel { Size = new Size(430, 94), Margin = new Padding(0,0,0,9), BackColor = colors.Surface };
                toolList.Controls.Add(row);
                TextAt(row, entry.Name, 16, 13, 245, 27, 12, true, colors.Text);
                string desc = entry.Id == "notes" ? "Not Defteri + not dosyan" : "Kendi eklediğin program";
                TextAt(row, desc, 16, 47, 260, 34, 9.5f, false, colors.Muted);
                if (sleeping)
                    ButtonAt(row, "Geri getir", 292, 24, 121, 40, true, delegate { SleepTool(entry, false); });
                else
                {
                    ButtonAt(row, "Aç", 290, 14, 68, 38, true, delegate { RunAction(delegate { return core.Launch(entry.Id); }); });
                    var sleep = ButtonAt(row, "···", 366, 14, 47, 38, false, delegate { ToolMenu(entry, row); });
                    sleep.AccessibleName = entry.Name + " seçenekleri";
                }
            }
            if (toolList.Controls.Count == 0)
            {
                var empty = new Panel { Size = new Size(430,130), BackColor = colors.Surface };
                TextAt(empty, sleeping ? "Uyuyan araç yok." : "İlk aracını ekleyebilirsin.", 18, 20, 390, 30, 12, true, colors.Text);
                TextAt(empty, sleeping ? "Uyuttuğun araçların hazırlığı korunur.\nİstediğinde buradan geri getirirsin." : "Bir program veya kısayol seç.\nUyuttuğun araçlar diğer sekmede seni bekler.", 18, 60, 390, 60, 10, false, colors.Muted);
                toolList.Controls.Add(empty);
            }
        }
        void ToolMenu(ToolEntry entry, Control row)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Uyut — listeden kaldır, hazırlığını koru", null, delegate { SleepTool(entry, true); });
            menu.Closed += delegate { menu.Dispose(); };
            menu.Show(row, new Point(180,55));
        }
        void SleepTool(ToolEntry entry, bool value)
        {
            try { core.SetSleeping(entry.Id, value); statusText = !String.IsNullOrEmpty(core.Notice) ? core.Notice :
                value ? entry.Name + " uyutuldu. Uyuyanlar’dan geri getirebilirsin." : entry.Name + " tekrar araçlarında."; Build(); }
            catch (Exception e) { SetStatus("Değişiklik yapılamadı. " + e.Message); }
        }
        void PickTool()
        {
            using (var picker = new OpenFileDialog { Title = "Bağlamak istediğin programı veya kısayolu seç", Filter = "Program veya kısayol (*.exe;*.lnk)|*.exe;*.lnk", CheckFileExists = true, DereferenceLinks = false, Multiselect = false })
            { if (picker.ShowDialog(this) == DialogResult.OK) AddPath(picker.FileName); }
        }
        public void RequestAddTool() { PickTool(); }
        public void RefreshTools() { Build(); }
        void AddPath(string path)
        {
            try { var entry = core.AddTool(path); sleeping = false; statusText = String.IsNullOrEmpty(core.Notice) ? entry.Name + " eklendi. Açmak için seçebilirsin." : core.Notice; Build(); }
            catch (Exception e) { SetStatus("Araç eklenemedi. " + e.Message); }
        }
        void RunAction(Func<string> action)
        {
            if (preview) return;
            try { SetStatus(action()); } catch (Exception e) { SetStatus("İşlem tamamlanamadı. " + e.Message); }
        }
        void SetStatus(string message) { statusText = message; if (status != null) status.Text = message; }
        public void ShowNotice(string message) { SetStatus(message); }
        void ToggleTheme()
        {
            core.State.Theme = core.State.Theme == "light" ? "dark" : "light"; core.Save(); colors = new Palette(core.State.Theme);
            if (!String.IsNullOrEmpty(core.Notice)) statusText = core.Notice;
            Build(); if (ThemeChanged != null) ThemeChanged(this, EventArgs.Empty);
        }
        void Collapse() { if (CollapseRequested != null) CollapseRequested(this, EventArgs.Empty); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Collapse(); return true; }
            if (keyData == (Keys.Control | Keys.O)) { PickTool(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Shape.Round(new Rectangle(1,1,Width-3,Height-3),(int)(19*layoutScale)))
            using (var pen = new Pen(colors.Border)) e.Graphics.DrawPath(pen,p);
        }
    }
}
