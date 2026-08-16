using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MhCleaner
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest")
            {
                SelfTest.Run();
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    // 与界面共用的清理逻辑：递归删除 res3d_* 和 V3d_cache* 文件
    static class Cleaner
    {
        public static long FilesDeleted, BytesFreed, Failures;

        public static void Reset() { FilesDeleted = 0; BytesFreed = 0; Failures = 0; }

        public static void Clean(string dir)
        {
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("目录不存在：" + dir);
            CleanPattern(dir, "res3d_*");
            CleanPattern(dir, "V3d_cache*");
        }

        static void CleanPattern(string dir, string pattern)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(dir, pattern, SearchOption.AllDirectories);
            }
            catch (UnauthorizedAccessException)
            {
                throw new Exception("没有权限访问部分子目录：" + dir);
            }
            foreach (string f in files)
            {
                try
                {
                    FileInfo fi = new FileInfo(f);
                    BytesFreed += fi.Length;
                    // 缓存文件常带只读属性，直接删除会失败，先去掉
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                    FilesDeleted++;
                }
                catch { Failures++; }
            }
        }
    }

    static class SelfTest
    {
        // --selftest：临时目录建假文件，验证清理逻辑后报告
        public static void Run()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mh_selftest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string a = Path.Combine(dir, "res3d_test.xyz"), b = Path.Combine(dir, "V3d_cache_test");
            string keep = Path.Combine(dir, "keep.txt");
            File.WriteAllText(a, "x"); File.WriteAllText(b, "y"); File.WriteAllText(keep, "z");
            string ok = "FAIL";
            try
            {
                Cleaner.Reset();
                Cleaner.Clean(dir);
                bool deleted = !File.Exists(a) && !File.Exists(b) && File.Exists(keep);
                if (deleted && Cleaner.FilesDeleted == 2) ok = "PASS";

                // 中文路径 + 只读属性文件：验证删除修复
                string cnDir = Path.Combine(Path.GetTempPath(), "梦幻自检目录_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(cnDir);
                string ro = Path.Combine(cnDir, "res3d_ro");
                File.WriteAllText(ro, "x");
                File.SetAttributes(ro, FileAttributes.ReadOnly);
                string keep2 = Path.Combine(cnDir, "keep2.txt");
                File.WriteAllText(keep2, "z");
                Cleaner.Reset();
                Cleaner.Clean(cnDir);
                bool roOk = !File.Exists(ro) && File.Exists(keep2) && Cleaner.FilesDeleted == 1 && Cleaner.Failures == 0;
                Directory.Delete(cnDir, true);
                if (!roOk) ok = "FAIL";

                // 版本号格式与比较逻辑：年月日.自增号
                if (!VersionUtil.IsValid("20260815.1")) ok = "FAIL";
                if (!VersionUtil.IsNewer("20260815.3", "20260815.2")) ok = "FAIL";
                if (VersionUtil.IsNewer("20260815.2", "20260815.3")) ok = "FAIL";
                if (VersionUtil.IsNewer("20250815.9", "20260815.1")) ok = "FAIL";
            }
            finally { Directory.Delete(dir, true); }
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "mh_selftest_result.txt"), ok);
            MessageBox.Show("自检结果：" + ok, "梦幻清理缓存工具");
        }
    }

    // 版本号格式：年月日.自增号，如 20260815.3
    static class VersionUtil
    {
        public static bool Parse(string s, out int[] v)
        {
            v = new int[0];
            if (string.IsNullOrEmpty(s)) return false;
            string[] p = s.Split('.');
            if (p.Length != 2) return false;
            int a, b;
            if (!int.TryParse(p[0], out a) || !int.TryParse(p[1], out b)) return false;
            v = new[] { a, b };
            return true;
        }

        public static bool IsValid(string s) { int[] v; return Parse(s, out v); }

        public static bool IsNewer(string remote, string current)
        {
            int[] r, c;
            if (!Parse(remote, out r) || !Parse(current, out c)) return false;
            return r[0] > c[0] || (r[0] == c[0] && r[1] > c[1]);
        }
    }

    // 更新检查：国内加速镜像优先（ghproxy 系），直连 GitHub 兜底
    static class Updater
    {
        public const string Repo = "qq458249269/MHXYTempCleaner";
        public static readonly string[] Mirrors =
        {
            "https://mirror.ghproxy.com/",
            "https://ghproxy.net/",
            "https://ghfast.top/",
            "https://gh-proxy.com/",
        };

        // 返回 {版本标签, 下载地址, 加速前缀}；加速前缀为空表示用的是直连
        public static string[] GetLatestReleaseInfo()
        {
            List<string> prefixes = new List<string>(Mirrors);
            prefixes.Add("");
            string url = "https://api.github.com/repos/" + Repo + "/releases/latest";
            Exception last = null;
            foreach (string p in prefixes)
            {
                try
                {
                    string json = Fetch(p + url);
                    string tag = Extract(json, "tag_name");
                    string dl = Extract(json, "browser_download_url");
                    if (tag.Length == 0 || dl.Length == 0) throw new Exception("更新服务器响应格式异常");
                    return new[] { tag, dl, p };
                }
                catch (Exception ex) { last = ex; }
            }
            throw last ?? new Exception("无法连接更新服务器");
        }

        static string Fetch(string fullUrl)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(fullUrl);
            req.Timeout = 8000;
            req.ReadWriteTimeout = 8000;
            req.UserAgent = "MHXYTempCleaner/" + Application.ProductVersion;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            {
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
        }

        // 从 JSON 里取第一个 "key" 的值（更新响应简单，不需要完整 JSON 库）
        static string Extract(string json, string key)
        {
            int i = json.IndexOf(key);
            if (i < 0) return "";
            int q1 = json.IndexOf('"', i + key.Length);
            int q2 = q1 < 0 ? -1 : json.IndexOf('"', q1 + 1);
            if (q2 < 0) return "";
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }
    }

    class MainForm : Form
    {
        TextBox txtDir, txtLog;
        CheckBox chkAuto, chkAutoStart, chkUpdate;
        Button btnClean;
        string cfgPath;

        // 配置放 %APPDATA%：exe 位于 Program Files (x86) 等受限目录时，exe 旁无法写文件导致目录记不住
        static string GetCfgPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "梦幻清理缓存工具");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "梦幻清理缓存.ini");
        }

        public MainForm()
        {
            Text = "梦幻西游缓存清理工具 v" + Application.ProductVersion;
            ClientSize = new Size(660, 400);
            MinimumSize = new Size(500, 300);
            cfgPath = GetCfgPath();

            chkAuto = new CheckBox { Text = "启动时自动清理", AutoSize = true, Checked = true, Anchor = AnchorStyles.Left };
            chkAutoStart = new CheckBox { Text = "开机自启动", AutoSize = true, Anchor = AnchorStyles.Left };
            chkUpdate = new CheckBox { Text = "自动检查更新", AutoSize = true, Checked = true, Anchor = AnchorStyles.Left };
            txtDir = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            Button btnBrowse = new Button { Text = "浏览...", Anchor = AnchorStyles.Right };
            btnClean = new Button { Text = "一键清理", Anchor = AnchorStyles.Right };
            txtLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };

            btnBrowse.Click += OnBrowse;
            btnClean.Click += OnClean;

            TableLayoutPanel top = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = 36, ColumnCount = 6, Padding = new Padding(2, 4, 2, 0)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.Controls.Add(chkAuto); top.Controls.Add(chkAutoStart); top.Controls.Add(chkUpdate); top.Controls.Add(txtDir); top.Controls.Add(btnBrowse); top.Controls.Add(btnClean);

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(top);
            root.Controls.Add(txtLog);
            Controls.Add(root);

            chkAuto.CheckedChanged += delegate { SaveConfig(); };
            chkAutoStart.CheckedChanged += delegate { SaveAutoStart(); };
            chkUpdate.CheckedChanged += delegate { SaveConfig(); };
            LoadConfig();
            LoadAutoStart();
            Shown += delegate
            {
                if (chkAuto.Checked)
                {
                    AppendLog("[自动执行] 启动自动清理...");
                    RunClean();
                }
                if (chkUpdate.Checked) CheckUpdate();
            };
            FormClosing += delegate { SaveConfig(); };
        }

        void OnBrowse(object s, EventArgs e)
        {
            try
            {
                FolderBrowserDialog dlg = new FolderBrowserDialog { Description = "选择游戏目录（如 D:\\games\\MH）", SelectedPath = txtDir.Text };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    txtDir.Text = dlg.SelectedPath;
                    SaveConfig();
                }
            }
            catch (Exception ex) { AppendLog("[错误] 选择目录失败：" + ex.Message); }
        }

        void OnClean(object s, EventArgs e)
        {
            SaveConfig();
            RunClean();
        }

        void RunClean()
        {
            string dir = txtDir.Text.Trim();
            if (dir.Length == 0) { AppendLog("[错误] 请先设置游戏目录。"); return; }
            try
            {
                Cleaner.Reset();
                AppendLog("开始清理：" + dir);
                DateTime start = DateTime.Now;
                Cleaner.Clean(dir);
                double sec = (DateTime.Now - start).TotalSeconds;
                AppendLog("清理完成：删除文件 " + Cleaner.FilesDeleted + " 个，" +
                          "释放 " + (Cleaner.BytesFreed / 1048576.0).ToString("0.0") + " MB，用时 " + sec.ToString("0.0") + " 秒" +
                          (Cleaner.Failures > 0 ? "（" + Cleaner.Failures + " 个文件删除失败，可能被占用）" : ""));
            }
            catch (Exception ex)
            {
                AppendLog("[错误] " + ex.Message);
            }
        }

        // 后台检查更新：国内加速镜像优先，直连 GitHub 兜底；有新版本时询问是否跳转下载
        void CheckUpdate()
        {
            new Thread(delegate()
            {
                try
                {
                    string[] info = Updater.GetLatestReleaseInfo();
                    // 有可用镜像时用同一镜像加速下载链接，直连则为原始地址
                    string url = info[2] + info[1];
                    string cur = Application.ProductVersion;
                    if (!VersionUtil.IsNewer(info[0], cur)) return;
                    BeginInvoke(new Action(delegate
                    {
                        string msg = "发现新版本 v" + info[0] + "（当前 v" + cur + "），是否前往下载？";
                        if (MessageBox.Show(this, msg, "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        {
                            try { Process.Start(url); }
                            catch (Exception ex) { AppendLog("[错误] 打开下载地址失败：" + ex.Message); }
                        }
                    }));
                }
                catch { } // 离线或镜像不可用时静默，不影响正常使用
            }) { IsBackground = true }.Start();
        }

        void AppendLog(string line)
        {
            if (InvokeRequired) { BeginInvoke(new Action(delegate { AppendLog(line); })); return; }
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss ") + line + Environment.NewLine);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        void LoadConfig()
        {
            // 迁移：旧的 exe 旁 ini 搬进 %APPDATA%
            string old = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "梦幻清理缓存.ini");
            if (!File.Exists(cfgPath) && File.Exists(old))
            {
                try { File.Copy(old, cfgPath, true); File.Delete(old); }
                catch { }
            }
            if (!File.Exists(cfgPath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(cfgPath))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (k == "dir") txtDir.Text = v;
                    else if (k == "auto") chkAuto.Checked = (v == "1");
                    else if (k == "update") chkUpdate.Checked = (v == "1");
                }
            }
            catch (Exception ex) { AppendLog("[错误] 配置读取失败：" + ex.Message); }
        }

        const string AutoStartName = "梦幻清理缓存工具";

        // 开机自启动：注册表 HKCU\...\Run 键，当前用户级，无需管理员权限
        void LoadAutoStart()
        {
            try
            {
                RegistryKey rk = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
                chkAutoStart.Checked = rk != null && rk.GetValue(AutoStartName) != null;
                if (rk != null) rk.Close();
            }
            catch { }
        }

        void SaveAutoStart()
        {
            try
            {
                RegistryKey rk = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (rk == null) return;
                if (chkAutoStart.Checked)
                    rk.SetValue(AutoStartName, "\"" + Application.ExecutablePath + "\"");
                else
                    rk.DeleteValue(AutoStartName, false);
                rk.Close();
            }
            catch (Exception ex) { AppendLog("[错误] 开机自启动设置失败：" + ex.Message); }
        }

        void SaveConfig()
        {
            try { File.WriteAllLines(cfgPath, new[] { "dir=" + txtDir.Text.Trim(), "auto=" + (chkAuto.Checked ? "1" : "0"), "update=" + (chkUpdate.Checked ? "1" : "0") }); }
            catch (Exception ex) { AppendLog("[错误] 配置保存失败：" + ex.Message); }
        }
    }
}