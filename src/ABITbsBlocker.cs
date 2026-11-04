// ABI-TBS-Blocker：用 Windows 防火牆的輸出規則，禁止《暗區突圍：無限》啟動器內建的瀏覽器 tbs_browser.exe 連網。
// 只會新增或刪除名為 Firewall.RuleName 的規則；不修改、不刪除任何檔案，本身也不連網。
// 編譯方式見 build.bat（Windows 內建 .NET Framework 4 的 csc.exe，語法限 C# 5）。
//
// 命令列（給進階使用者與測試用，結果寫到 /report 指定的檔案）：
//   ABI-TBS-Blocker.exe /status  /report r.txt
//   ABI-TBS-Blocker.exe /block   /report r.txt   [/path 其他位置\tbs_browser.exe]
//   ABI-TBS-Blocker.exe /unblock /report r.txt
//   結束代碼：0 成功、1 失敗、2 找不到 tbs_browser.exe、3 沒有系統管理員權限

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("ABI TBS Blocker")]
[assembly: AssemblyDescription("Blocks the Arena Breakout Infinite launcher's tbs_browser.exe with a Windows Firewall outbound rule")]
[assembly: AssemblyProduct("ABI TBS Blocker")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace AbiTbsBlocker
{
    sealed class RuleInfo
    {
        public string Name;
        public string Program;
        public bool OutboundBlockEnabled;
    }

    static class Firewall
    {
        public const string RuleName = "Block tbs_browser.exe (Arena Breakout Infinite)";
        const int DirectionOut = 2;          // NET_FW_RULE_DIR_OUT
        const int ActionBlock = 0;           // NET_FW_ACTION_BLOCK
        const int AllProfiles = 0x7FFFFFFF;  // NET_FW_PROFILE2_ALL

        static dynamic Policy()
        {
            return Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true));
        }

        static bool IsOurs(string name)
        {
            return name != null && name.StartsWith(RuleName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                a = Path.GetFullPath(Environment.ExpandEnvironmentVariables(a));
                b = Path.GetFullPath(Environment.ExpandEnvironmentVariables(b));
            }
            catch (Exception) { }
            return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        public static List<RuleInfo> ReadOurRules()
        {
            var rules = new List<RuleInfo>();
            foreach (dynamic r in Policy().Rules)
            {
                string name = r.Name;
                if (!IsOurs(name)) continue;
                var info = new RuleInfo();
                info.Name = name;
                info.Program = r.ApplicationName;
                info.OutboundBlockEnabled = (int)r.Direction == DirectionOut && (int)r.Action == ActionBlock && (bool)r.Enabled;
                rules.Add(info);
            }
            return rules;
        }

        public static bool IsBlocked(string program, List<RuleInfo> rules)
        {
            return rules.Any(x => x.OutboundBlockEnabled && SamePath(x.Program, program));
        }

        // 回傳 true 表示這次新增了規則；已經封鎖時不重複新增。寫入後一定讀回確認。
        public static bool Block(string program)
        {
            if (IsBlocked(program, ReadOurRules())) return false;
            Unblock(program);  // 先清掉同一路徑、但被停用或改過的舊規則
            dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", true));
            rule.Name = RuleName;
            rule.Description = "由 ABI-TBS-Blocker 建立：禁止暗區啟動器內建瀏覽器 tbs_browser.exe 連網。要還原請用工具的「解除封鎖」，或直接刪除這條規則。";
            rule.Grouping = "ABI-TBS-Blocker";
            rule.ApplicationName = program;
            rule.Direction = DirectionOut;
            rule.Action = ActionBlock;
            rule.Profiles = AllProfiles;
            rule.InterfaceTypes = "All";
            rule.Enabled = true;
            Policy().Rules.Add(rule);
            if (!IsBlocked(program, ReadOurRules()))
                throw new InvalidOperationException("規則寫入後讀不回來，封鎖沒有成功。");
            return true;
        }

        // 回傳刪除的規則數。刪除後一定讀回確認。
        public static int Unblock(string program)
        {
            dynamic policy = Policy();
            var targets = new List<dynamic>();
            foreach (dynamic r in policy.Rules)
            {
                string name = r.Name;
                string app = r.ApplicationName;
                if (IsOurs(name) && SamePath(app, program)) targets.Add(r);
            }
            foreach (dynamic t in targets)
            {
                // 不同路徑的規則可能同名，先改成唯一名稱再刪，才不會刪到別條
                string temp = RuleName + " (removing " + Guid.NewGuid().ToString("N") + ")";
                t.Name = temp;
                policy.Rules.Remove(temp);
            }
            if (ReadOurRules().Any(x => SamePath(x.Program, program)))
                throw new InvalidOperationException("規則刪除後仍然存在，解除封鎖沒有成功。");
            return targets.Count;
        }

        public static List<string> Warnings()
        {
            var warnings = new List<string>();
            try
            {
                Type type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true);
                object policy = Activator.CreateInstance(type);
                int current = (int)type.InvokeMember("CurrentProfileTypes", BindingFlags.GetProperty, null, policy, null);
                var off = new List<string>();
                foreach (int profile in new[] { 1, 2, 4 })
                {
                    if ((current & profile) == 0) continue;
                    if (!(bool)type.InvokeMember("FirewallEnabled", BindingFlags.GetProperty, null, policy, new object[] { profile }))
                        off.Add(profile == 1 ? "網域" : profile == 2 ? "私人" : "公用");
                }
                if (off.Count > 0)
                    warnings.Add("Windows 防火牆在目前的網路（" + string.Join("、", off) + "）是關閉的，封鎖規則不會生效。請到「Windows 安全性 → 防火牆與網路保護」開啟。");
                if ((int)type.InvokeMember("LocalPolicyModifyState", BindingFlags.GetProperty, null, policy, null) == 1)
                    warnings.Add("這台電腦的防火牆由群組原則管理，本機新增的規則可能不會生效。");
            }
            catch (Exception ex)
            {
                warnings.Add("讀不到 Windows 防火牆的狀態：" + ex.Message);
            }
            try
            {
                var names = new List<string>();
                using (var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName FROM FirewallProduct"))
                    foreach (ManagementBaseObject o in searcher.Get())
                        names.Add(Convert.ToString(o["displayName"]));
                if (names.Count > 0)
                    warnings.Add("偵測到其他防火牆軟體（" + string.Join("、", names) + "），Windows 防火牆的規則可能不會生效。建議也在該軟體裡封鎖 tbs_browser.exe。");
            }
            catch (Exception) { }  // 伺服器版 Windows 沒有 SecurityCenter2，略過
            return warnings;
        }
    }

    static class Detector
    {
        const string ExeName = "tbs_browser.exe";
        static readonly string[] KnownSubPaths = { @"launcher\service\tbs_browser.exe", @"service\tbs_browser.exe" };
        static readonly string[] FolderHints = { "Arena Breakout", "ArenaBreakout", "暗區" };
        static readonly string[] CommonParents =
        {
            "", "Program Files", "Program Files (x86)", "Games",
            @"SteamLibrary\steamapps\common", @"Steam\steamapps\common",
            @"Program Files (x86)\Steam\steamapps\common", @"Program Files\Steam\steamapps\common"
        };

        public static List<string> Find(IEnumerable<string> manual, List<RuleInfo> rules)
        {
            var roots = new List<string>();
            var found = new List<string>();

            // 1. 登錄檔的「解除安裝」清單（暗區啟動器會寫入 GameInstallPath）
            foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (string sub in new[] { @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                    using (RegistryKey list = Open(hive, sub))
                    {
                        if (list == null) continue;
                        foreach (string id in list.GetSubKeyNames())
                            using (RegistryKey entry = Open(list, id))
                            {
                                if (entry == null || !HasHint(entry.GetValue("DisplayName") as string)) continue;
                                AddDir(roots, entry.GetValue("GameInstallPath") as string);
                                AddDir(roots, entry.GetValue("InstallLocation") as string);
                                AddDir(roots, DirectoryOf(entry.GetValue("DisplayIcon") as string));
                                AddDir(roots, DirectoryOf(entry.GetValue("UninstallString") as string));
                            }
                    }

            // 2. 各磁碟的常見位置，以及前兩層名稱像暗區的資料夾
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                bool ready;
                try { ready = drive.DriveType == DriveType.Fixed && drive.IsReady; }
                catch (Exception) { ready = false; }
                if (!ready) continue;
                string root = drive.RootDirectory.FullName;
                foreach (string parent in CommonParents)
                    foreach (string dir in Subdirectories(Path.Combine(root, parent)))
                        if (HasHint(Path.GetFileName(dir))) AddDir(roots, dir);
                foreach (string top in Subdirectories(root))
                    foreach (string dir in Subdirectories(top))
                        if (HasHint(Path.GetFileName(dir))) AddDir(roots, dir);
            }

            // 3. 在每個候選資料夾找 tbs_browser.exe：先看已知位置，再往下最多 3 層
            foreach (string dir in roots)
            {
                foreach (string sub in KnownSubPaths) AddFile(found, Path.Combine(dir, sub), true);
                foreach (string file in Search(dir, 3)) AddFile(found, file, true);
            }

            // 4. 正在執行、而且位在暗區資料夾裡的 tbs_browser.exe
            foreach (Process p in Process.GetProcessesByName("tbs_browser"))
            {
                try { string file = p.MainModule.FileName; if (HasHint(file)) AddFile(found, file, true); }
                catch (Exception) { }
                finally { p.Dispose(); }
            }

            // 5. 手動選的檔案，以及已經存在的封鎖規則（檔案不在了也列出來，才能解除）
            foreach (string file in manual) AddFile(found, file, true);
            foreach (RuleInfo rule in rules) AddFile(found, rule.Program, false);
            return found;
        }

        public static bool TbsRunning()
        {
            Process[] processes = Process.GetProcessesByName("tbs_browser");
            foreach (Process p in processes) p.Dispose();
            return processes.Length > 0;
        }

        static bool HasHint(string s)
        {
            return s != null && FolderHints.Any(h => s.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static void AddDir(List<string> dirs, string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            if (!dirs.Any(x => Firewall.SamePath(x, dir))) dirs.Add(dir);
        }

        static void AddFile(List<string> files, string file, bool mustExist)
        {
            try
            {
                if (string.IsNullOrEmpty(file)) return;
                if (!string.Equals(Path.GetFileName(file), ExeName, StringComparison.OrdinalIgnoreCase)) return;
                if (mustExist && !File.Exists(file)) return;
                if (!files.Any(x => Firewall.SamePath(x, file))) files.Add(file);
            }
            catch (Exception) { }  // 路徑格式不對就略過
        }

        static string DirectoryOf(string command)
        {
            if (string.IsNullOrEmpty(command)) return null;
            string s = command.Trim();
            if (s.StartsWith("\""))
            {
                int end = s.IndexOf('"', 1);
                if (end > 1) s = s.Substring(1, end - 1);
            }
            else
            {
                int exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exe > 0) s = s.Substring(0, exe + 4);
            }
            int comma = s.LastIndexOf(',');  // DisplayIcon 可能是「路徑,0」
            if (comma > 2 && !File.Exists(s)) s = s.Substring(0, comma);
            try { return Path.GetDirectoryName(s); }
            catch (Exception) { return null; }
        }

        static RegistryKey Open(RegistryKey parent, string name)
        {
            try { return parent.OpenSubKey(name); }
            catch (Exception) { return null; }
        }

        static string[] Subdirectories(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : new string[0]; }
            catch (Exception) { return new string[0]; }
        }

        static List<string> Search(string root, int maxDepth)
        {
            var result = new List<string>();
            var queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(root, 0));
            while (queue.Count > 0)
            {
                KeyValuePair<string, int> current = queue.Dequeue();
                string file = Path.Combine(current.Key, ExeName);
                if (File.Exists(file)) result.Add(file);
                if (current.Value >= maxDepth) continue;
                foreach (string dir in Subdirectories(current.Key))
                {
                    try { if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue; }
                    catch (Exception) { continue; }
                    queue.Enqueue(new KeyValuePair<string, int>(dir, current.Value + 1));
                }
            }
            return result;
        }
    }

    sealed class MainForm : Form
    {
        readonly ListView list = new ListView();
        readonly Label status = new Label();
        readonly Button blockButton = new Button();
        readonly Button unblockButton = new Button();
        readonly Button pickButton = new Button();
        readonly Button refreshButton = new Button();
        readonly Button closeButton = new Button();
        readonly List<string> manual;
        List<string> paths = new List<string>();

        public MainForm(List<string> manualPaths)
        {
            manual = manualPaths;
            Text = Program.Title;
            Font = new Font("Microsoft JhengHei UI", 10F);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 500);
            MinimumSize = new Size(640, 440);

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.Padding = new Padding(14);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(720, 0);
            intro.Margin = new Padding(0, 0, 0, 10);
            intro.Text =
                "這個工具會新增一條 Windows 防火牆規則，禁止《暗區突圍：無限》啟動器內建的瀏覽器 tbs_browser.exe 連上網路。\r\n\r\n" +
                "‧ 只會新增或刪除名為「" + Firewall.RuleName + "」的規則，不會修改或刪除任何遊戲檔案。\r\n" +
                "‧ 副作用：啟動器裡的公告、活動等網頁內容可能變成空白。如果啟動器因此無法登入、更新或開始遊戲，按「解除封鎖」就能完全還原。\r\n" +
                "‧ 封鎖或解除之後，請完全關閉暗區啟動器（右下角圖示按右鍵 → 結束）再重新開啟。";

            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.Dock = DockStyle.Fill;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Columns.Add("狀態", 110);
            list.Columns.Add("tbs_browser.exe 的位置", 600);

            var bar = new FlowLayoutPanel();
            bar.AutoSize = true;
            bar.Dock = DockStyle.Fill;
            bar.WrapContents = true;
            bar.Margin = new Padding(0, 10, 0, 0);
            SetupButton(blockButton, "封鎖", OnBlock);
            SetupButton(unblockButton, "解除封鎖", OnUnblock);
            SetupButton(pickButton, "手動選擇檔案…", OnPick);
            SetupButton(refreshButton, "重新偵測", delegate { Reload(); });
            SetupButton(closeButton, "關閉", delegate { Close(); });
            blockButton.Font = new Font(Font, FontStyle.Bold);
            bar.Controls.AddRange(new Control[] { blockButton, unblockButton, pickButton, refreshButton, closeButton });

            status.AutoSize = true;
            status.MaximumSize = new Size(720, 0);
            status.Margin = new Padding(0, 10, 0, 0);

            layout.Controls.Add(intro, 0, 0);
            layout.Controls.Add(list, 0, 1);
            layout.Controls.Add(bar, 0, 2);
            layout.Controls.Add(status, 0, 3);
            Controls.Add(layout);
            CancelButton = closeButton;
            Shown += delegate { Reload(); };
        }

        static void SetupButton(Button button, string text, EventHandler onClick)
        {
            button.Text = text;
            button.AutoSize = true;
            button.Padding = new Padding(10, 4, 10, 4);
            button.Margin = new Padding(0, 0, 8, 0);
            button.Click += onClick;
        }

        void Reload()
        {
            UseWaitCursor = true;
            Application.DoEvents();
            try
            {
                List<RuleInfo> rules = Firewall.ReadOurRules();
                paths = Detector.Find(manual, rules);
                list.BeginUpdate();
                list.Items.Clear();
                foreach (string p in paths)
                {
                    bool exists = File.Exists(p);
                    bool blocked = Firewall.IsBlocked(p, rules);
                    var item = new ListViewItem(blocked ? "已封鎖" : exists ? "未封鎖" : "檔案不存在");
                    item.SubItems.Add(p);
                    item.ForeColor = blocked ? Color.ForestGreen : exists ? Color.Firebrick : Color.Gray;
                    list.Items.Add(item);
                }
                list.EndUpdate();
                list.Columns[1].Width = -2;

                var warnings = new List<string>();
                if (paths.Count == 0)
                    warnings.Add("找不到暗區的 tbs_browser.exe。有安裝暗區的話，請按「手動選擇檔案…」，選擇遊戲資料夾裡的 launcher\\service\\tbs_browser.exe。");
                warnings.AddRange(Firewall.Warnings());
                var notes = new List<string>();
                if (Detector.TbsRunning())
                    notes.Add("tbs_browser.exe 正在執行中。封鎖或解除之後，請完全關閉暗區啟動器再重新開啟，才會生效。");

                if (warnings.Count > 0)
                {
                    status.ForeColor = Color.Firebrick;
                    status.Text = "注意：" + string.Join("\r\n注意：", warnings) + (notes.Count > 0 ? "\r\n" + string.Join("\r\n", notes) : "");
                }
                else
                {
                    status.ForeColor = notes.Count > 0 ? Color.DarkGoldenrod : Color.ForestGreen;
                    status.Text = notes.Count > 0 ? string.Join("\r\n", notes) : "Windows 防火牆運作正常。";
                }
                blockButton.Enabled = paths.Any(p => File.Exists(p) && !Firewall.IsBlocked(p, rules));
                unblockButton.Enabled = rules.Count > 0;
            }
            catch (Exception ex)
            {
                status.ForeColor = Color.Firebrick;
                status.Text = "讀取防火牆狀態失敗：" + ex.Message;
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        void OnBlock(object sender, EventArgs e)
        {
            int added = 0;
            var errors = new List<string>();
            foreach (string p in paths.Where(x => File.Exists(x)).ToList())
            {
                try { if (Firewall.Block(p)) added++; }
                catch (Exception ex) { errors.Add(p + "\r\n  " + ex.Message); }
            }
            Reload();
            if (errors.Count > 0)
                MessageBox.Show(this, "有 " + errors.Count + " 個沒有封鎖成功：\r\n\r\n" + string.Join("\r\n", errors), Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            else if (added > 0)
                MessageBox.Show(this, "封鎖完成（" + added + " 個）。\r\n\r\n請完全關閉暗區啟動器再重新開啟，確認能正常登入、更新、開始遊戲。\r\n如果不行，回到這個工具按「解除封鎖」就能還原。", Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, "已經是封鎖狀態，不需要再做任何事。", Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void OnUnblock(object sender, EventArgs e)
        {
            int removed = 0;
            var errors = new List<string>();
            foreach (string p in paths.ToList())
            {
                try { removed += Firewall.Unblock(p); }
                catch (Exception ex) { errors.Add(p + "\r\n  " + ex.Message); }
            }
            Reload();
            if (errors.Count > 0)
                MessageBox.Show(this, "有 " + errors.Count + " 個沒有解除成功：\r\n\r\n" + string.Join("\r\n", errors), Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            else if (removed > 0)
                MessageBox.Show(this, "已解除封鎖（刪除 " + removed + " 條規則），恢復成原本的狀態。\r\n\r\n請完全關閉暗區啟動器再重新開啟。", Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, "目前沒有封鎖，不需要解除。", Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void OnPick(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "選擇暗區資料夾裡的 tbs_browser.exe（通常在 遊戲資料夾\\launcher\\service\\）";
                dialog.Filter = "tbs_browser.exe|tbs_browser.exe";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!string.Equals(Path.GetFileName(dialog.FileName), "tbs_browser.exe", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, "請選擇名稱是 tbs_browser.exe 的檔案。", Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                manual.Add(dialog.FileName);
            }
            Reload();
        }
    }

    static class Program
    {
        public const string Title = "暗區突圍：無限 — 封鎖 tbs_browser.exe";
        public const string Version = "1.0.0";

        [STAThread]
        static int Main(string[] args)
        {
            bool block = false, unblock = false, cli = false;
            string report = null;
            var manual = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].TrimStart('/', '-').ToLowerInvariant();
                if (a == "block") { block = true; cli = true; }
                else if (a == "unblock") { unblock = true; cli = true; }
                else if (a == "status") cli = true;
                else if (a == "report" && i + 1 < args.Length) { report = args[++i]; cli = true; }
                else if (a == "path" && i + 1 < args.Length) manual.Add(args[++i]);
            }

            if (!IsAdministrator())
            {
                if (cli)
                {
                    WriteReport(report, "error=需要系統管理員權限\r\nexit=3\r\n");
                    return 3;
                }
                MessageBox.Show("需要系統管理員權限才能修改防火牆。\r\n請對這個程式按右鍵 →「以系統管理員身分執行」。", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 3;
            }
            if (cli) return RunCommandLine(block, unblock, report, manual);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(manual));
            return 0;
        }

        static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        static int RunCommandLine(bool block, bool unblock, string report, List<string> manual)
        {
            var sb = new StringBuilder();
            int code = 0;
            try
            {
                if (block && unblock) throw new ArgumentException("/block 和 /unblock 不能同時使用");
                List<RuleInfo> before = Firewall.ReadOurRules();
                List<string> paths = Detector.Find(manual, before);
                sb.AppendLine("ABI-TBS-Blocker " + Version);
                sb.AppendLine("action=" + (block ? "block" : unblock ? "unblock" : "status"));
                sb.AppendLine("found=" + paths.Count);
                foreach (string p in paths)
                {
                    bool exists = File.Exists(p);
                    bool wasBlocked = Firewall.IsBlocked(p, before);
                    string change = "none";
                    if (block && exists) change = Firewall.Block(p) ? "added" : "unchanged";
                    if (unblock) { int n = Firewall.Unblock(p); change = n > 0 ? "removed " + n : "unchanged"; }
                    bool nowBlocked = Firewall.IsBlocked(p, Firewall.ReadOurRules());
                    sb.AppendLine(string.Format("path={0} | exists={1} | blocked_before={2} | blocked_after={3} | change={4}", p, exists, wasBlocked, nowBlocked, change));
                    if ((block && exists && !nowBlocked) || (unblock && nowBlocked)) code = 1;
                }
                foreach (string w in Firewall.Warnings()) sb.AppendLine("warning=" + w);
                sb.AppendLine("tbs_running=" + Detector.TbsRunning());
                if (paths.Count == 0 && code == 0) code = 2;
            }
            catch (Exception ex)
            {
                sb.AppendLine("error=" + ex);
                code = 1;
            }
            sb.AppendLine("exit=" + code);
            WriteReport(report, sb.ToString());
            return code;
        }

        static void WriteReport(string path, string text)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { File.WriteAllText(path, text, new UTF8Encoding(true)); }
            catch (Exception) { }
        }
    }
}
