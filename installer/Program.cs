using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CTTestInstaller {

    // Installs the official Casualties: Together 5.5.4 plus the CT Fixes plugin (prosthesis repair for
    // clients, scatter rule count) into a Casualties: Unknown folder, with a backup that "Restore" puts back.
    static class Program {
        const string GameExe = "CasualtiesUnknown.exe";
        const string BackupRoot = "CT-test-backup";
        internal static readonly string SilentLogPath = Path.Combine(Path.GetTempPath(), "ct-test-installer.log");

        [STAThread]
        static int Main(string[] args) {
            // silent mode for scripting/testing:  --install "<game dir>"  |  --restore "<game dir>"
            if (args.Length >= 2 && (args[0] == "--install" || args[0] == "--restore")) {
                var log = new List<string>();
                try {
                    if (args[0] == "--install") Install(args[1], log.Add); else Restore(args[1], log.Add);
                    File.WriteAllText(SilentLogPath, string.Join(Environment.NewLine, log));
                    return 0;
                } catch (Exception e) {
                    log.Add("ERROR: " + e.Message);
                    try { File.WriteAllText(SilentLogPath, string.Join(Environment.NewLine, log)); } catch { }
                    return 1;
                }
            }
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
            return 0;
        }

        // ---------- game folder detection ----------

        internal static string DetectGameDir() {
            var candidates = new List<string>();
            foreach (var steam in SteamRoots()) {
                candidates.Add(steam);
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf)) {
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"")) {
                        candidates.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                    }
                }
            }
            foreach (var lib in candidates.Distinct(StringComparer.OrdinalIgnoreCase)) {
                string dir = Path.Combine(lib, "steamapps", "common", "Casualties Unknown");
                if (IsGameDir(dir)) return dir;
            }
            return null;
        }

        static IEnumerable<string> SteamRoots() {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 }) {
                foreach (var (hive, key, val) in new[] {
                    (RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                    (RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                    (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath") }) {
                    string p = null;
                    try {
                        using (var b = RegistryKey.OpenBaseKey(hive, view))
                        using (var k = b.OpenSubKey(key)) {
                            p = k?.GetValue(val) as string;
                        }
                    } catch { }
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) yield return p.Replace('/', '\\');
                }
            }
            yield return @"C:\Program Files (x86)\Steam";
        }

        internal static bool IsGameDir(string dir) =>
            !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, GameExe));

        // ---------- install / restore ----------

        internal static void Install(string gameDir, Action<string> log) {
            gameDir = gameDir.Trim().Trim('"');
            if (!IsGameDir(gameDir)) throw new Exception("В этой папке нет " + GameExe + ". Укажи папку игры.");
            EnsureGameClosed(gameDir);

            string bep = Path.Combine(gameDir, "BepInEx");
            string plugins = Path.Combine(bep, "plugins");
            string together = Path.Combine(plugins, "Together");
            string fixes = Path.Combine(plugins, "CTFixes");

            // 1. BepInEx itself, only if it is missing.
            if (!File.Exists(Path.Combine(bep, "core", "BepInEx.dll")) || !File.Exists(Path.Combine(gameDir, "winhttp.dll"))) {
                log("BepInEx не найден — устанавливаю BepInEx 5.4.23.5...");
                using (var s = Res("BepInEx.zip"))
                using (var zip = new ZipArchive(s, ZipArchiveMode.Read)) {
                    foreach (var e in zip.Entries) {
                        string dest = Path.GetFullPath(Path.Combine(gameDir, e.FullName));
                        if (!dest.StartsWith(Path.GetFullPath(gameDir), StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.IsNullOrEmpty(e.Name)) { Directory.CreateDirectory(dest); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        e.ExtractToFile(dest, true);
                    }
                }
            } else {
                log("BepInEx уже установлен.");
            }
            Directory.CreateDirectory(plugins);

            // 2. Backup of whatever mod files are there now.
            string backup = Path.Combine(bep, BackupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(backup);
            if (Directory.Exists(together)) {
                CopyDir(together, Path.Combine(backup, "Together"));
                log("Бэкап старого мода: " + backup);
            } else {
                File.WriteAllText(Path.Combine(backup, "no-Together.txt"), "plugins\\Together did not exist before install");
            }
            string cuc = Path.Combine(plugins, "CUCoreLib.dll");
            if (File.Exists(cuc)) File.Copy(cuc, Path.Combine(backup, "CUCoreLib.dll"), true);
            else File.WriteAllText(Path.Combine(backup, "no-CUCoreLib.txt"), "CUCoreLib.dll did not exist before install");
            if (Directory.Exists(fixes)) CopyDir(fixes, Path.Combine(backup, "CTFixes"));
            string ruLang = Path.Combine(gameDir, "CasualtiesUnknown_Data", "Lang", "RU-r-full.json");
            if (File.Exists(ruLang)) File.Copy(ruLang, Path.Combine(backup, "RU-r-full.json"), true);
            // very old mod versions lived in plugins\KrokMP; two copies of the mod would both load.
            string krok = Path.Combine(plugins, "KrokMP");
            if (Directory.Exists(krok)) {
                CopyDir(krok, Path.Combine(backup, "KrokMP"));
                Directory.Delete(krok, true);
                log("Старая папка KrokMP перенесена в бэкап.");
            }

            // 3. New files.
            Directory.CreateDirectory(together);
            foreach (string name in ResNames("Together/")) {
                WriteRes(name, Path.Combine(together, name.Substring("Together/".Length)));
            }
            WriteRes("CUCoreLib.dll", cuc);
            log("Файлы мода записаны в " + together);

            // CT Fixes: the fixes as a separate plugin on top of the official build.
            Directory.CreateDirectory(fixes);
            foreach (string name in ResNames("CTFixes/")) {
                WriteRes(name, Path.Combine(fixes, name.Substring("CTFixes/".Length)));
            }
            log("Плагин CT Fixes записан в " + fixes);

            // Russian locale: RU-r 1.0.2 with the character list fixed, missing 1.0.2 lines and the mod's strings.
            // It is a separate language entry, so the player picks it in the game's language menu.
            string lang = Path.Combine(gameDir, "CasualtiesUnknown_Data", "Lang");
            if (Directory.Exists(lang)) {
                WriteRes("RU-r-full.json", Path.Combine(lang, "RU-r-full.json"));
                log("Добавлен русский перевод «Русский (RU-r + мультиплеер)» — выбери его в настройках языка игры.");
            }

            // 4. The auto-updater would replace the test build with the official one on next launch.
            string upd = Path.Combine(together, "Multiupdater.dll");
            if (File.Exists(upd)) {
                File.Delete(upd);
                log("Автообновлятор мода отключён (иначе он заменит эту сборку официальной).");
            }
            log("ГОТОВО. Можно запускать игру через Steam.");
        }

        internal static void Restore(string gameDir, Action<string> log) {
            gameDir = gameDir.Trim().Trim('"');
            if (!IsGameDir(gameDir)) throw new Exception("В этой папке нет " + GameExe + ". Укажи папку игры.");
            EnsureGameClosed(gameDir);
            string bep = Path.Combine(gameDir, "BepInEx");
            string plugins = Path.Combine(bep, "plugins");
            string root = Path.Combine(bep, BackupRoot);
            var last = Directory.Exists(root) ? Directory.GetDirectories(root).OrderBy(d => d).LastOrDefault() : null;
            if (last == null) throw new Exception("Бэкап не найден — восстанавливать нечего.");

            string together = Path.Combine(plugins, "Together");
            if (Directory.Exists(together)) Directory.Delete(together, true);
            if (Directory.Exists(Path.Combine(last, "Together"))) CopyDir(Path.Combine(last, "Together"), together);
            if (Directory.Exists(Path.Combine(last, "KrokMP"))) CopyDir(Path.Combine(last, "KrokMP"), Path.Combine(plugins, "KrokMP"));
            string cuc = Path.Combine(plugins, "CUCoreLib.dll");
            if (File.Exists(Path.Combine(last, "CUCoreLib.dll"))) File.Copy(Path.Combine(last, "CUCoreLib.dll"), cuc, true);
            else if (File.Exists(cuc)) File.Delete(cuc);
            string fixes = Path.Combine(plugins, "CTFixes");
            if (Directory.Exists(fixes)) Directory.Delete(fixes, true);
            if (Directory.Exists(Path.Combine(last, "CTFixes"))) CopyDir(Path.Combine(last, "CTFixes"), fixes);
            string ruLang = Path.Combine(gameDir, "CasualtiesUnknown_Data", "Lang", "RU-r-full.json");
            if (File.Exists(Path.Combine(last, "RU-r-full.json"))) File.Copy(Path.Combine(last, "RU-r-full.json"), ruLang, true);
            else if (File.Exists(ruLang)) File.Delete(ruLang);
            Directory.Delete(last, true);
            log("Восстановлено из бэкапа " + Path.GetFileName(last) + ".");
            log("ГОТОВО. Мод вернулся к версии, которая стояла до установки.");
        }

        // only a game running from THIS folder blocks the install (its plugin files are locked)
        static void EnsureGameClosed(string gameDir) {
            string want = Path.GetFullPath(Path.Combine(gameDir, GameExe));
            foreach (var p in Process.GetProcessesByName("CasualtiesUnknown")) {
                string path = null;
                try { path = p.MainModule.FileName; } catch { }
                if (path == null || string.Equals(Path.GetFullPath(path), want, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Игра запущена. Закрой её и попробуй снова.");
            }
        }

        // ---------- helpers ----------

        static Stream Res(string name) =>
            Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new Exception("Нет ресурса " + name);

        static IEnumerable<string> ResNames(string prefix) =>
            Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n => n.StartsWith(prefix));

        static void WriteRes(string name, string path) {
            using (var s = Res(name))
            using (var f = File.Create(path)) s.CopyTo(f);
        }

        static void CopyDir(string src, string dst) {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        internal static bool IsAdmin() {
            using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        // Program Files needs admin. relaunch elevated only when writing actually needs it.
        internal static bool NeedsElevation(string gameDir) {
            if (IsAdmin()) return false;
            try {
                string probe = Path.Combine(gameDir, ".ct_write_test");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return false;
            } catch (UnauthorizedAccessException) {
                return true;
            }
        }
    }

    // Windows 95 style: classic controls (no visual styles), silver background, one path box and one button.
    class MainForm : Form {
        static readonly Color Silver = Color.FromArgb(192, 192, 192);
        readonly TextBox pathBox = new TextBox();
        readonly Label status = new Label();
        readonly Button installBtn = new Button();

        public MainForm(string initialPath) {
            Text = "Casualties: Together 5.5.4 Setup";
            Font = new Font("Microsoft Sans Serif", 8.25f);
            BackColor = Silver;
            ClientSize = new Size(440, 150);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var title = new Label {
                Left = 0, Top = 0, Width = 440, Height = 24, BackColor = Color.FromArgb(0, 0, 128), ForeColor = Color.White,
                Font = new Font("Microsoft Sans Serif", 8.25f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
                Text = "  Casualties: Together 5.5.4 + fixes"
            };
            var pathLabel = new Label { Left = 10, Top = 36, Width = 420, Height = 16, Text = "Папка игры (где лежит CasualtiesUnknown.exe):" };
            pathBox.SetBounds(10, 54, 336, 21);
            pathBox.BorderStyle = BorderStyle.Fixed3D;
            var browse = new Button { Text = "Обзор...", Left = 354, Top = 53, Width = 76, Height = 23, BackColor = Silver };
            browse.Click += (s, e) => {
                using (var d = new FolderBrowserDialog { Description = "Выбери папку игры Casualties Unknown" }) {
                    if (Directory.Exists(pathBox.Text)) d.SelectedPath = pathBox.Text;
                    if (d.ShowDialog(this) == DialogResult.OK) pathBox.Text = d.SelectedPath;
                }
            };
            installBtn.Text = "Установить";
            installBtn.BackColor = Silver;
            installBtn.SetBounds(258, 90, 90, 23);
            installBtn.Click += (s, e) => Run("--install");
            var restoreBtn = new Button { Text = "Восстановить", Left = 10, Top = 90, Width = 96, Height = 23, BackColor = Silver };
            restoreBtn.Click += (s, e) => Run("--restore");
            var exit = new Button { Text = "Выход", Left = 354, Top = 90, Width = 76, Height = 23, BackColor = Silver };
            exit.Click += (s, e) => Close();
            status.SetBounds(2, 126, 436, 22);
            status.BorderStyle = BorderStyle.Fixed3D;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.AutoEllipsis = true;

            Controls.AddRange(new Control[] { title, pathLabel, pathBox, browse, restoreBtn, installBtn, exit, status });
            AcceptButton = installBtn;

            pathBox.Text = initialPath ?? Program.DetectGameDir() ?? "";
            Log(Program.IsGameDir(pathBox.Text) ? "Игра найдена. Нажми «Установить»." : "Укажи папку игры через «Обзор...».");
        }

        void Log(string s) { status.Text = " " + s; status.Refresh(); }

        void Run(string mode) {
            string dir = pathBox.Text.Trim().Trim('"');
            if (!Program.IsGameDir(dir)) {
                MessageBox.Show(this, "В этой папке нет CasualtiesUnknown.exe.\r\nВ Steam: ПКМ по игре → Управление → Просмотреть локальные файлы.", "Не та папка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try {
                if (Program.NeedsElevation(dir)) {
                    Log("Нужны права администратора...");
                    var psi = new ProcessStartInfo(Application.ExecutablePath, mode + " \"" + dir + "\"") {
                        Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
                    };
                    var p = Process.Start(psi);
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                        throw new Exception(File.Exists(Program.SilentLogPath) ? File.ReadAllText(Program.SilentLogPath) : "код " + p.ExitCode);
                } else if (mode == "--install") {
                    Program.Install(dir, Log);
                } else {
                    Program.Restore(dir, Log);
                }
                if (mode == "--install") {
                    Log("Готово. Запускай игру через Steam.");
                    MessageBox.Show(this, "Мод 5.5.4 + CT Fixes установлен.\r\nПодключаться можно и к игрокам с обычной 5.5.4. Фикс протезов работает, если плагин стоит у хоста.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                } else {
                    Log("Готово. Вернулась версия, которая стояла до установки.");
                    MessageBox.Show(this, "Восстановлено из бэкапа: вернулась версия, которая стояла до установки.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            } catch (System.ComponentModel.Win32Exception) {
                Log("Отменено: нужны права администратора.");
            } catch (Exception ex) {
                Log("Ошибка.");
                MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
