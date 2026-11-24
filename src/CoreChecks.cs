using System;
using System.IO;
using System.Text;

namespace KarincaKokpit
{
    public static class CoreChecks
    {
        private static int count;
        private static StringBuilder log;
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAIL " + name);
            count++;
            log.AppendLine("PASS " + name);
        }

        public static int Run(string isolatedRoot)
        {
            count = 0;
            log = new StringBuilder();
            string root = Path.Combine(Path.GetFullPath(isolatedRoot), "core-checks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string data = Path.Combine(root, "data with spaces");
                CockpitCore core = new CockpitCore(data);
                Check(core.State.Tools.Count == 1 && core.State.Tools[0].Id == "notes", "fresh state has Notes");
                Check(!Directory.Exists(core.WorkspaceDirectory), "no work created before explicit preparation");
                string exe = Path.Combine(root, "A & B (test).exe");
                File.WriteAllText(exe, "inert test fixture, never executed");
                ToolEntry tool = core.AddTool(exe);
                Check(core.State.Tools.Count == 2, "local executable added");
                Check(core.AddTool(exe.ToUpperInvariant()).Id == tool.Id && core.State.Tools.Count == 2, "duplicate path case insensitive");
                core.SetSleeping(tool.Id, true);
                CockpitCore reopened = new CockpitCore(data);
                Check(reopened.State.Tools[1].Sleeping, "sleep state persists");
                Check(reopened.Launch(tool.Id).Contains("beklemede"), "sleep blocks launch without executing fixture");
                reopened.SetSleeping(tool.Id, false);
                Check(!new CockpitCore(data).State.Tools[1].Sleeping, "restore persists");
                File.Delete(exe);
                Check(reopened.Launch(tool.Id).Contains("bulunamadı"), "missing target returns useful issue");
                bool rejected = false;
                try { reopened.AddTool(Path.Combine(root, "script.cmd")); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "shell script rejected");
                rejected = false;
                try { reopened.AddTool("https://example.com/app.exe"); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "URL rejected");
                rejected = false;
                try { reopened.AddTool("relative.exe"); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "relative path rejected");
                rejected = false;
                try { reopened.AddTool(@"C:relative.exe"); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "drive relative path rejected");
                rejected = false;
                try { reopened.AddTool(@"\\server\share\remote.exe"); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "network path rejected");
                string shortcut = Path.Combine(root, "test shortcut.lnk");
                File.WriteAllText(shortcut, "inert shortcut fixture, never executed");
                Check(reopened.AddTool(shortcut).Name == "test shortcut", "user chosen shortcut accepted without executing");
                Check(CockpitCore.QuoteArgument(@"E:\work space\notes.txt") == "\"E:\\work space\\notes.txt\"", "spaced argument quoted as one value");
                Check(CockpitCore.QuoteArgument("tail\\") == "\"tail\\\\\"", "trailing slash escaped");
                Check(CockpitCore.QuoteArgument("a\"b") == "\"a\\\"b\"", "embedded quote escaped");
                string note = reopened.PrepareNotes();
                File.WriteAllText(note, "user content: ğüşİ", Encoding.UTF8);
                reopened.PrepareNotes();
                Check(File.ReadAllText(note, Encoding.UTF8) == "user content: ğüşİ", "repeated preparation preserves existing notes");
                string cfg = Path.Combine(data, "kokpit.json");
                File.WriteAllText(cfg, "{bad json", Encoding.UTF8);
                CockpitCore recovered = new CockpitCore(data);
                Check(recovered.Notice.Contains("Son sağlam"), "corrupt current state recovered from backup");
                Check(Directory.GetFiles(data, "kokpit.json.bozuk-*").Length == 1, "corrupt input preserved");
                string brokenData = Path.Combine(root, "bad-schema");
                Directory.CreateDirectory(brokenData);
                File.WriteAllText(Path.Combine(brokenData, "kokpit.json"), "{\"Version\":42,\"Theme\":\"dark\",\"Tools\":[]}");
                CockpitCore badSchema = new CockpitCore(brokenData);
                Check(badSchema.State.Version == 1 && badSchema.State.Tools.Count == 1 && badSchema.Notice.Contains("korundu"), "unsupported schema preserved and fresh default created");
                string missingShape = Path.Combine(root, "missing-schema");
                Directory.CreateDirectory(missingShape);
                File.WriteAllText(Path.Combine(missingShape, "kokpit.json"), "{}");
                Check(new CockpitCore(missingShape).Notice.Contains("korundu"), "missing schema detected (known negative)");
                string[] invalidVersions = { "\"oops\"", "999999999999999" };
                string[] invalidLabels = { "typed field format", "typed field overflow" };
                for (int i = 0; i < invalidVersions.Length; i++)
                {
                    string invalidData = Path.Combine(root, "invalid-version-" + i);
                    Directory.CreateDirectory(invalidData);
                    string invalidJson = "{\"Version\":" + invalidVersions[i] + ",\"Theme\":\"dark\",\"Tools\":[]}";
                    File.WriteAllText(Path.Combine(invalidData, "kokpit.json"), invalidJson);
                    CockpitCore invalidState = new CockpitCore(invalidData);
                    string[] preserved = Directory.GetFiles(invalidData, "kokpit.json.bozuk-*");
                    Check(invalidState.State.Version == 1 && invalidState.State.Tools.Count == 1 && invalidState.Notice.Contains("korundu") && preserved.Length == 1 && File.ReadAllText(preserved[0]) == invalidJson && new CockpitCore(invalidData).State.Tools.Count == 1, invalidLabels[i] + " rejected, original preserved and usable state saved");
                }
                string unwritable = Path.Combine(root, "not-a-folder");
                File.WriteAllText(unwritable, "preserve me");
                CockpitCore blocked = new CockpitCore(unwritable);
                Check(blocked.Notice.Contains("kaydedilemez"), "storage failure is visible and nonfatal");
                blocked.SetSleeping("notes", true);
                Check(!blocked.State.Tools[0].Sleeping && File.ReadAllText(unwritable) == "preserve me", "failed mutation rolls back and existing content survives");
                log.AppendLine("CORE_CHECKS_OK " + count);
                File.WriteAllText(Path.Combine(root, "result.txt"), log.ToString(), Encoding.UTF8);
                Console.Write(log.ToString());
                Console.WriteLine("Evidence: " + root);
                return 0;
            }
            catch (Exception ex)
            {
                log.AppendLine(ex.GetType().Name + ": " + ex.Message);
                File.WriteAllText(Path.Combine(root, "result.txt"), log.ToString(), Encoding.UTF8);
                Console.Write(log.ToString());
                return 1;
            }
        }
    }
}
