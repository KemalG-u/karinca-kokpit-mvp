using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace KarincaKokpit
{
    public class ToolEntry
    {
        public string Id, Name, Path;
        public bool Sleeping;
    }

    public class CockpitState
    {
        public int Version = 1;
        public string Theme = "dark";
        public int CapsuleX = -1, CapsuleY = -1;
        public List<ToolEntry> Tools = new List<ToolEntry>();
    }

    public class CockpitCore
    {
        public readonly string DataDirectory, WorkspaceDirectory;
        public CockpitState State;
        public string Notice;
        private readonly string configPath;
        private readonly Dictionary<string, Process> started = new Dictionary<string, Process>();
        private bool lastSaveSucceeded;
        private bool persistenceBlocked;

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        internal static string NotesProgram { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe"); } }
        public CockpitCore(string dataDirectory)
        {
            DataDirectory = System.IO.Path.GetFullPath(dataDirectory);
            WorkspaceDirectory = System.IO.Path.Combine(DataDirectory, "calisma");
            configPath = System.IO.Path.Combine(DataDirectory, "kokpit.json");
            State = FreshState();
            Notice = "";
            try
            {
                Directory.CreateDirectory(DataDirectory);
                if (!File.Exists(configPath)) { Save(); return; }
                try { State = ReadState(configPath); }
                catch (Exception ex)
                {
                    if (!IsStorageOrDataError(ex)) throw;
                    // Preserve the exact rejected input before allowing any fresh state to be saved.
                    string rejected = configPath + ".bozuk-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".json";
                    File.Move(configPath, rejected);
                    bool recovered = false;
                    if (File.Exists(configPath + ".bak"))
                    {
                        try { State = ReadState(configPath + ".bak"); recovered = true; }
                        catch (Exception backupError) { if (!IsStorageOrDataError(backupError)) throw; }
                    }
                    if (!recovered) State = FreshState();
                    Save();
                    Notice = (recovered ? "Son sağlam düzen geri getirildi." : "Yeni bir düzen açıldı.") + " Okunamayan kayıt ayrıca korundu." + (lastSaveSucceeded ? "" : " Yeni düzen henüz kaydedilemedi.");
                }
            }
            catch (Exception ex)
            {
                if (!IsStorageOrDataError(ex)) throw;
                persistenceBlocked = true;
                Notice = "Kayıt klasörüne erişilemiyor. Önceki düzenin üzerine yazılmadı; bu oturumdaki değişiklikler kaydedilemez.";
            }
        }

        private static CockpitState FreshState()
        {
            CockpitState state = new CockpitState();
            state.Tools.Add(new ToolEntry { Id = "notes", Name = "Notlarım", Path = NotesProgram });
            return state;
        }

        private static bool IsStorageOrDataError(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is FormatException || ex is OverflowException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is System.Security.SecurityException || ex is NotSupportedException;
        }

        private static CockpitState ReadState(string path)
        {
            FileInfo info = new FileInfo(path);
            if (info.Length > 1024 * 1024) throw new InvalidDataException("Kayıt beklenenden büyük.");
            string json = File.ReadAllText(path, Encoding.UTF8);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> shape = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (shape == null || !shape.ContainsKey("Version") || !shape.ContainsKey("Tools") || !shape.ContainsKey("Theme")) throw new InvalidDataException("Kayıt yapısı eksik.");
            CockpitState state;
            try { state = serializer.Deserialize<CockpitState>(json); }
            catch (Exception conversionError)
            {
                // Framework TypeConverter can wrap malformed field conversions in plain Exception.
                // Confine this normalization to deserialization; do not swallow application bugs globally.
                if (conversionError is OutOfMemoryException || conversionError is StackOverflowException || conversionError is AccessViolationException || conversionError is System.Threading.ThreadAbortException || conversionError is AppDomainUnloadedException) throw;
                throw new InvalidDataException("Kayıt alanları beklenen biçimde değil.", conversionError);
            }
            ValidateState(state);
            foreach (ToolEntry tool in state.Tools) if (tool.Id == "notes") tool.Path = NotesProgram;
            return state;
        }

        private static void ValidateState(CockpitState state)
        {
            if (state == null || state.Version != 1 || state.Tools == null || state.Tools.Count > 200 || (state.Theme != "dark" && state.Theme != "light")) throw new InvalidDataException("Bu kayıt sürümü veya düzeni desteklenmiyor.");
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ToolEntry tool in state.Tools)
            {
                if (tool == null || tool.Id == null || !Regex.IsMatch(tool.Id, "^[a-zA-Z0-9_-]{1,64}$") || !ids.Add(tool.Id) || String.IsNullOrWhiteSpace(tool.Name) || tool.Name.Length > 100) throw new InvalidDataException("Araç kaydı okunamadı.");
                string normalized = NormalizeToolPath(tool.Path);
                if (!paths.Add(normalized)) throw new InvalidDataException("Yinelenen araç kaydı.");
                tool.Path = normalized;
            }
        }

        internal static string NormalizeToolPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !Regex.IsMatch(path, @"^[a-zA-Z]:[\\/]") || path.IndexOf(':', 2) >= 0)
                throw new ArgumentException("Bilgisayarındaki bir programı (.exe) veya kısayolu (.lnk) seç.");
            string normalized = System.IO.Path.GetFullPath(path);
            string ext = System.IO.Path.GetExtension(normalized);
            if (!String.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase) && !String.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Bu sürüm program (.exe) ve kısayol (.lnk) ekleyebilir.");
            return normalized;
        }

        public void Save()
        {
            lastSaveSucceeded = false;
            if (persistenceBlocked) { Notice = "Düzen kaydedilemedi. Kayıt klasörüne erişim gerekiyor."; return; }
            string temp = configPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                ValidateState(State);
                Directory.CreateDirectory(DataDirectory);
                string json = new JavaScriptSerializer().Serialize(State);
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(configPath)) File.Replace(temp, configPath, configPath + ".bak", true);
                else File.Move(temp, configPath);
                lastSaveSucceeded = true;
                Notice = "";
            }
            catch (Exception ex)
            {
                if (!IsStorageOrDataError(ex)) throw;
                Notice = "Düzen kaydedilemedi. Önceki kayıt korundu; klasör erişimini ve boş alanı kontrol et.";
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public ToolEntry AddTool(string filePath)
        {
            string path = NormalizeToolPath(filePath);
            if (!File.Exists(path)) throw new FileNotFoundException("Seçtiğin dosya bulunamadı. Programın güncel kısayolunu seç.");
            foreach (ToolEntry existing in State.Tools)
            {
                if (String.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    if (existing.Sleeping) SetSleeping(existing.Id, false);
                    else Notice = "Bu araç zaten kokpitte.";
                    return existing;
                }
            }
            if (State.Tools.Count >= 200) throw new ArgumentException("Bu ilk sürümde en fazla 200 araç saklanabilir.");
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name.Length > 100) name = name.Substring(0, 100);
            ToolEntry tool = new ToolEntry { Id = Guid.NewGuid().ToString("N"), Name = name, Path = path };
            State.Tools.Add(tool);
            Save();
            if (!lastSaveSucceeded) { State.Tools.Remove(tool); throw new IOException(Notice); }
            return tool;
        }

        public void SetSleeping(string id, bool value)
        {
            ToolEntry tool = FindTool(id);
            if (tool == null) { Notice = "Araç kaydı bulunamadı."; return; }
            bool old = tool.Sleeping;
            tool.Sleeping = value;
            Save();
            if (!lastSaveSucceeded) tool.Sleeping = old;
        }

        private ToolEntry FindTool(string id)
        {
            return State.Tools.Find(delegate(ToolEntry tool) { return tool.Id == id; });
        }

        internal string PrepareNotes()
        {
            Directory.CreateDirectory(WorkspaceDirectory);
            string note = System.IO.Path.Combine(WorkspaceDirectory, "Notlarim.txt");
            try
            {
                using (FileStream stream = new FileStream(note, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(true)))
                {
                    writer.WriteLine("Notlarım");
                    writer.WriteLine();
                    writer.WriteLine("Bu dosya Karınca Kokpit tarafından çalışma alanında hazırlandı.");
                    writer.WriteLine("Buraya yazdıkların bir sonraki açılışta da aynı dosyada kalır.");
                    writer.WriteLine();
                }
            }
            catch (IOException) { if (!File.Exists(note)) throw; }
            return note;
        }

        internal static string QuoteArgument(string value)
        {
            StringBuilder result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append(c); slashes = 0; continue; }
                result.Append('\\', slashes); slashes = 0; result.Append(c);
            }
            result.Append('\\', slashes * 2); result.Append('"');
            return result.ToString();
        }

        // 0: no living tracked process, 1: focused, 2: alive but Windows did not focus it.
        private int TryFocus(string id)
        {
            Process process;
            if (!started.TryGetValue(id, out process)) return 0;
            try
            {
                if (process.HasExited) { started.Remove(id); process.Dispose(); return 0; }
                process.Refresh();
                IntPtr handle = process.MainWindowHandle;
                uint owner;
                if (handle == IntPtr.Zero || GetWindowThreadProcessId(handle, out owner) == 0 || owner != process.Id) return 2;
                if (IsIconic(handle)) ShowWindow(handle, 9);
                return SetForegroundWindow(handle) ? 1 : 2;
            }
            catch (InvalidOperationException) { return 0; }
            catch (System.ComponentModel.Win32Exception) { return 2; }
        }

        public string Launch(string id)
        {
            ToolEntry tool = FindTool(id);
            if (tool == null) return "Araç bulunamadı. Listeyi yeniden aç.";
            if (tool.Sleeping) return "Bu araç beklemede. Önce kokpite geri al.";
            try
            {
                int focus = TryFocus(id);
                if (focus == 1) return tool.Name + " penceresine dönüldü.";
                if (focus == 2) return tool.Name + " zaten başlatıldı. Penceresine görev çubuğundan geçebilirsin.";
                string path = tool.Id == "notes" ? NotesProgram : NormalizeToolPath(tool.Path);
                if (!File.Exists(path)) return "Program dosyası bulunamadı. Güncel kısayolunu Araç ekle ile yeniden seç.";
                Directory.CreateDirectory(WorkspaceDirectory);
                string arguments = tool.Id == "notes" ? QuoteArgument(PrepareNotes()) : "";
                Process process = Process.Start(new ProcessStartInfo(path, arguments) { WorkingDirectory = WorkspaceDirectory, UseShellExecute = true });
                Process old;
                if (started.TryGetValue(id, out old)) old.Dispose();
                if (process != null) started[id] = process;
                else started.Remove(id);
                return tool.Name + " için açılış istendi. Çalışma klasörün hazır.";
            }
            catch (System.ComponentModel.Win32Exception) { return "Program açılamadı veya açılış iptal edildi. Dosyayı ve varsa Windows izin penceresini kontrol et."; }
            catch (Exception ex)
            {
                if (!IsStorageOrDataError(ex)) throw;
                return "Çalışma hazırlanamadı. Program dosyasını ve çalışma klasörüne erişimi kontrol et.";
            }
        }

        public string OpenWorkspace()
        {
            try
            {
                Directory.CreateDirectory(WorkspaceDirectory);
                Process.Start(new ProcessStartInfo(WorkspaceDirectory) { UseShellExecute = true });
                return "Çalışma klasörün açılıyor.";
            }
            catch (System.ComponentModel.Win32Exception) { return "Çalışma klasörü açılamadı."; }
            catch (Exception ex)
            {
                if (!IsStorageOrDataError(ex)) throw;
                return "Çalışma klasörü hazırlanamadı. Klasör erişimini kontrol et.";
            }
        }
    }
}
