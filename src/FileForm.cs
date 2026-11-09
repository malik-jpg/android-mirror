// Android Mirror - jendela transfer file (HP <-> PC) lewat adb
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

namespace AndroidMirror
{
    class RemoteEntry
    {
        public string Name;
        public bool IsDir;
        public bool IsLink;
        public long Size;
        public string Date;
    }

    class FileForm : Form
    {
        const string Root = "/storage/emulated/0";

        static readonly Color Bg = Color.FromArgb(24, 26, 31);
        static readonly Color Card = Color.FromArgb(34, 37, 44);
        static readonly Color Fg = Color.FromArgb(232, 234, 238);
        static readonly Color Muted = Color.FromArgb(150, 156, 168);
        static readonly Color Accent = Color.FromArgb(61, 220, 132);
        static readonly Color Danger = Color.FromArgb(235, 87, 87);

        readonly string adbExe, serial, deviceName;
        readonly Func<string, string> getSetting;
        readonly Action<string, string> setSetting;
        string cwd = Root;
        string localDir;
        bool busy;
        readonly Stack<string> back = new Stack<string>();

        TextBox txtPath;
        ComboBox cbQuick;
        ListView list;
        ImageList icons;
        Label lblStatus, lblLocal;
        ProgressBar progress;
        CheckBox chkHidden;
        Button btnUp, btnRefresh, btnPull, btnPush, btnNewFolder, btnDelete, btnLocal, btnOpenLocal;

        static readonly string[][] Quick = new[]
        {
            new[] { "Memori internal", "" },
            new[] { "Kamera (DCIM/Camera)", "DCIM/Camera" },
            new[] { "Screenshot", "DCIM/Screenshots" },
            new[] { "Screenshot (Pictures)", "Pictures/Screenshots" },
            new[] { "Download", "Download" },
            new[] { "Dokumen", "Documents" },
            new[] { "Gambar", "Pictures" },
            new[] { "Video", "Movies" },
            new[] { "Musik", "Music" },
            new[] { "WhatsApp Media", "Android/media/com.whatsapp/WhatsApp/Media" },
            new[] { "Telegram", "Download/Telegram" },
        };

        public FileForm(string adbExe, string serial, string deviceName, Icon icon,
                        Func<string, string> getSetting, Action<string, string> setSetting)
        {
            this.adbExe = adbExe; this.serial = serial; this.deviceName = deviceName;
            this.getSetting = getSetting; this.setSetting = setSetting;

            string defLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Android Mirror");
            localDir = getSetting("localdir") ?? defLocal;
            string lastCwd = getSetting("cwd");
            if (!string.IsNullOrEmpty(lastCwd) && lastCwd.StartsWith("/")) cwd = lastCwd;

            Text = "File - " + deviceName;
            Icon = icon;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            BackColor = Bg; ForeColor = Fg;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 420);
            ClientSize = new Size(860, 580);
            KeyPreview = true;

            BuildUi();
            Shown += delegate { LoadDir(cwd, false); };
            KeyDown += OnKey;
            FormClosing += delegate { setSetting("cwd", cwd); setSetting("localdir", localDir); };
        }

        // ---------- UI ----------
        Button Btn(string text, int w, Color back, Color fore)
        {
            var b = new Button();
            b.Text = text; b.Width = w; b.Height = 30;
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
            b.BackColor = back; b.ForeColor = fore; b.Cursor = Cursors.Hand;
            b.Margin = new Padding(0, 0, 6, 0);
            return b;
        }

        void BuildUi()
        {
            // Baris atas: navigasi
            var nav = new FlowLayoutPanel();
            nav.Dock = DockStyle.Top; nav.Height = 46; nav.Padding = new Padding(12, 8, 12, 4);
            nav.WrapContents = false; nav.BackColor = Bg;

            btnUp = Btn("↑ Naik", 70, Card, Fg);
            btnUp.Click += delegate { GoUp(); };
            btnRefresh = Btn("⟳", 36, Card, Fg);
            btnRefresh.Click += delegate { LoadDir(cwd, false); };

            cbQuick = new ComboBox();
            cbQuick.DropDownStyle = ComboBoxStyle.DropDownList; cbQuick.Width = 190;
            cbQuick.FlatStyle = FlatStyle.Flat; cbQuick.BackColor = Card; cbQuick.ForeColor = Fg;
            cbQuick.Margin = new Padding(0, 4, 6, 0);
            cbQuick.Items.Add("Pintasan...");
            foreach (var q in Quick) cbQuick.Items.Add(q[0]);
            cbQuick.SelectedIndex = 0;
            cbQuick.SelectedIndexChanged += delegate
            {
                int i = cbQuick.SelectedIndex;
                if (i <= 0) return;
                string rel = Quick[i - 1][1];
                LoadDir(rel.Length == 0 ? Root : Root + "/" + rel, true);
                cbQuick.SelectedIndex = 0;
            };

            txtPath = new TextBox();
            txtPath.Width = 420; txtPath.BackColor = Card; txtPath.ForeColor = Fg;
            txtPath.BorderStyle = BorderStyle.FixedSingle; txtPath.Margin = new Padding(0, 4, 0, 0);
            txtPath.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LoadDir(NormalizePath(txtPath.Text), true); }
            };

            nav.Controls.AddRange(new Control[] { btnUp, btnRefresh, cbQuick, txtPath });
            nav.Resize += delegate { txtPath.Width = Math.Max(150, nav.ClientSize.Width - txtPath.Left - 12); };

            // Baris aksi
            var act = new FlowLayoutPanel();
            act.Dock = DockStyle.Top; act.Height = 42; act.Padding = new Padding(12, 2, 12, 6);
            act.WrapContents = false; act.BackColor = Bg;

            btnPull = Btn("⬇  Ambil ke PC", 130, Accent, Color.FromArgb(10, 30, 18));
            btnPull.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnPull.Click += delegate { PullSelected(false); };
            btnPush = Btn("⬆  Kirim ke HP", 120, Card, Fg);
            btnPush.Click += delegate { PushDialog(); };
            btnNewFolder = Btn("+ Folder", 80, Card, Fg);
            btnNewFolder.Click += delegate { NewFolder(); };
            btnDelete = Btn("Hapus", 70, Card, Danger);
            btnDelete.Click += delegate { DeleteSelected(); };
            chkHidden = new CheckBox();
            chkHidden.Text = "File tersembunyi"; chkHidden.AutoSize = true; chkHidden.ForeColor = Muted;
            chkHidden.Margin = new Padding(8, 6, 0, 0);
            chkHidden.CheckedChanged += delegate { LoadDir(cwd, false); };
            act.Controls.AddRange(new Control[] { btnPull, btnPush, btnNewFolder, btnDelete, chkHidden });

            // Bawah: folder PC + status
            var bottom = new Panel();
            bottom.Dock = DockStyle.Bottom; bottom.Height = 70; bottom.BackColor = Bg;
            bottom.Padding = new Padding(12, 4, 12, 8);

            lblLocal = new Label();
            lblLocal.AutoSize = false; lblLocal.Left = 12; lblLocal.Top = 10; lblLocal.Height = 20;
            lblLocal.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            lblLocal.ForeColor = Muted; lblLocal.AutoEllipsis = true;
            btnLocal = Btn("Ganti folder PC", 120, Card, Fg);
            btnLocal.Anchor = AnchorStyles.Top | AnchorStyles.Right; btnLocal.Height = 26;
            btnLocal.Click += delegate { ChooseLocal(); };
            btnOpenLocal = Btn("Buka folder", 96, Card, Fg);
            btnOpenLocal.Anchor = AnchorStyles.Top | AnchorStyles.Right; btnOpenLocal.Height = 26;
            btnOpenLocal.Click += delegate { OpenLocal(null); };

            progress = new ProgressBar();
            progress.Left = 12; progress.Top = 44; progress.Height = 8; progress.Width = 160;
            progress.Style = ProgressBarStyle.Marquee; progress.Visible = false;
            lblStatus = new Label();
            lblStatus.AutoSize = false; lblStatus.Left = 12; lblStatus.Top = 40; lblStatus.Height = 20;
            lblStatus.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            lblStatus.ForeColor = Muted; lblStatus.AutoEllipsis = true;
            lblStatus.Text = "Seret file dari Windows ke daftar ini untuk mengirim ke HP.";

            bottom.Controls.AddRange(new Control[] { lblLocal, btnLocal, btnOpenLocal, progress, lblStatus });
            bottom.Resize += delegate
            {
                int w = bottom.ClientSize.Width;
                btnOpenLocal.Left = w - 12 - btnOpenLocal.Width; btnOpenLocal.Top = 6;
                btnLocal.Left = btnOpenLocal.Left - 6 - btnLocal.Width; btnLocal.Top = 6;
                lblLocal.Width = btnLocal.Left - 18;
                lblStatus.Left = progress.Visible ? 182 : 12;
                lblStatus.Width = w - lblStatus.Left - 12;
            };
            UpdateLocalLabel();

            // Daftar file
            icons = new ImageList();
            icons.ColorDepth = ColorDepth.Depth32Bit;
            icons.ImageSize = new Size(16, 16);

            list = new ListView();
            list.Dock = DockStyle.Fill; list.View = View.Details; list.FullRowSelect = true;
            list.HideSelection = false; list.MultiSelect = true; list.BorderStyle = BorderStyle.None;
            list.BackColor = Card; list.ForeColor = Fg; list.SmallImageList = icons;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Columns.Add("Nama", 470);
            list.Columns.Add("Ukuran", 100, HorizontalAlignment.Right);
            list.Columns.Add("Diubah", 150);
            list.OwnerDraw = true;
            list.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(Bg)) e.Graphics.FillRectangle(b, e.Bounds);
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.LeftAndRightPadding |
                            (e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, e.Bounds, Muted, flags);
            };
            list.DrawItem += (s, e) => { e.DrawDefault = true; };
            list.DrawSubItem += (s, e) => { e.DrawDefault = true; };
            list.DoubleClick += delegate { OpenSelected(); };
            list.AllowDrop = true;
            list.DragEnter += (s, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) && !busy ? DragDropEffects.Copy : DragDropEffects.None; };
            list.DragDrop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) Push(files);
            };
            list.Resize += delegate { list.Columns[0].Width = Math.Max(200, list.ClientSize.Width - 260); };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Ambil ke PC", null, delegate { PullSelected(false); });
            menu.Items.Add("Ambil && buka", null, delegate { PullSelected(true); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Ganti nama", null, delegate { RenameSelected(); });
            menu.Items.Add("Hapus", null, delegate { DeleteSelected(); });
            list.ContextMenuStrip = menu;

            Controls.Add(list);
            Controls.Add(act);
            Controls.Add(nav);
            Controls.Add(bottom);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (txtPath.Focused) return;
            if (e.KeyCode == Keys.Back || (e.Alt && e.KeyCode == Keys.Up)) { GoUp(); e.Handled = true; }
            else if (e.KeyCode == Keys.F5) { LoadDir(cwd, false); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.F2) { RenameSelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { OpenSelected(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem it in list.Items) it.Selected = true; e.Handled = true; }
        }

        void UpdateLocalLabel() { lblLocal.Text = "Simpan di PC:  " + localDir; }

        void SetStatus(string text, Color color)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => SetStatus(text, color))); return; }
            lblStatus.Text = text; lblStatus.ForeColor = color;
        }

        void SetBusy(bool on)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => SetBusy(on))); return; }
            busy = on;
            progress.Visible = on;
            lblStatus.Left = on ? 182 : 12;
            lblStatus.Width = lblStatus.Parent.ClientSize.Width - lblStatus.Left - 12;
            foreach (var b in new[] { btnPull, btnPush, btnNewFolder, btnDelete, btnUp, btnRefresh }) b.Enabled = !on;
            UseWaitCursor = on && false;
        }

        // ---------- adb ----------
        static string Q(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }
        static string SQ(string s) { return "'" + s.Replace("'", "'\\''") + "'"; }

        string Adb(string args, int timeoutMs)
        {
            var psi = new ProcessStartInfo(adbExe, "-s " + serial + " " + args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8;
            try
            {
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEndAsync();
                    var e = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return "ERR: timeout"; }
                    o.Wait(3000); e.Wait(3000);
                    string res = (o.IsCompleted ? o.Result : "") + (e.IsCompleted ? e.Result : "");
                    if (p.ExitCode != 0 && !res.StartsWith("ERR")) res = "ERR: " + res.Trim();
                    return res;
                }
            }
            catch (Exception ex) { return "ERR: " + ex.Message; }
        }

        string Shell(string cmd, int timeoutMs) { return Adb("shell " + Q(cmd), timeoutMs); }

        static string NormalizePath(string p)
        {
            p = (p ?? "").Trim().Replace('\\', '/');
            if (p.Length == 0) return Root;
            if (p == "/sdcard" || p.StartsWith("/sdcard/")) p = Root + p.Substring(7);
            if (!p.StartsWith("/")) p = Root + "/" + p;
            while (p.Length > 1 && p.EndsWith("/")) p = p.Substring(0, p.Length - 1);
            return p;
        }

        static string Join(string dir, string name) { return dir == "/" ? "/" + name : dir + "/" + name; }

        static readonly Regex LsLine = new Regex(
            @"^([\-dlcbps])[rwxsStT\-]{9}\S*\s+\d+\s+\S+\s+\S+\s+([\d,\s]+?)\s+(\d{4}-\d{2}-\d{2})\s+(\d{2}:\d{2})\s+(.+)$");

        List<RemoteEntry> Parse(string output)
        {
            var res = new List<RemoteEntry>();
            foreach (var raw in output.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                var m = LsLine.Match(line);
                if (!m.Success) continue;
                var e = new RemoteEntry();
                char t = m.Groups[1].Value[0];
                e.Name = m.Groups[5].Value;
                e.IsLink = t == 'l';
                if (e.IsLink)
                {
                    int i = e.Name.IndexOf(" -> ");
                    if (i > 0) e.Name = e.Name.Substring(0, i);
                }
                e.IsDir = t == 'd' || e.IsLink;
                long sz; long.TryParse(m.Groups[2].Value.Trim(), out sz); e.Size = sz;
                e.Date = m.Groups[3].Value + " " + m.Groups[4].Value;
                if (e.Name == "." || e.Name == "..") continue;
                res.Add(e);
            }
            return res;
        }

        // ---------- navigasi ----------
        void LoadDir(string path, bool pushHistory)
        {
            if (busy) return;
            path = NormalizePath(path);
            SetBusy(true);
            SetStatus("Membuka " + path + " ...", Muted);
            bool hidden = chkHidden.Checked;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string outp = Shell("ls -lA " + SQ(path + "/"), 20000);
                var entries = Parse(outp);
                bool failed = entries.Count == 0 && (outp.StartsWith("ERR") || outp.Contains("No such file") || outp.Contains("Permission denied") || outp.Contains("Not a directory"));
                BeginInvoke((Action)(() =>
                {
                    SetBusy(false);
                    if (failed)
                    {
                        string msg = outp.Replace("ERR: ", "").Trim();
                        if (msg.Contains("no devices") || msg.Contains("not found") && msg.Contains("device"))
                            msg = "HP tidak tersambung.";
                        SetStatus("Tidak bisa membuka: " + msg, Danger);
                        txtPath.Text = cwd;
                        return;
                    }
                    if (pushHistory && path != cwd) back.Push(cwd);
                    cwd = path;
                    txtPath.Text = cwd;
                    Fill(entries.Where(x => hidden || !x.Name.StartsWith(".")).ToList());
                    int nd = entries.Count(x => x.IsDir), nf = entries.Count - nd;
                    SetStatus(nd + " folder, " + nf + " file", Muted);
                    btnUp.Enabled = cwd != "/";
                }));
            });
        }

        void Fill(List<RemoteEntry> entries)
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var e in entries.OrderBy(x => x.IsDir ? 0 : 1).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var it = new ListViewItem(e.Name);
                it.ImageKey = IconKey(e);
                it.SubItems.Add(e.IsDir ? "" : HumanSize(e.Size));
                it.SubItems.Add(e.Date);
                it.Tag = e;
                list.Items.Add(it);
            }
            list.EndUpdate();
        }

        void GoUp()
        {
            if (busy || cwd == "/") return;
            int i = cwd.LastIndexOf('/');
            LoadDir(i <= 0 ? "/" : cwd.Substring(0, i), true);
        }

        List<RemoteEntry> Selected()
        {
            return list.SelectedItems.Cast<ListViewItem>().Select(i => (RemoteEntry)i.Tag).ToList();
        }

        void OpenSelected()
        {
            var sel = Selected();
            if (sel.Count != 1) return;
            if (sel[0].IsDir) LoadDir(Join(cwd, sel[0].Name), true);
            else PullSelected(true);
        }

        // ---------- transfer ----------
        void PullSelected(bool openAfter)
        {
            var sel = Selected();
            if (busy) return;
            if (sel.Count == 0) { SetStatus("Pilih file/folder dulu (Ctrl+klik untuk banyak).", Danger); return; }
            try { Directory.CreateDirectory(localDir); }
            catch (Exception ex) { SetStatus("Folder PC tidak bisa dibuat: " + ex.Message, Danger); return; }

            string dir = cwd, dest = localDir;
            SetBusy(true);
            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0; string lastErr = null; string lastLocal = null;
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < sel.Count; i++)
                {
                    var e = sel[i];
                    SetStatus("Mengambil " + (i + 1) + "/" + sel.Count + ": " + e.Name, Muted);
                    string remote = Join(dir, e.Name);
                    string res = Adb("pull -a " + Q(remote) + " " + Q(dest), 6 * 3600 * 1000);
                    if (res.StartsWith("ERR")) lastErr = e.Name + ": " + res.Substring(4).Trim();
                    else { ok++; lastLocal = Path.Combine(dest, e.Name); }
                }
                SetBusy(false);
                if (lastErr != null)
                    SetStatus(ok + "/" + sel.Count + " berhasil. Gagal: " + lastErr, Danger);
                else
                    SetStatus(ok + " item tersimpan di PC (" + sw.Elapsed.TotalSeconds.ToString("0.0") + " dtk).", Accent);
                if (openAfter && lastLocal != null && sel.Count == 1 && File.Exists(lastLocal))
                    BeginInvoke((Action)(() =>
                    {
                        try { Process.Start(new ProcessStartInfo(lastLocal) { UseShellExecute = true }); }
                        catch { OpenLocal(lastLocal); }
                    }));
            });
        }

        void PushDialog()
        {
            if (busy) return;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Multiselect = true;
                dlg.Title = "Pilih file untuk dikirim ke HP (" + cwd + ")";
                if (dlg.ShowDialog(this) == DialogResult.OK) Push(dlg.FileNames);
            }
        }

        void Push(string[] paths)
        {
            if (busy) return;
            string dir = cwd;
            SetBusy(true);
            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0; string lastErr = null;
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < paths.Length; i++)
                {
                    string local = paths[i].TrimEnd('\\');
                    string name = Path.GetFileName(local);
                    SetStatus("Mengirim " + (i + 1) + "/" + paths.Length + ": " + name, Muted);
                    string res = Adb("push " + Q(local) + " " + Q(dir + "/"), 6 * 3600 * 1000);
                    if (res.StartsWith("ERR")) lastErr = name + ": " + res.Substring(4).Trim();
                    else
                    {
                        ok++;
                        // daftarkan ke galeri / media store
                        Shell("am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d " + SQ("file://" + Join(dir, name)), 8000);
                    }
                }
                SetBusy(false);
                BeginInvoke((Action)(() =>
                {
                    LoadDir(dir, false);
                    if (lastErr != null) SetStatus(ok + "/" + paths.Length + " terkirim. Gagal: " + lastErr, Danger);
                    else SetStatus(ok + " item terkirim ke " + dir + " (" + sw.Elapsed.TotalSeconds.ToString("0.0") + " dtk).", Accent);
                }));
            });
        }

        // ---------- kelola file di HP ----------
        string Prompt(string title, string label, string value)
        {
            using (var f = new Form())
            {
                f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = false; f.MaximizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent; f.BackColor = Bg; f.ForeColor = Fg; f.Font = Font;
                f.AutoScaleMode = AutoScaleMode.Dpi; f.AutoScaleDimensions = new SizeF(96f, 96f);
                f.ClientSize = new Size(380, 120);
                var l = new Label { Text = label, Left = 14, Top = 14, AutoSize = true, ForeColor = Muted };
                var t = new TextBox { Left = 14, Top = 38, Width = 352, Text = value, BackColor = Card, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle };
                var ok = new Button { Text = "OK", Left = 206, Top = 78, Width = 76, Height = 28, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.Black };
                var cancel = new Button { Text = "Batal", Left = 290, Top = 78, Width = 76, Height = 28, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Card, ForeColor = Fg };
                ok.FlatAppearance.BorderSize = 0; cancel.FlatAppearance.BorderSize = 0;
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                f.Shown += delegate { t.SelectAll(); t.Focus(); };
                return f.ShowDialog(this) == DialogResult.OK ? t.Text.Trim() : null;
            }
        }

        static bool ValidName(string n)
        {
            return !string.IsNullOrEmpty(n) && n != "." && n != ".." && n.IndexOf('/') < 0;
        }

        void RunShellThenReload(string cmd, string doneMsg)
        {
            string dir = cwd;
            SetBusy(true);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string res = Shell(cmd + " && echo __OK__", 120000);
                SetBusy(false);
                BeginInvoke((Action)(() =>
                {
                    LoadDir(dir, false);
                    if (res.Contains("__OK__")) SetStatus(doneMsg, Accent);
                    else SetStatus("Gagal: " + res.Replace("ERR:", "").Trim(), Danger);
                }));
            });
        }

        void NewFolder()
        {
            if (busy) return;
            string n = Prompt("Folder baru", "Nama folder di " + cwd, "Folder baru");
            if (n == null) return;
            if (!ValidName(n)) { SetStatus("Nama folder tidak valid.", Danger); return; }
            RunShellThenReload("mkdir -p " + SQ(Join(cwd, n)), "Folder \"" + n + "\" dibuat.");
        }

        void RenameSelected()
        {
            var sel = Selected();
            if (busy || sel.Count != 1) return;
            string n = Prompt("Ganti nama", "Nama baru", sel[0].Name);
            if (n == null || n == sel[0].Name) return;
            if (!ValidName(n)) { SetStatus("Nama tidak valid.", Danger); return; }
            RunShellThenReload("mv -n " + SQ(Join(cwd, sel[0].Name)) + " " + SQ(Join(cwd, n)), "Nama diganti.");
        }

        void DeleteSelected()
        {
            var sel = Selected();
            if (busy || sel.Count == 0) return;
            string what = sel.Count == 1 ? "\"" + sel[0].Name + "\"" : sel.Count + " item";
            bool anyDir = sel.Any(x => x.IsDir && !x.IsLink);
            var r = MessageBox.Show(this,
                "Hapus " + what + " dari HP secara permanen?" + (anyDir ? "\nFolder akan dihapus beserta seluruh isinya." : "") + "\n\nTindakan ini tidak bisa dibatalkan.",
                "Hapus dari HP", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            string cmd = "rm -rf " + string.Join(" ", sel.Select(x => SQ(Join(cwd, x.Name))).ToArray());
            RunShellThenReload(cmd, what + " dihapus.");
        }

        // ---------- folder PC ----------
        void ChooseLocal()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Pilih folder di PC untuk menyimpan file dari HP";
                dlg.ShowNewFolderButton = true;
                if (Directory.Exists(localDir)) dlg.SelectedPath = localDir;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    localDir = dlg.SelectedPath.TrimEnd('\\');
                    if (localDir.EndsWith(":")) localDir += "\\";
                    setSetting("localdir", localDir);
                    UpdateLocalLabel();
                }
            }
        }

        void OpenLocal(string selectFile)
        {
            try
            {
                Directory.CreateDirectory(localDir);
                if (selectFile != null && File.Exists(selectFile))
                    Process.Start("explorer.exe", "/select," + Q(selectFile));
                else
                    Process.Start("explorer.exe", Q(localDir));
            }
            catch (Exception ex) { SetStatus(ex.Message, Danger); }
        }

        // ---------- ikon & format ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        string IconKey(RemoteEntry e)
        {
            string key = e.IsDir ? "<dir>" : (Path.GetExtension(e.Name) ?? "").ToLowerInvariant();
            if (key.Length == 0) key = "<file>";
            if (icons.Images.ContainsKey(key)) return key;
            try
            {
                var info = new SHFILEINFO();
                const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_USEFILEATTRIBUTES = 0x10;
                uint attr = e.IsDir ? 0x10u : 0x80u;
                string probe = e.IsDir ? "folder" : "file" + (key.StartsWith("<") ? "" : key);
                SHGetFileInfo(probe, attr, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
                if (info.hIcon != IntPtr.Zero)
                {
                    using (var ic = Icon.FromHandle(info.hIcon)) icons.Images.Add(key, ic.ToBitmap());
                    DestroyIcon(info.hIcon);
                }
            }
            catch { }
            return key;
        }

        static string HumanSize(long b)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = b; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return (i == 0 ? v.ToString("0") : v.ToString(v < 10 ? "0.0" : "0")) + " " + u[i];
        }
    }
}
