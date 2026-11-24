using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace KarincaKokpit
{
    sealed class WheelItem
    {
        public string Id, Title, Detail, Icon;
        public bool Enabled = true;
        public WheelItem(string id, string title, string detail, string icon)
        { Id=id; Title=title; Detail=detail; Icon=icon; }
    }

    sealed class RadialForm : Form
    {
        const float Center=240, Outer=226, Inner=102;
        readonly CockpitCore core;
        readonly bool preview;
        readonly float systemScale;
        float scale;
        Palette colors;
        List<WheelItem> items;
        int selected=-1, pressed=-1, page;
        bool toolsPage, noActivate, closing, pointerSeen, keyboardNavigation;
        readonly Timer leaveTimer = new Timer();
        DateTime outsideSince=DateTime.MaxValue;
        public event EventHandler DismissRequested;
        public event Action<string> ActionRequested;
        public string LastMessage = "";
        public bool InTools { get { return toolsPage; } }
        public int Selected { get { return selected; } }
        public IList<WheelItem> Items { get { return items.AsReadOnly(); } }
        public RadialForm(CockpitCore core, bool preview)
        {
            this.core=core; this.preview=preview;
            Text="Karınca · Seçim çarkı"; AccessibleName="Karınca araç seçim çarkı";
            FormBorderStyle=FormBorderStyle.None; StartPosition=FormStartPosition.Manual;
            // Keep the open wheel reachable by Alt+Tab and desktop accessibility tools.
            // Dismiss still hides it; the resting capsule stays out of the taskbar.
            ShowInTaskbar=true; TopMost=true; AutoScaleMode=AutoScaleMode.None;
            DoubleBuffered=true; KeyPreview=true;
            using(var g=CreateGraphics()) systemScale=g.DpiX/96f;
            ResizeWheel(systemScale); ResetRoot();
            leaveTimer.Interval=120;
            leaveTimer.Tick+=delegate {
                if(preview || !Visible || keyboardNavigation) return;
                var p=PointToClient(Cursor.Position);
                double dx=p.X-Width/2.0,dy=p.Y-Height/2.0;
                if(dx*dx+dy*dy < (Width/2.0+18)*(Width/2.0+18)) { outsideSince=DateTime.MaxValue; pointerSeen=true; }
                else if(!pointerSeen) return;
                else if(outsideSince==DateTime.MaxValue) outsideSince=DateTime.UtcNow;
                else if((DateTime.UtcNow-outsideSince).TotalMilliseconds>450) Dismiss();
            };
        }
        protected override bool ShowWithoutActivation { get { return noActivate; } }
        void ResizeWheel(float factor)
        {
            scale=factor; Size=new Size((int)Math.Round(480*scale),(int)Math.Round(480*scale));
            using(var p=new GraphicsPath()) {
                p.AddEllipse(1,1,Width-2,Height-2); var old=Region; Region=new Region(p); if(old!=null) old.Dispose();
            }
        }
        public static Rectangle Place(Point anchor, Size size, Rectangle area)
        {
            int w=Math.Min(size.Width,area.Width),h=Math.Min(size.Height,area.Height);
            int x=Math.Max(area.Left,Math.Min(anchor.X-w/2,area.Right-w));
            int y=Math.Max(area.Top,Math.Min(anchor.Y-h/2,area.Bottom-h));
            return new Rectangle(x,y,w,h);
        }
        public void OpenAt(Point anchor, bool activate)
        {
            closing=false; noActivate=!activate; ResetRoot();
            Rectangle area=Screen.FromPoint(anchor).WorkingArea;
            float fit=Math.Min(systemScale,Math.Min(area.Width-16,area.Height-16)/480f);
            ResizeWheel(Math.Max(0.25f,fit));
            Location=Place(anchor,Size,area).Location;
            outsideSince=activate?DateTime.MaxValue:DateTime.UtcNow.AddMilliseconds(350);
            pointerSeen=!activate; keyboardNavigation=activate; Show();
            if(activate) Activate();
            if(!preview) leaveTimer.Start();
        }
        public void ResetRoot()
        {
            toolsPage=false; page=0; colors=new Palette(core.State.Theme); BackColor=colors.Bg;
            var notes=core.State.Tools.FirstOrDefault(t=>t.Id=="notes" && !t.Sleeping);
            items=new List<WheelItem> {
                notes!=null ? new WheelItem("tool:notes","Notlarım","Not Defteri ve\nçalışma dosyan","note") : new WheelItem("tools","Araç seç","Eklediğin araçlara\ngit","apps"),
                new WheelItem("tools","Araçlarım","Kendi araçlarını\nseç","apps"),
                new WheelItem("workspace","Çalışma alanı","Dosyalarının\nklasörünü aç","folder"),
                new WheelItem("dictation","Dikte","Henüz bağlı değil","mic") {Enabled=false},
                new WheelItem("add","Araç ekle","Program veya\nkısayol bağla","plus"),
                new WheelItem("settings","Düzenle","Araçlar, görünüm\nve ayarlar","gear")
            };
            selected=-1; pressed=-1; Invalidate();
        }
        void LoadTools()
        {
            toolsPage=true; var tools=core.State.Tools.Where(t=>!t.Sleeping).ToList();
            int pages=Math.Max(1,(tools.Count+3)/4); page=Math.Max(0,Math.Min(page,pages-1));
            items=new List<WheelItem>();
            foreach(var tool in tools.Skip(page*4).Take(4))
                items.Add(new WheelItem("tool:"+tool.Id,tool.Name,"Aracı aç","note"));
            while(items.Count<4) items.Add(new WheelItem("empty","Boş yer","Araç ekleyebilirsin","empty") {Enabled=false});
            items.Add(new WheelItem("add","Araç ekle","Yeni bir program\nveya kısayol","plus"));
            items.Add(pages>1 ? new WheelItem("next","Sonrakiler","Sayfa "+(page+1)+" / "+pages,"next") : new WheelItem("back","Geri","Ana çarka dön","back"));
            selected=-1; pressed=-1; Invalidate();
        }
        internal static int HitLogical(float x,float y)
        {
            float dx=x-Center,dy=y-Center; double r=Math.Sqrt(dx*dx+dy*dy);
            if(r<=80) return -2;
            if(r<Inner || r>Outer) return -1;
            double angle=(Math.Atan2(dy,dx)*180/Math.PI+90+360)%360;
            int index=(int)Math.Floor((angle+30)/60)%6;
            double offset=((angle-index*60+540)%360)-180;
            return Math.Abs(offset)<=28 ? index : -1;
        }
        internal int HitAt(Point p) { return HitLogical(p.X/scale,p.Y/scale); }
        internal void SelectAt(Point p) { selected=HitAt(p); Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e)
        { base.OnMouseMove(e); pointerSeen=true; keyboardNavigation=false; int hit=HitAt(e.Location); if(hit!=selected) { selected=hit; Cursor=hit==-1?Cursors.Default:Cursors.Hand; Invalidate(); } }
        internal void PointerDown(Point p) { pressed=HitAt(p); }
        internal void PointerUp(Point p) { int hit=HitAt(p); if(hit==pressed) Choose(hit); pressed=-1; }
        protected override void OnMouseDown(MouseEventArgs e)
        { base.OnMouseDown(e); if(e.Button==MouseButtons.Left) PointerDown(e.Location); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); if(e.Button!=MouseButtons.Left) return;
            PointerUp(e.Location);
        }
        internal void Choose(int index)
        {
            if(index==-2) { if(toolsPage) ResetRoot(); else Dismiss(); return; }
            if(index<0 || index>=items.Count || !items[index].Enabled) return;
            string id=items[index].Id;
            if(id=="tools") { LoadTools(); return; }
            if(id=="back") { ResetRoot(); return; }
            if(id=="next") { page++; int n=core.State.Tools.Count(t=>!t.Sleeping); if(page>=(n+3)/4)page=0; LoadTools(); return; }
            if(ActionRequested!=null) ActionRequested(id);
        }
        internal bool Navigate(Keys key)
        {
            keyboardNavigation=true;
            if(key==Keys.Escape) { if(toolsPage) ResetRoot(); else Dismiss(); return true; }
            if(key==Keys.Enter || key==Keys.Space) { Choose(selected); return true; }
            if(key==Keys.Right || key==Keys.Down || key==Keys.Tab || key==Keys.Left || key==Keys.Up || key==(Keys.Shift|Keys.Tab)) {
                int direction=(key==Keys.Left || key==Keys.Up || key==(Keys.Shift|Keys.Tab))?-1:1;
                int next=selected<0?(direction==1?0:5):(selected+direction+6)%6;
                for(int tries=0;tries<6 && !items[next].Enabled;tries++) next=(next+direction+6)%6;
                selected=next; Invalidate(); return true;
            }
            return false;
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        { return Navigate(keyData) || base.ProcessCmdKey(ref msg,keyData); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); if(!preview && Visible) Dismiss(); }
        public void Dismiss()
        {
            if(closing)return; closing=true; leaveTimer.Stop(); Hide();
            if(DismissRequested!=null) DismissRequested(this,EventArgs.Empty);
            closing=false;
        }
        protected override void Dispose(bool disposing) { if(disposing) leaveTimer.Dispose(); base.Dispose(disposing); }
        static PointF Polar(float radius,float angle)
        { double a=angle*Math.PI/180; return new PointF(Center+(float)Math.Cos(a)*radius,Center+(float)Math.Sin(a)*radius); }
        static GraphicsPath Wedge(int i)
        {
            float start=-118+i*60; var p=new GraphicsPath();
            p.AddArc(Center-Outer,Center-Outer,Outer*2,Outer*2,start,56);
            p.AddArc(Center-Inner,Center-Inner,Inner*2,Inner*2,start+56,-56); p.CloseFigure(); return p;
        }
        void Write(Graphics g,string text,RectangleF box,float size,bool bold,Color color)
        {
            using(var font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))
            using(var brush=new SolidBrush(color))
            using(var format=new StringFormat { Alignment=StringAlignment.Center, LineAlignment=StringAlignment.Center, Trimming=StringTrimming.EllipsisCharacter })
                g.DrawString(text,font,brush,box,format);
        }
        void DrawIcon(Graphics g,string name,PointF p,Color color)
        {
            var state=g.Save(); g.TranslateTransform(p.X,p.Y);
            using(var pen=new Pen(color,1.7f)) {
                pen.StartCap=LineCap.Round; pen.EndCap=LineCap.Round; pen.LineJoin=LineJoin.Round;
                if(name=="note") { g.DrawRectangle(pen,-9,-12,18,24); g.DrawLine(pen,-5,-5,5,-5); g.DrawLine(pen,-5,1,5,1); g.DrawLine(pen,-5,7,2,7); }
                else if(name=="apps") { for(int a=0;a<2;a++) for(int b=0;b<2;b++) g.DrawRectangle(pen,-11+a*13,-11+b*13,9,9); }
                else if(name=="folder") { g.DrawLines(pen,new[]{new PointF(-13,10),new PointF(-13,-10),new PointF(-3,-10),new PointF(1,-5),new PointF(13,-5),new PointF(13,10),new PointF(-13,10)}); g.DrawLine(pen,-12,-2,12,-2); }
                else if(name=="mic") { using(var r=Shape.Round(new Rectangle(-5,-12,10,18),5)) g.DrawPath(pen,r); g.DrawArc(pen,-10,-6,20,18,0,180); g.DrawLine(pen,0,12,0,17); g.DrawLine(pen,-5,17,5,17); }
                else if(name=="plus") { g.DrawLine(pen,-11,0,11,0); g.DrawLine(pen,0,-11,0,11); }
                else if(name=="gear") { g.DrawEllipse(pen,-9,-9,18,18); g.DrawEllipse(pen,-3,-3,6,6); for(int n=0;n<8;n++) { double a=n*Math.PI/4; g.DrawLine(pen,(float)Math.Cos(a)*10,(float)Math.Sin(a)*10,(float)Math.Cos(a)*14,(float)Math.Sin(a)*14); } }
                else if(name=="next" || name=="back") { int d=name=="next"?1:-1; g.DrawLine(pen,-10*d,0,10*d,0); g.DrawLine(pen,4*d,-6,10*d,0); g.DrawLine(pen,4*d,6,10*d,0); }
                else { pen.DashStyle=DashStyle.Dot; g.DrawEllipse(pen,-9,-9,18,18); }
            }
            g.Restore(state);
        }
        public void PaintWheel(Graphics g)
        {
            var state=g.Save(); g.ScaleTransform(scale,scale);
            g.SmoothingMode=SmoothingMode.AntiAlias; g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using(var b=new SolidBrush(colors.Bg)) g.FillEllipse(b,1,1,478,478);
            using(var pen=new Pen(colors.Border,1)) g.DrawEllipse(pen,2,2,476,476);
            for(int i=0;i<6;i++) {
                bool active=selected==i && items[i].Enabled;
                Color text=active?colors.OnAccent:items[i].Enabled?colors.Text:colors.Muted;
                using(var p=Wedge(i)) using(var b=new SolidBrush(active?colors.Accent:colors.Surface)) using(var pen=new Pen(active?colors.Accent:colors.Border,1))
                { g.FillPath(b,p); g.DrawPath(pen,p); }
                PointF label=Polar(163,-90+i*60);
                DrawIcon(g,items[i].Icon,new PointF(label.X,label.Y-16),text);
                Write(g,items[i].Title,new RectangleF(label.X-64,label.Y+6,128,40),14,true,text);
                if(!items[i].Enabled && items[i].Id=="dictation") Write(g,"Henüz bağlı değil",new RectangleF(label.X-68,label.Y+39,136,18),11,false,colors.Muted);
            }
            using(var b=new SolidBrush(selected==-2?colors.Elevated:colors.Surface)) g.FillEllipse(b,160,160,160,160);
            using(var pen=new Pen(colors.Border,1)) g.DrawEllipse(pen,160,160,160,160);
            bool hasSelection=selected>=0 && selected<items.Count;
            string heading=hasSelection?items[selected].Title:toolsPage?"Araçlarım":"Karınca";
            string detail=hasSelection?items[selected].Detail:toolsPage?"Ortaya tıkla\nana çarka dön":"Bir yöne git\ntıkla ve aç";
            Write(g,heading,new RectangleF(170,195,140,38),hasSelection?17:22,true,colors.Text);
            Write(g,detail,new RectangleF(170,236,140,44),12,false,colors.Muted);
            Write(g,toolsPage?"Esc · geri":"Esc · kapat",new RectangleF(175,282,130,20),10,false,colors.Muted);
            g.Restore(state);
        }
        protected override void OnPaint(PaintEventArgs e) { PaintWheel(e.Graphics); }
        public static void Render(string output)
        {
            Directory.CreateDirectory(output); var core=new CockpitCore(Path.Combine(output,"wheel-data"));
            foreach(string theme in new[]{"dark","light"}) {
                core.State.Theme=theme;
                using(var form=new RadialForm(core,true)) {
                    foreach(bool selected in new[]{false,true}) {
                        if(selected) form.selected=0;
                        using(var bmp=new Bitmap(form.Width,form.Height)) {
                            using(var g=Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); form.PaintWheel(g); }
                            bmp.Save(Path.Combine(output,"cark-"+theme+(selected?"-secim":"")+".png"));
                        }
                    }
                }
            }
        }
    }
}
