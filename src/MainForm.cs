using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SaruTask
{
    internal sealed class MainForm : Form
    {
        private AppSettings settings;
        private readonly TextBox search = new TextBox();
        private readonly CheckBox recursive = new CheckBox();
        private readonly ListView results = new ListView();
        private readonly Label summary = new Label();
        private readonly Label scope = new Label();
        private readonly Label empty = new Label();
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 350 };
        private readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 30000 };
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly RegisteredWaitHandle instanceWait;
        private readonly ToolTip tips = new ToolTip { AutoPopDelay = 20000 };
        private readonly bool startHidden;
        private List<SearchEntry> catalog = new List<SearchEntry>();
        private List<string> warnings = new List<string>();
        private CancellationTokenSource scanCancellation;
        private bool exiting, hotkeyRegistered, scanning, hasScanned;
        private int sortColumn;
        private bool descending;
        private string startupWarning;
        private readonly Icon appIcon;

        public MainForm(AppSettings initial, bool background, EventWaitHandle showEvent, string warning)
        {
            settings = initial; startHidden = background; startupWarning = warning;
            Text = "SaruTask"; Font = new Font("Yu Gothic UI", 10); BackColor = Color.White;
            Size = new Size(1000, 650); MinimumSize = new Size(760, 400); StartPosition = FormStartPosition.CenterScreen;
            appIcon = CreateIcon(); Icon = appIcon;
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(18, 12, 18, 8) };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            heading.Controls.Add(new Label { Text = "SaruTask  /  タスクをすぐに開く", Font = new Font(Font.FontFamily, 12, FontStyle.Bold), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            var refresh = new Button { Text = "更新", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat }; refresh.FlatAppearance.BorderColor = Color.Gainsboro;
            var configure = new Button { Text = "設定", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat }; configure.FlatAppearance.BorderColor = Color.Gainsboro;
            refresh.Click += delegate { BeginScan(); }; configure.Click += delegate { Configure(); };
            heading.Controls.Add(refresh, 1, 0); heading.Controls.Add(configure, 2, 0); shell.Controls.Add(heading, 0, 0);
            search.Dock = DockStyle.Fill; search.Font = new Font(Font.FontFamily, 15); search.Margin = new Padding(0, 5, 0, 5); search.AccessibleName = "タスク名を検索";
            search.TextChanged += delegate { Filter(); }; search.KeyDown += SearchKeyDown; shell.Controls.Add(search, 0, 1);
            var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
            scope.Dock = DockStyle.Fill; scope.TextAlign = ContentAlignment.MiddleLeft; scope.ForeColor = Color.DimGray; scope.AutoEllipsis = true;
            recursive.Text = "サブフォルダも検索"; recursive.Dock = DockStyle.Fill; recursive.Checked = false;
            recursive.CheckedChanged += delegate { RebuildWatchers(); BeginScan(); };
            options.Controls.Add(scope, 0, 0); options.Controls.Add(recursive, 1, 0); shell.Controls.Add(options, 0, 2);
            results.Dock = DockStyle.Fill; results.View = View.Details; results.FullRowSelect = true; results.HideSelection = false; results.MultiSelect = false;
            results.BorderStyle = BorderStyle.FixedSingle; results.ShowItemToolTips = true; results.AccessibleName = "検索結果";
            results.Columns.Add("名前", 370); results.Columns.Add("種類", 95); results.Columns.Add("フォルダ", 370); results.Columns.Add("更新日時", 145);
            var icons = new ImageList { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit };
            icons.Images.Add(DrawEntryIcon(true)); icons.Images.Add(DrawEntryIcon(false)); results.SmallImageList = icons;
            results.ItemActivate += delegate { OpenSelected(false); }; results.KeyDown += ResultsKeyDown;
            results.ColumnClick += delegate(object sender, ColumnClickEventArgs e) { if (sortColumn == e.Column) descending = !descending; else { sortColumn = e.Column; descending = false; } Filter(); };
            var menu = new ContextMenuStrip(); menu.Items.Add("開く", null, delegate { OpenSelected(false); }); menu.Items.Add("保存場所を開く", null, delegate { OpenSelected(true); });
            menu.Items.Add("パスをコピー", null, delegate { CopyPath(); }); results.ContextMenuStrip = menu;
            var resultPanel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            resultPanel.Controls.Add(results);
            empty.Text = "検索対象を読み込んでいます…"; empty.TextAlign = ContentAlignment.MiddleCenter; empty.BackColor = Color.White;
            empty.AutoSize = false; empty.Size = new Size(540, 65); empty.Anchor = AnchorStyles.None;
            resultPanel.Controls.Add(empty); resultPanel.Resize += delegate { empty.Location = new Point(Math.Max(0, (resultPanel.Width - empty.Width) / 2), Math.Max(40, (resultPanel.Height - empty.Height) / 2)); };
            shell.Controls.Add(resultPanel, 0, 3);
            summary.Dock = DockStyle.Fill; summary.TextAlign = ContentAlignment.MiddleLeft; summary.ForeColor = Color.DimGray; summary.AutoEllipsis = true;
            shell.Controls.Add(summary, 0, 4); Controls.Add(shell);
            var menuBar = new MenuStrip { Font = Font };
            var help = new ToolStripMenuItem("ヘルプ(&H)");
            help.DropDownItems.Add("更新(&U)…", null, delegate { ShowUpdate(); });
            help.DropDownItems.Add("バージョン(&A)…", null, delegate { ShowVersion(); });
            menuBar.Items.Add(help); MainMenuStrip = menuBar; Controls.Add(menuBar);
            var trayMenu = new ContextMenuStrip(); trayMenu.Items.Add("SaruTask を表示  (Ctrl + Alt + T)", null, delegate { BringToFrontForSearch(); });
            trayMenu.Items.Add("更新", null, delegate { BeginScan(); }); trayMenu.Items.Add("設定…", null, delegate { BringToFrontForSearch(); Configure(); });
            trayMenu.Items.Add(new ToolStripSeparator()); trayMenu.Items.Add("終了", null, delegate { exiting = true; Close(); });
            tray = new NotifyIcon { Icon = appIcon, Text = "SaruTask — Ctrl + Alt + T", ContextMenuStrip = trayMenu, Visible = true };
            tray.DoubleClick += delegate { BringToFrontForSearch(); };
            debounce.Tick += delegate { debounce.Stop(); BeginScan(); };
            poll.Tick += delegate { if (!scanning) BeginScan(); }; poll.Start();
            // Register before returning to Application.Run so a second launch can always activate us.
            var windowHandle = Handle;
            instanceWait = ThreadPool.RegisterWaitForSingleObject(showEvent, delegate { Post(delegate { BringToFrontForSearch(); }); }, null, -1, false);
            Shown += delegate { RebuildWatchers(); BeginScan(); search.Focus(); if (startHidden) Hide(); if (startupWarning != null) Notify(startupWarning); };
            Activated += delegate { search.Focus(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = true; }
                if (e.KeyCode == Keys.F5) { BeginScan(); e.Handled = true; }
                if (e.Control && e.KeyCode == Keys.L) { search.Focus(); search.SelectAll(); e.SuppressKeyPress = true; }
            };
            UpdateScope();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            hotkeyRegistered = NativeMethods.RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.T);
            if (!hotkeyRegistered) startupWarning = "Ctrl + Alt + Tを登録できませんでした。他のアプリが使用中の可能性があります。トレイから開けます。";
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (hotkeyRegistered) NativeMethods.UnregisterHotKey(Handle, 1);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1) BringToFrontForSearch();
            base.WndProc(ref m);
        }

        private void Post(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        public void BringToFrontForSearch()
        {
            Show(); WindowState = FormWindowState.Normal;
            TopMost = true; Activate(); NativeMethods.SetForegroundWindow(Handle); TopMost = false;
            search.Focus(); search.SelectAll(); BeginScan();
        }

        private async void BeginScan()
        {
            if (exiting || IsDisposed) return;
            if (scanCancellation != null) scanCancellation.Cancel();
            var cancellation = new CancellationTokenSource(); scanCancellation = cancellation;
            var roots = settings.Roots.ToArray(); bool includeChildren = recursive.Checked; scanning = true; UpdateSummary();
            try
            {
                var scan = await Task.Run(() => SearchCatalog.Scan(roots, includeChildren, cancellation.Token));
                if (cancellation.IsCancellationRequested || IsDisposed || exiting) return;
                bool changed = !hasScanned || CatalogChanged(scan.Entries);
                catalog = scan.Entries; warnings = scan.Warnings; scanning = false; hasScanned = true;
                if (changed) Filter(); else { empty.Visible = results.Items.Count == 0; UpdateSummary(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!cancellation.IsCancellationRequested && !IsDisposed) { scanning = false; warnings = new List<string> { ex.Message }; UpdateSummary(); } }
            finally { if (scanCancellation == cancellation) scanCancellation = null; cancellation.Dispose(); }
        }

        private void Filter()
        {
            string selected = results.SelectedItems.Count == 0 ? null : ((SearchEntry)results.SelectedItems[0].Tag).FullPath;
            string top = results.TopItem == null ? null : ((SearchEntry)results.TopItem.Tag).FullPath;
            var tokens = SearchCatalog.Tokens(search.Text);
            IEnumerable<SearchEntry> filtered = catalog.Where(entry => SearchCatalog.Matches(entry, tokens));
            Func<SearchEntry, string> key = entry => sortColumn == 1 ? (entry.IsDirectory ? "0" : "1") : sortColumn == 2 ? entry.Parent : entry.Name;
            IOrderedEnumerable<SearchEntry> ordered;
            if (sortColumn == 3) ordered = descending ? filtered.OrderByDescending(x => x.Modified) : filtered.OrderBy(x => x.Modified);
            else ordered = descending ? filtered.OrderByDescending(key, StringComparer.CurrentCultureIgnoreCase) : filtered.OrderBy(key, StringComparer.CurrentCultureIgnoreCase);
            var visible = ordered.ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase).ToList();
            results.BeginUpdate(); results.Items.Clear();
            ListViewItem topItem = null;
            foreach (var entry in visible)
            {
                var item = new ListViewItem(entry.Name, entry.IsDirectory ? 0 : 1) { Tag = entry, ToolTipText = entry.FullPath };
                item.SubItems.Add(entry.IsDirectory ? "フォルダ" : "Markdown"); item.SubItems.Add(entry.Parent); item.SubItems.Add(entry.Modified.ToString("yyyy/MM/dd HH:mm"));
                results.Items.Add(item); if (entry.FullPath == selected) item.Selected = true;
                if (entry.FullPath == top) topItem = item;
            }
            if (results.Items.Count > 0 && results.SelectedItems.Count == 0) results.Items[0].Selected = true;
            results.EndUpdate(); empty.Visible = results.Items.Count == 0;
            if (topItem != null) results.TopItem = topItem;
            empty.Text = scanning ? "検索対象を読み込んでいます…" : catalog.Count == 0 ? "対象のフォルダ・Markdownがありません。\n「設定」で検索対象を確認してください。" : "一致するタスクがありません。\n名前の一部を入力してください。";
            UpdateSummary();
        }

        private bool CatalogChanged(List<SearchEntry> updated)
        {
            if (catalog.Count != updated.Count) return true;
            var old = catalog.ToDictionary(x => x.FullPath, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in updated)
            {
                SearchEntry previous;
                if (!old.TryGetValue(entry.FullPath, out previous) || previous.FullPath != entry.FullPath || previous.Modified != entry.Modified || previous.IsDirectory != entry.IsDirectory) return true;
            }
            return false;
        }

        private void UpdateScope()
        {
            scope.Text = settings.Roots.Count + "か所  ·  フォルダ / Markdown  ·  " + (recursive.Checked ? "サブフォルダを含む" : "直下のみ");
            tips.SetToolTip(scope, string.Join(Environment.NewLine, settings.Roots));
        }

        private void UpdateSummary()
        {
            UpdateScope();
            summary.Text = (scanning ? "更新中…  " : "") + results.Items.Count + " / " + catalog.Count + "件    Enter: 開く   Ctrl+Enter: 保存場所   Esc: トレイへ";
            if (warnings.Count > 0) summary.Text += "    ⚠ 読み取りエラー " + warnings.Count + "件";
            if (!hotkeyRegistered) summary.Text += "    ⚠ ホットキーを登録できません";
            summary.ForeColor = warnings.Count > 0 || !hotkeyRegistered ? Color.FromArgb(150, 85, 15) : Color.DimGray;
            tips.SetToolTip(summary, string.Join(Environment.NewLine, warnings.Take(15)));
        }

        private void RebuildWatchers()
        {
            foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear();
            foreach (string root in settings.Roots)
            {
                try
                {
                    var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = recursive.Checked, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
                    FileSystemEventHandler changed = delegate { Post(ScheduleScan); };
                    watcher.Created += changed; watcher.Deleted += changed; watcher.Changed += changed;
                    watcher.Renamed += delegate { Post(ScheduleScan); }; watcher.Error += delegate { Post(ScheduleScan); };
                    watchers.Add(watcher); watcher.EnableRaisingEvents = true;
                }
                catch (Exception ex) { Trace.WriteLine("Watcher: " + ex.Message); }
            }
        }

        private void ScheduleScan() { debounce.Stop(); debounce.Start(); }
        private void SearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && results.Items.Count > 0) { results.Focus(); results.Items[0].Focused = true; e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Enter) { OpenSelected(e.Control); e.SuppressKeyPress = true; }
        }
        private void ResultsKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { OpenSelected(e.Control); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.C) { CopyPath(); e.SuppressKeyPress = true; }
        }
        private void OpenSelected(bool location)
        {
            if (results.SelectedItems.Count == 0) return;
            var entry = (SearchEntry)results.SelectedItems[0].Tag;
            try
            {
                if (location) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + entry.FullPath + "\"") { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, "開けませんでした。\n" + ex.Message, "SaruTask", MessageBoxButtons.OK, MessageBoxIcon.Warning); BeginScan(); }
        }
        private void CopyPath()
        {
            if (results.SelectedItems.Count == 0) return;
            try { Clipboard.SetText(((SearchEntry)results.SelectedItems[0].Tag).FullPath); }
            catch (Exception ex) { MessageBox.Show(this, "コピーできませんでした。\n" + ex.Message); }
        }
        private void Configure()
        {
            using (var dialog = new SettingsForm(settings))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var updated = new AppSettings { Roots = dialog.Roots };
                    updated.Save(); settings = updated; RebuildWatchers(); BeginScan();
                    SettingsForm.SetStartup(dialog.StartWithWindows);
                }
                catch (Exception ex) { MessageBox.Show(this, "設定の保存中にエラーが発生しました。\n" + ex.Message, "SaruTask", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }
        private void ShowUpdate()
        {
            using (var dialog = new UpdateForm())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    UpdateService.StartUpdate(dialog.CandidatePath, Application.ExecutablePath);
                    exiting = true; Close();
                }
                catch (Exception ex) { MessageBox.Show(this, "更新を開始できませんでした。\n" + ex.Message, "SaruTask の更新", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        private void ShowVersion()
        {
            MessageBox.Show(this, "SaruTask\nバージョン " + UpdateService.VersionText + "\n\n指定フォルダのタスク検索・常駐ランチャー\nCtrl + Alt + Tで呼び出し\n\n" + UpdateService.ReleasesUrl, "SaruTask のバージョン", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void Notify(string message) { tray.BalloonTipTitle = "SaruTask"; tray.BalloonTipText = message; tray.ShowBalloonTip(8000); }

        internal static Icon CreateIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var blue = new SolidBrush(Color.FromArgb(33, 99, 195)))
            using (var pen = new Pen(Color.White, 3))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.FillEllipse(blue, 1, 1, 30, 30); graphics.DrawEllipse(pen, 8, 7, 12, 12); graphics.DrawLine(pen, 19, 19, 25, 25);
                IntPtr handle = bitmap.GetHicon(); try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { NativeMethods.DestroyIcon(handle); }
            }
        }
        private static Bitmap DrawEntryIcon(bool folder)
        {
            var bitmap = new Bitmap(20, 20);
            using (var g = Graphics.FromImage(bitmap))
            {
                if (folder) { using (var brush = new SolidBrush(Color.FromArgb(226, 169, 54))) { g.FillRectangle(brush, 2, 4, 7, 3); g.FillRectangle(brush, 2, 6, 16, 11); } }
                else { using (var brush = new SolidBrush(Color.FromArgb(71, 119, 181))) g.FillRectangle(brush, 4, 2, 12, 16); using (var font = new Font("Segoe UI", 8, FontStyle.Bold)) g.DrawString("M", font, Brushes.White, 4, 4); }
            }
            return bitmap;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                exiting = true; instanceWait.Unregister(null); debounce.Dispose(); poll.Dispose();
                if (scanCancellation != null) scanCancellation.Cancel();
                foreach (var watcher in watchers) watcher.Dispose();
                tray.Visible = false; tray.Dispose(); tips.Dispose(); results.SmallImageList.Dispose(); appIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

}
