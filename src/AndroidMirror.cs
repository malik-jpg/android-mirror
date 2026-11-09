// Android Mirror - GUI ringan untuk scrcpy (C# 5 / .NET Framework 4, tanpa dependensi)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("Android Mirror")]
[assembly: AssemblyProduct("Android Mirror")]
[assembly: AssemblyDescription("Mirror, kontrol & transfer file HP Android dari PC")]
[assembly: AssemblyCompany("MalixNet")]
[assembly: AssemblyCopyright("Copyright (c) 2026 MalixNet")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace AndroidMirror
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    class DeviceItem
    {
        public string Serial;
        public string State;
        public string Label;
        public override string ToString() { return Label; }
    }

    class MainForm : Form
    {
        // Theme
        static readonly Color Bg = Color.FromArgb(24, 26, 31);
        static readonly Color Card = Color.FromArgb(34, 37, 44);
        static readonly Color Fg = Color.FromArgb(232, 234, 238);
        static readonly Color Muted = Color.FromArgb(150, 156, 168);
        static readonly Color Accent = Color.FromArgb(61, 220, 132);
        static readonly Color Danger = Color.FromArgb(235, 87, 87);

        string scrcpyDir, scrcpyExe, adbExe;
        Process mirrorProc;
        bool injectHintShown;
        readonly string settingsPath;
        readonly Dictionary<string, string> settings = new Dictionary<string, string>();
        int refreshing;
        bool openFilesOnStart = Environment.GetCommandLineArgs().Any(a => a == "--files");

        ComboBox cbDevice, cbSize, cbFps, cbBitrate;
        Label lblStatus, lblDeviceInfo;
        CheckBox chkAudio, chkScreenOff, chkAwake, chkTop, chkFull, chkBorderless;
        Button btnStart, btnStop, btnFiles, btnRefresh, btnWifi, btnConnect, btnLog;
        TextBox txtIp, txtLog;
        System.Windows.Forms.Timer timer;
        int compactHeight, expandedHeight;

        public MainForm()
        {
            settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AndroidMirror", "settings.ini");
            LoadSettings();

            Text = "Android Mirror";
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            BackColor = Bg;
            ForeColor = Fg;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            FindScrcpy();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 3000;
            timer.Tick += delegate { RefreshDevicesAsync(); };
            timer.Start();

            Shown += delegate { RefreshDevicesAsync(); };
            FormClosing += OnClosing;
        }

        // ---------- UI ----------
        Label MakeLabel(string text, int x, int y, Color color, float size, bool bold)
        {
            var l = new Label();
            l.Text = text; l.Left = x; l.Top = y; l.AutoSize = true;
            l.ForeColor = color; l.BackColor = Color.Transparent;
            l.Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            Controls.Add(l);
            return l;
        }

        Button MakeButton(string text, int x, int y, int w, int h, Color back, Color fore)
        {
            var b = new Button();
            b.Text = text; b.Left = x; b.Top = y; b.Width = w; b.Height = h;
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
            b.BackColor = back; b.ForeColor = fore; b.Cursor = Cursors.Hand;
            Controls.Add(b);
            return b;
        }

        ComboBox MakeCombo(int x, int y, int w, string[] items, string saved, int defIndex)
        {
            var c = new ComboBox();
            c.Left = x; c.Top = y; c.Width = w;
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat; c.BackColor = Card; c.ForeColor = Fg;
            c.Items.AddRange(items);
            int idx = saved != null ? Array.IndexOf(items, saved) : -1;
            c.SelectedIndex = idx >= 0 ? idx : defIndex;
            Controls.Add(c);
            return c;
        }

        CheckBox MakeCheck(string text, int x, int y, string key, bool def)
        {
            var c = new CheckBox();
            c.Text = text; c.Left = x; c.Top = y; c.AutoSize = true;
            c.ForeColor = Fg; c.BackColor = Color.Transparent;
            c.Checked = GetBool(key, def);
            c.Tag = key;
            Controls.Add(c);
            return c;
        }

        void BuildUi()
        {
            const int L = 20, W = 400;
            int tx = L;
            try
            {
                var s = typeof(MainForm).Assembly.GetManifestResourceStream("logo.png");
                if (s != null)
                {
                    var pb = new PictureBox();
                    pb.Image = Image.FromStream(s);
                    pb.SizeMode = PictureBoxSizeMode.Zoom;
                    pb.Left = L; pb.Top = 14; pb.Width = 52; pb.Height = 52;
                    pb.BackColor = Color.Transparent;
                    Controls.Add(pb);
                    tx = L + 62;
                }
            }
            catch { }
            MakeLabel("Android Mirror", tx, 14, Fg, 16f, true);
            MakeLabel("Mirror && kontrol HP Android lewat USB / Wi-Fi", tx + 2, 46, Muted, 9f, false);

            // Device
            MakeLabel("PERANGKAT", L, 82, Muted, 8f, true);
            cbDevice = new ComboBox();
            cbDevice.Left = L; cbDevice.Top = 102; cbDevice.Width = W - 90;
            cbDevice.DropDownStyle = ComboBoxStyle.DropDownList;
            cbDevice.FlatStyle = FlatStyle.Flat; cbDevice.BackColor = Card; cbDevice.ForeColor = Fg;
            Controls.Add(cbDevice);
            btnRefresh = MakeButton("Refresh", L + W - 82, 101, 82, 27, Card, Fg);
            btnRefresh.Click += delegate { RefreshDevicesAsync(); };
            lblDeviceInfo = MakeLabel("Mencari perangkat...", L, 134, Muted, 8.5f, false);

            // Wi-Fi
            MakeLabel("WI-FI", L, 166, Muted, 8f, true);
            btnWifi = MakeButton("Aktifkan Wi-Fi dari USB", L, 186, 180, 28, Card, Fg);
            btnWifi.Click += delegate { EnableWifiAsync(); };
            txtIp = new TextBox();
            txtIp.Left = L + 190; txtIp.Top = 189; txtIp.Width = 130;
            txtIp.BackColor = Card; txtIp.ForeColor = Fg; txtIp.BorderStyle = BorderStyle.FixedSingle;
            txtIp.Text = Get("ip", "");
            Controls.Add(txtIp);
            btnConnect = MakeButton("Sambung", L + 326, 186, 74, 28, Card, Fg);
            btnConnect.Click += delegate { ConnectIpAsync(txtIp.Text.Trim()); };

            // Quality
            MakeLabel("KUALITAS", L, 228, Muted, 8f, true);
            MakeLabel("Resolusi", L, 250, Muted, 8.5f, false);
            MakeLabel("FPS", L + 140, 250, Muted, 8.5f, false);
            MakeLabel("Bitrate", L + 270, 250, Muted, 8.5f, false);
            cbSize = MakeCombo(L, 270, 130, new[] { "Asli", "1920", "1600", "1280", "1024" }, Get("size", null), 0);
            cbFps = MakeCombo(L + 140, 270, 120, new[] { "30", "60", "90", "120" }, Get("fps", null), 1);
            cbBitrate = MakeCombo(L + 270, 270, 130, new[] { "4M", "8M", "12M", "16M", "24M" }, Get("bitrate", null), 1);

            // Options
            MakeLabel("OPSI", L, 310, Muted, 8f, true);
            chkAudio = MakeCheck("Audio ke PC", L, 332, "audio", true);
            chkScreenOff = MakeCheck("Layar HP mati", L + 140, 332, "screenoff", false);
            chkAwake = MakeCheck("HP tetap menyala", L + 270, 332, "awake", true);
            chkTop = MakeCheck("Selalu di atas", L, 358, "top", false);
            chkFull = MakeCheck("Layar penuh", L + 140, 358, "full", false);
            chkBorderless = MakeCheck("Tanpa bingkai", L + 270, 358, "borderless", false);

            // Actions
            btnStart = MakeButton("▶  Mulai Mirror", L, 396, 206, 44, Accent, Color.FromArgb(10, 30, 18));
            btnStart.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            btnStart.Click += delegate { StartMirror(); };
            btnFiles = MakeButton("📁  File", L + 214, 396, 96, 44, Card, Fg);
            btnFiles.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnFiles.Click += delegate { OpenFiles(); };
            btnStop = MakeButton("Stop", L + 318, 396, 82, 44, Card, Danger);
            btnStop.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnStop.Enabled = false;
            btnStop.Click += delegate { StopMirror(); };

            lblStatus = MakeLabel("Siap.", L, 450, Muted, 8.5f, false);
            lblStatus.AutoSize = false; lblStatus.Width = W - 70; lblStatus.Height = 36;
            btnLog = MakeButton("Log ▾", L + W - 62, 448, 62, 24, Bg, Muted);
            btnLog.Font = new Font("Segoe UI", 8.5f);
            btnLog.Click += delegate { ToggleLog(); };

            txtLog = new TextBox();
            txtLog.Left = L; txtLog.Top = 490; txtLog.Width = W; txtLog.Height = 150;
            txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Color.FromArgb(16, 17, 21); txtLog.ForeColor = Muted;
            txtLog.BorderStyle = BorderStyle.None; txtLog.Font = new Font("Consolas", 8.5f);
            Controls.Add(txtLog);

            compactHeight = 490;
            expandedHeight = 656;
            ClientSize = new Size(W + 2 * L, compactHeight);

            var tip = new ToolTip();
            tip.SetToolTip(chkScreenOff, "Layar fisik HP gelap, mirror di PC tetap tampil");
            tip.SetToolTip(chkAudio, "Butuh Android 11 ke atas");
            tip.SetToolTip(btnWifi, "Colok USB dulu, lalu klik ini. Setelah tersambung kabel boleh dicabut.");
            tip.SetToolTip(cbSize, "Sisi terpanjang layar. Lebih kecil = lebih ringan");
        }

        void ToggleLog()
        {
            bool open = ClientSize.Height < expandedHeight;
            ClientSize = new Size(ClientSize.Width, open ? expandedHeight : compactHeight);
            btnLog.Text = open ? "Log ▴" : "Log ▾";
        }

        void SetStatus(string text, Color color)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => SetStatus(text, color))); return; }
            lblStatus.Text = text; lblStatus.ForeColor = color;
        }

        void Log(string line)
        {
            if (line == null) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => Log(line))); return; }
            if (txtLog.TextLength > 60000) txtLog.Text = txtLog.Text.Substring(30000);
            txtLog.AppendText(line + Environment.NewLine);
        }

        // ---------- scrcpy / adb ----------
        void FindScrcpy()
        {
            var candidates = new List<string>();
            candidates.Add(Path.Combine(Application.StartupPath, "scrcpy"));
            candidates.Add(Application.StartupPath);
            try
            {
                string pk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Packages");
                if (Directory.Exists(pk))
                    foreach (var d in Directory.GetDirectories(pk, "Genymobile.scrcpy*"))
                        candidates.AddRange(Directory.GetDirectories(d, "scrcpy-win64-*").OrderByDescending(x => x));
            }
            catch { }
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            candidates.AddRange(path.Split(';'));

            foreach (var c in candidates)
            {
                try
                {
                    if (!string.IsNullOrEmpty(c) && File.Exists(Path.Combine(c, "scrcpy.exe")) && File.Exists(Path.Combine(c, "adb.exe")))
                    {
                        scrcpyDir = c;
                        scrcpyExe = Path.Combine(c, "scrcpy.exe");
                        adbExe = Path.Combine(c, "adb.exe");
                        Log("scrcpy: " + c);
                        return;
                    }
                }
                catch { }
            }
            SetStatus("scrcpy tidak ditemukan. Instal: winget install Genymobile.scrcpy", Danger);
            btnStart.Enabled = false;
        }

        string RunAdb(string args, int timeoutMs)
        {
            if (adbExe == null) return "";
            try
            {
                var psi = new ProcessStartInfo(adbExe, args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                using (var p = Process.Start(psi))
                {
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var errTask = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return ""; }
                    outTask.Wait(2000); errTask.Wait(2000);
                    return (outTask.IsCompleted ? outTask.Result : "") + (errTask.IsCompleted ? errTask.Result : "");
                }
            }
            catch (Exception ex) { return "ERR " + ex.Message; }
        }

        void RefreshDevicesAsync()
        {
            if (adbExe == null || Interlocked.Exchange(ref refreshing, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var list = new List<DeviceItem>();
                try
                {
                    string outp = RunAdb("devices -l", 8000);
                    foreach (var raw in outp.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("List of") || line.StartsWith("*")) continue;
                        var parts = Regex.Split(line, @"\s+");
                        if (parts.Length < 2) continue;
                        var di = new DeviceItem { Serial = parts[0], State = parts[1] };
                        var m = Regex.Match(line, @"model:(\S+)");
                        string model = m.Success ? m.Groups[1].Value.Replace('_', ' ') : "";
                        if (di.State == "device")
                        {
                            string name = RunAdb("-s " + di.Serial + " shell getprop ro.product.marketname", 4000).Trim();
                            if (name.Length == 0 || name.StartsWith("ERR") || name.Contains("error")) name = model;
                            di.Label = name + "  (" + (di.Serial.Contains(":") ? "Wi-Fi " : "USB ") + di.Serial + ")";
                        }
                        else if (di.State == "unauthorized")
                            di.Label = di.Serial + "  - izinkan USB debugging di HP";
                        else
                            di.Label = di.Serial + "  - " + di.State;
                        list.Add(di);
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref refreshing, 0);
                }
                try { BeginInvoke((Action)(() => ApplyDevices(list))); } catch { }
            });
        }

        void ApplyDevices(List<DeviceItem> list)
        {
            string current = cbDevice.SelectedItem is DeviceItem ? ((DeviceItem)cbDevice.SelectedItem).Serial : Get("serial", null);
            string currentSig = string.Join("|", cbDevice.Items.Cast<object>().Select(o => o.ToString()).ToArray());
            string newSig = string.Join("|", list.Select(d => d.Label).ToArray());
            if (currentSig != newSig)
            {
                cbDevice.Items.Clear();
                foreach (var d in list) cbDevice.Items.Add(d);
                int sel = list.FindIndex(d => d.Serial == current);
                if (sel < 0) sel = list.FindIndex(d => d.State == "device");
                if (sel < 0 && list.Count > 0) sel = 0;
                cbDevice.SelectedIndex = sel;
            }

            if (openFilesOnStart && list.Any(x => x.State == "device"))
            {
                openFilesOnStart = false;
                OpenFiles();
            }

            if (list.Count == 0)
                lblDeviceInfo.Text = "Tidak ada HP. Nyalakan USB debugging lalu colok kabel.";
            else if (list.Any(d => d.State == "unauthorized"))
                lblDeviceInfo.Text = "Ketuk \"Allow / Izinkan\" di layar HP.";
            else
                lblDeviceInfo.Text = list.Count(d => d.State == "device") + " perangkat siap.";
        }

        DeviceItem SelectedDevice()
        {
            return cbDevice.SelectedItem as DeviceItem;
        }

        void EnableWifiAsync()
        {
            var d = SelectedDevice();
            if (d == null || d.State != "device" || d.Serial.Contains(":"))
            {
                MessageBox.Show(this, "Pilih HP yang tersambung lewat USB dulu.", "Wi-Fi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            btnWifi.Enabled = false;
            SetStatus("Mengaktifkan ADB Wi-Fi...", Muted);
            string serial = d.Serial;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string ip = null;
                string route = RunAdb("-s " + serial + " shell ip route", 5000);
                var m = Regex.Match(route, @"wlan\d+\s.*?src\s+(\d+\.\d+\.\d+\.\d+)");
                if (!m.Success) m = Regex.Match(route, @"src\s+(\d+\.\d+\.\d+\.\d+)");
                if (m.Success) ip = m.Groups[1].Value;
                if (ip == null)
                {
                    SetStatus("IP HP tidak ditemukan. Pastikan Wi-Fi HP menyala.", Danger);
                    BeginInvoke((Action)(() => btnWifi.Enabled = true));
                    return;
                }
                Log(RunAdb("-s " + serial + " tcpip 5555", 8000).Trim());
                Thread.Sleep(2000);
                string res = RunAdb("connect " + ip + ":5555", 10000).Trim();
                Log(res);
                BeginInvoke((Action)(() =>
                {
                    btnWifi.Enabled = true;
                    txtIp.Text = ip;
                    Set("ip", ip);
                    Set("serial", ip + ":5555");
                    SaveSettings();
                }));
                if (res.Contains("connected"))
                    SetStatus("Wi-Fi tersambung (" + ip + "). Kabel USB boleh dicabut.", Accent);
                else
                    SetStatus("Gagal sambung Wi-Fi: " + res, Danger);
                RefreshDevicesAsync();
            });
        }

        void ConnectIpAsync(string ip)
        {
            if (ip.Length == 0) { txtIp.Focus(); return; }
            if (!ip.Contains(":")) ip += ":5555";
            btnConnect.Enabled = false;
            SetStatus("Menyambung ke " + ip + "...", Muted);
            string target = ip;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string res = RunAdb("connect " + target, 10000).Trim();
                Log(res);
                bool ok = res.Contains("connected");
                SetStatus(ok ? "Tersambung ke " + target : "Gagal: " + res, ok ? Accent : Danger);
                BeginInvoke((Action)(() =>
                {
                    btnConnect.Enabled = true;
                    if (ok) { Set("ip", txtIp.Text.Trim()); Set("serial", target); SaveSettings(); }
                }));
                RefreshDevicesAsync();
            });
        }

        void OpenFiles()
        {
            var d = SelectedDevice();
            if (d == null || d.State != "device")
            {
                MessageBox.Show(this, "Belum ada HP yang siap. Sambungkan HP lewat USB atau Wi-Fi dulu.",
                    "File", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string name = d.Label.Split(new[] { "  (" }, StringSplitOptions.None)[0];
            var f = new FileForm(adbExe, d.Serial, name, Icon,
                k => Get(k, null), (k, v) => { Set(k, v); SaveSettings(); });
            f.Show(this);
        }

        void StartMirror()
        {
            var d = SelectedDevice();
            if (d == null || d.State != "device")
            {
                MessageBox.Show(this,
                    "Belum ada HP yang siap.\n\n1. Settings > About phone > ketuk Build number 7x\n2. Developer options > USB debugging ON\n3. Colok kabel data dan izinkan pop-up di HP",
                    "Android Mirror", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (mirrorProc != null && !mirrorProc.HasExited) return;

            var a = new List<string>();
            a.Add("-s " + d.Serial);
            a.Add("--window-title=\"Android Mirror - " + d.Label.Split(new[] { "  (" }, StringSplitOptions.None)[0].Replace("\"", "") + "\"");
            a.Add("--shortcut-mod=lalt");
            a.Add("--max-fps=" + cbFps.SelectedItem);
            a.Add("--video-bit-rate=" + cbBitrate.SelectedItem);
            if ((string)cbSize.SelectedItem != "Asli") a.Add("--max-size=" + cbSize.SelectedItem);
            if (!chkAudio.Checked) a.Add("--no-audio");
            if (chkScreenOff.Checked) a.Add("--turn-screen-off");
            if (chkAwake.Checked && !d.Serial.Contains(":")) a.Add("--stay-awake");
            if (chkTop.Checked) a.Add("--always-on-top");
            if (chkFull.Checked) a.Add("--fullscreen");
            if (chkBorderless.Checked) a.Add("--window-borderless");

            SaveCurrent(d.Serial);
            injectHintShown = false;

            var psi = new ProcessStartInfo(scrcpyExe, string.Join(" ", a.ToArray()));
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.WorkingDirectory = scrcpyDir;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.EnvironmentVariables["ADB"] = adbExe;
            psi.EnvironmentVariables["PATH"] = scrcpyDir + ";" + (Environment.GetEnvironmentVariable("PATH") ?? "");

            Log("> scrcpy " + psi.Arguments);
            try
            {
                mirrorProc = new Process();
                mirrorProc.StartInfo = psi;
                mirrorProc.EnableRaisingEvents = true;
                mirrorProc.OutputDataReceived += (s, e) => OnScrcpyLine(e.Data);
                mirrorProc.ErrorDataReceived += (s, e) => OnScrcpyLine(e.Data);
                mirrorProc.Exited += delegate { BeginInvoke((Action)OnMirrorExited); };
                mirrorProc.Start();
                mirrorProc.BeginOutputReadLine();
                mirrorProc.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                SetStatus("Gagal menjalankan scrcpy: " + ex.Message, Danger);
                return;
            }
            ApplyIconToMirrorWindow(mirrorProc);
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            SetStatus("Mirror berjalan. Tutup jendela mirror atau klik Stop untuk berhenti.", Accent);
        }

        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        Icon iconSmall, iconBig;

        // Ganti ikon jendela mirror scrcpy (title bar + taskbar) dengan ikon aplikasi ini
        void ApplyIconToMirrorWindow(Process proc)
        {
            try
            {
                if (iconSmall == null)
                {
                    iconSmall = new Icon(Icon, SystemInformation.SmallIconSize);
                    iconBig = new Icon(Icon, SystemInformation.IconSize);
                }
            }
            catch { return; }
            int pid = proc.Id;
            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int i = 0; i < 100; i++)   // tunggu sampai 30 detik jendela muncul
                {
                    Thread.Sleep(300);
                    try { if (proc.HasExited) return; } catch { return; }
                    IntPtr found = IntPtr.Zero;
                    EnumWindows((h, l) =>
                    {
                        uint wp; GetWindowThreadProcessId(h, out wp);
                        if (wp == pid && IsWindowVisible(h)) { found = h; return false; }
                        return true;
                    }, IntPtr.Zero);
                    if (found != IntPtr.Zero)
                    {
                        SendMessage(found, 0x0080, (IntPtr)0, iconSmall.Handle); // WM_SETICON ICON_SMALL
                        SendMessage(found, 0x0080, (IntPtr)1, iconBig.Handle);   // WM_SETICON ICON_BIG
                        return;
                    }
                }
            });
        }

        void OnScrcpyLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            Log(line);
            if (line.Contains("INJECT_EVENTS") && !injectHintShown)
            {
                injectHintShown = true;
                BeginInvoke((Action)(() =>
                {
                    SetStatus("Sentuh/scroll diblokir HP. Lihat petunjuk.", Danger);
                    MessageBox.Show(this,
                        "HP menolak input sentuh dari PC (gambar tetap tampil).\n\n" +
                        "Untuk Xiaomi / Redmi / POCO (MIUI / HyperOS):\n" +
                        "1. Settings > Additional settings > Developer options\n" +
                        "2. Nyalakan \"USB debugging (Security settings)\"\n" +
                        "   - perlu login Mi Account\n" +
                        "   - sering perlu SIM + data seluler aktif (matikan Wi-Fi sebentar)\n" +
                        "3. Restart HP, lalu mulai mirror lagi.\n\n" +
                        "Untuk merek lain: cari opsi serupa seperti \"Disable permission monitoring\" (OPPO/realme/vivo).",
                        "Input diblokir", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }));
            }
            else if (line.StartsWith("ERROR:"))
                SetStatus(line, Danger);
        }

        void OnMirrorExited()
        {
            btnStart.Enabled = scrcpyExe != null;
            btnStop.Enabled = false;
            int code = -1;
            try { code = mirrorProc.ExitCode; } catch { }
            if (lblStatus.ForeColor != Danger || code == 0)
                SetStatus(code == 0 ? "Mirror dihentikan." : "Mirror berhenti (kode " + code + "). Buka Log untuk detail.", code == 0 ? Muted : Danger);
            mirrorProc = null;
        }

        void StopMirror()
        {
            try { if (mirrorProc != null && !mirrorProc.HasExited) mirrorProc.Kill(); } catch { }
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            var d = SelectedDevice();
            SaveCurrent(d != null ? d.Serial : Get("serial", ""));
            StopMirror();
        }

        // ---------- settings ----------
        void SaveCurrent(string serial)
        {
            Set("serial", serial);
            Set("size", (string)cbSize.SelectedItem);
            Set("fps", (string)cbFps.SelectedItem);
            Set("bitrate", (string)cbBitrate.SelectedItem);
            Set("ip", txtIp.Text.Trim());
            foreach (var c in new[] { chkAudio, chkScreenOff, chkAwake, chkTop, chkFull, chkBorderless })
                Set((string)c.Tag, c.Checked ? "1" : "0");
            SaveSettings();
        }

        string Get(string k, string def) { string v; return settings.TryGetValue(k, out v) ? v : def; }
        bool GetBool(string k, bool def) { string v = Get(k, null); return v == null ? def : v == "1"; }
        void Set(string k, string v) { settings[k] = v ?? ""; }

        void LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsPath)) return;
                foreach (var line in File.ReadAllLines(settingsPath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) settings[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch { }
        }

        void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                File.WriteAllLines(settingsPath, settings.Select(kv => kv.Key + "=" + kv.Value).ToArray());
            }
            catch { }
        }
    }
}
