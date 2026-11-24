using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace KarincaKokpit
{
    static class RadialChecks
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr handle,uint message,IntPtr w,IntPtr l);
        static int count;
        static StringBuilder log;
        static void Check(bool ok,string name) { if(!ok) throw new Exception("FAIL "+name); count++; log.AppendLine("PASS "+name); }
        static Point LogicalPoint(int index,float radius)
        { double a=(-90+index*60)*Math.PI/180; return new Point((int)Math.Round(240+Math.Cos(a)*radius),(int)Math.Round(240+Math.Sin(a)*radius)); }
        static Point Physical(RadialForm form,int i) { Point p=LogicalPoint(i,165); float s=form.Width/480f; return new Point((int)Math.Round(p.X*s),(int)Math.Round(p.Y*s)); }
        static System.Collections.Generic.IEnumerable<Control> Controls(Control root)
        { foreach(Control c in root.Controls) { yield return c; foreach(var sub in Controls(c)) yield return sub; } }
        public static int RunContext(string baseDirectory)
        {
            string root=Path.Combine(baseDirectory,"checks","context-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(root); count=0;log=new StringBuilder();
            CockpitContext context=null;
            try {
                context=new CockpitContext(new CockpitCore(Path.Combine(root,"data")));
                Application.DoEvents();
                var forms=Application.OpenForms.Cast<Form>().ToList();
                var wheel=forms.OfType<RadialForm>().Single();
                var capsule=forms.OfType<CapsuleForm>().Single();
                Check(wheel.Visible && !capsule.Visible,"startup shows wheel rather than settings panel");
                wheel.Choose(5); Application.DoEvents();
                var panel=Application.OpenForms.Cast<Form>().OfType<CockpitForm>().Single();
                Check(panel.Visible && !wheel.Visible,"settings sector opens separate management panel");
                Controls(panel).OfType<Button>().First(b=>b.AccessibleName.StartsWith("Kokpiti küçült")).PerformClick();
                Application.DoEvents();
                Check(wheel.Visible && !panel.Visible,"management return opens wheel");
                wheel.Close(); Application.DoEvents();
                Check(!wheel.IsDisposed && !wheel.Visible && capsule.Visible,"Alt-F4 close path preserves reusable wheel and restores capsule");
                capsule.ContextMenuStrip.Items[0].PerformClick(); Application.DoEvents();
                Check(wheel.Visible && !capsule.Visible,"capsule can reopen after native close request");
                wheel.Navigate(Keys.Escape);Application.DoEvents();
                Check(capsule.Visible && !wheel.Visible,"Escape restores persistent capsule");
                Check(PostMessage(capsule.Handle,0x8000+410,IntPtr.Zero,IntPtr.Zero),"launcher reveal message accepted by own window");
                Application.DoEvents();
                Check(wheel.Visible && !capsule.Visible,"launcher reveal restores hidden wheel");
                PostMessage(capsule.Handle,0x8000+410,IntPtr.Zero,IntPtr.Zero);Application.DoEvents();
                Check(wheel.Visible,"repeated launcher reveal never toggles visible wheel off");
                capsule.ContextMenuStrip.Items[1].PerformClick(); Application.DoEvents();
                Check(wheel.IsDisposed && capsule.IsDisposed && panel.IsDisposed,"explicit exit disposes only own forms");
                log.AppendLine("CONTEXT_CHECKS_OK "+count+"; native lifecycle inside sandbox, not user desktop acceptance.");
                File.WriteAllText(Path.Combine(root,"result.txt"),log.ToString(),Encoding.UTF8);return 0;
            } catch(Exception ex) { log.AppendLine(ex.ToString());File.WriteAllText(Path.Combine(root,"result.txt"),log.ToString(),Encoding.UTF8);return 1; }
            finally { if(context!=null) context.Dispose(); }
        }
        public static int Run(string baseDirectory)
        {
            string root=Path.Combine(baseDirectory,"checks","wheel-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(root); count=0; log=new StringBuilder();
            try {
                for(int i=0;i<6;i++) {
                    Point p=LogicalPoint(i,165); Check(RadialForm.HitLogical(p.X,p.Y)==i,"sector center "+i);
                }
                Check(RadialForm.HitLogical(240,240)==-2,"center is cancel or back, not launch");
                Check(RadialForm.HitLogical(240,146)==-1,"inner gap inert");
                Check(RadialForm.HitLogical(240,0)==-1,"outside inert");
                double gapAngle=-60*Math.PI/180;
                Check(RadialForm.HitLogical(240+(float)Math.Cos(gapAngle)*165,240+(float)Math.Sin(gapAngle)*165)==-1,"slice separator inert");
                var area=new Rectangle(-1280,40,1280,900);
                foreach(Point anchor in new[]{new Point(-1280,40),new Point(0,40),new Point(-1280,940),new Point(0,940)})
                    Check(area.Contains(RadialForm.Place(anchor,new Size(720,720),area)),"negative-monitor edge fit "+anchor);
                Check(new Rectangle(0,0,320,240).Contains(RadialForm.Place(Point.Empty,new Size(480,480),new Rectangle(0,0,320,240))),"small work area fit");
                var core=new CockpitCore(Path.Combine(root,"data"));
                using(var form=new RadialForm(core,true)) {
                    int actions=0; string id=null;
                    form.ActionRequested+=delegate(string action){actions++;id=action;};
                    Point note=Physical(form,0);
                    form.SelectAt(note); Check(actions==0 && form.Selected==0,"hover highlights without launch");
                    form.PointerUp(note); Check(actions==0,"opening mouse-up alone never launches");
                    form.PointerDown(note); form.PointerUp(note); Check(actions==1 && id=="tool:notes","one complete click dispatches exact tool once");
                    form.PointerDown(note); form.PointerUp(Physical(form,2)); Check(actions==1,"drag across sectors cancels action");
                    form.Choose(3); Check(actions==1,"unconnected dictation cannot dispatch");
                    form.ResetRoot(); form.Navigate(Keys.Right); Check(form.Selected==0,"keyboard begins with first enabled slot");
                    form.Navigate(Keys.Enter); Check(actions==2,"Enter dispatches selected action once");
                    form.Navigate(Keys.Right); form.Navigate(Keys.Right); form.Navigate(Keys.Right); Check(form.Selected==4,"keyboard skips disabled dictation");
                    form.Choose(1); Check(form.InTools,"tools opens second wheel, not settings panel");
                    Check(form.Items[0].Id=="tool:notes","subwheel contains actual tool");
                    form.Navigate(Keys.Escape); Check(!form.InTools,"Escape from subwheel returns home");
                    bool dismissed=false; form.DismissRequested+=delegate{dismissed=true;}; form.Navigate(Keys.Escape); Check(dismissed,"Escape at root dismisses");
                    core.SetSleeping("notes",true); form.ResetRoot();
                    Check(!form.Items.Any(t=>t.Id=="tool:notes"),"sleeping quick tool omitted");
                    form.Choose(1); Check(!form.Items.Any(t=>t.Id=="tool:notes"),"sleeping tool omitted from subwheel");
                    core.SetSleeping("notes",false);
                    for(int i=0;i<9;i++) {
                        string path=Path.Combine(root,"fixture"+i+".exe"); File.WriteAllText(path,"never execute fixture"); core.AddTool(path);
                    }
                    form.ResetRoot(); form.Choose(1);
                    var seen=new System.Collections.Generic.HashSet<string>();
                    for(int n=0;n<3;n++) { foreach(var t in form.Items.Where(t=>t.Id.StartsWith("tool:"))) seen.Add(t.Id); form.Choose(5); }
                    Check(seen.Count==10,"all tools reachable through page cycle");
                    Check(form.Items[0].Id=="tool:notes","last page wraps to first");
                }
                var liveCore=new CockpitCore(Path.Combine(root,"live"));
                var probe=liveCore.AddTool(Path.Combine(baseDirectory,"checks-bin","LaunchProbe.exe"));
                using(var form=new RadialForm(liveCore,true)) {
                    form.ActionRequested+=delegate(string action){if(action.StartsWith("tool:")) liveCore.Launch(action.Substring(5));};
                    form.Choose(1);
                    int index=form.Items.ToList().FindIndex(t=>t.Id=="tool:"+probe.Id);
                    Point target=Physical(form,index); form.PointerDown(target); form.PointerUp(target);
                    string proof=Path.Combine(liveCore.WorkspaceDirectory,"launch-proof.txt");
                    for(int i=0;i<40 && !File.Exists(proof);i++)Thread.Sleep(100);
                    Check(File.Exists(proof) && File.ReadAllText(proof).Trim()==liveCore.WorkspaceDirectory,"radial click starts real owned program with prepared directory");
                }
                log.AppendLine("WHEEL_CHECKS_OK "+count);
                File.WriteAllText(Path.Combine(root,"result.txt"),log.ToString(),Encoding.UTF8);return 0;
            } catch(Exception ex) { log.AppendLine(ex.ToString()); File.WriteAllText(Path.Combine(root,"result.txt"),log.ToString(),Encoding.UTF8);return 1; }
        }
    }
}
