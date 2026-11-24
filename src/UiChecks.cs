using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;

namespace KarincaKokpit
{
    static class UiChecks
    {
        static IEnumerable<Control> All(Control root)
        {
            foreach (Control child in root.Controls)
            { yield return child; foreach (Control sub in All(child)) yield return sub; }
        }
        static Button Button(Control root, string name) { return All(root).OfType<Button>().First(b => b.Text == name); }
        static void Check(bool condition, string message, StringBuilder log)
        { if (!condition) throw new InvalidOperationException(message); log.AppendLine("PASS " + message); }
        public static int Run(string baseDirectory)
        {
            string root = Path.Combine(baseDirectory, "checks", "ui-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(root); var log = new StringBuilder();
            try
            {
                foreach (float factor in new[] { 1f, 1.25f, 1.5f })
                {
                    var core = new CockpitCore(Path.Combine(root, "scale-" + factor.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    using (var form = new CockpitForm(core, true, factor))
                    {
                        form.ShowInTaskbar = false; form.Location = new Point(-12000,-12000); form.Show(); Application.DoEvents();
                        Rectangle original = Button(form,"Aç").Bounds;
                        Button(form,"Kâğıt").PerformClick(); Application.DoEvents();
                        Check(core.State.Theme == "light" && new CockpitCore(core.DataDirectory).State.Theme == "light", "theme click persists at " + factor, log);
                        Check(Button(form,"Aç").Bounds == original, "theme rebuild preserves scaled action bounds at " + factor, log);
                        Button(form,"Uyuyanlar").PerformClick(); Application.DoEvents();
                        Check(All(form).Any(c => c.Text == "Uyuyan araç yok."), "sleeping tab shows honest empty state at " + factor, log);
                        Button(form,"Araçlarım").PerformClick(); Application.DoEvents();
                        Check(Button(form,"Aç").Bounds == original, "tab rebuild preserves scaled action bounds at " + factor, log);
                        bool collapse = false; form.CollapseRequested += delegate { collapse = true; };
                        All(form).OfType<Button>().First(b => b.AccessibleName.StartsWith("Kokpiti küçült")).PerformClick();
                        Check(collapse, "collapse button invokes capsule lifecycle at " + factor, log);
                        form.Close();
                    }
                }
                var liveCore = new CockpitCore(Path.Combine(root,"launch"));
                var tool = liveCore.AddTool(Path.Combine(baseDirectory,"checks-bin","LaunchProbe.exe"));
                using (var form = new CockpitForm(liveCore,false))
                {
                    form.ShowInTaskbar = false; form.Location = new Point(-12000,-12000); form.Show(); Application.DoEvents();
                    Control row = All(form).OfType<Label>().First(l => l.Text == tool.Name).Parent;
                    Button(row,"Aç").PerformClick();
                    string proof = Path.Combine(liveCore.WorkspaceDirectory,"launch-proof.txt");
                    for (int i=0; i<50 && !File.Exists(proof); i++) { Application.DoEvents(); Thread.Sleep(100); }
                    Check(File.Exists(proof), "real Open click launches owned executable", log);
                    Check(File.ReadAllText(proof).Trim() == liveCore.WorkspaceDirectory, "launched executable receives prepared working directory", log);
                    form.Close();
                }
                log.AppendLine("UI_CHECKS_OK 17; scale checks simulate layout factors, not physical monitor DPI changes.");
                File.WriteAllText(Path.Combine(root,"result.txt"),log.ToString(),Encoding.UTF8); return 0;
            }
            catch (Exception ex) { log.AppendLine("FAIL " + ex); File.WriteAllText(Path.Combine(root,"result.txt"),log.ToString(),Encoding.UTF8); return 1; }
        }
    }
}
