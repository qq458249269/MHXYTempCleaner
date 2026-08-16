using System;
using System.Drawing;
using System.IO;
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
            }
            finally { Directory.Delete(dir, true); }
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "mh_selftest_result.txt"), ok);
            MessageBox.Show("自检结果：" + ok, "梦幻清理缓存工具");
        }
    }

    class MainForm : Form
    {
        TextBox txtDir, txtLog;
        CheckBox chkAuto, chkAutoStart;
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
            Text = "梦幻西游缓存清理工具";
            ClientSize = new Size(660, 400);
            MinimumSize = new Size(500, 300);
            cfgPath = GetCfgPath();

            chkAuto = new CheckBox { Text = "启动时自动清理", AutoSize = true, Checked = true, Anchor = AnchorStyles.Left };
            chkAutoStart = new CheckBox { Text = "开机自启动", AutoSize = true, Anchor = AnchorStyles.Left };
            txtDir = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            Button btnBrowse = new Button { Text = "浏览...", Anchor = AnchorStyles.Right };
            btnClean = new Button { Text = "一键清理", Anchor = AnchorStyles.Right };
            txtLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };

            btnBrowse.Click += OnBrowse;
            btnClean.Click += OnClean;

            TableLayoutPanel top = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = 36, ColumnCount = 5, Padding = new Padding(2, 4, 2, 0)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.Controls.Add(chkAuto); top.Controls.Add(chkAutoStart); top.Controls.Add(txtDir); top.Controls.Add(btnBrowse); top.Controls.Add(btnClean);

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(top);
            root.Controls.Add(txtLog);
            Controls.Add(root);

            chkAuto.CheckedChanged += delegate { SaveConfig(); };
            chkAutoStart.CheckedChanged += delegate { SaveAutoStart(); };
            LoadConfig();
            LoadAutoStart();
            Shown += delegate
            {
                if (chkAuto.Checked)
                {
                    AppendLog("[自动执行] 启动自动清理...");
                    RunClean();
                }
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
            try { File.WriteAllLines(cfgPath, new[] { "dir=" + txtDir.Text.Trim(), "auto=" + (chkAuto.Checked ? "1" : "0") }); }
            catch (Exception ex) { AppendLog("[错误] 配置保存失败：" + ex.Message); }
        }
    }
}