using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SaruTask
{
    internal sealed class SettingsForm : Form
    {
        private readonly ListBox roots = new ListBox();
        private readonly CheckBox startup = new CheckBox();
        public List<string> Roots { get { return roots.Items.Cast<string>().ToList(); } }
        public bool StartWithWindows { get { return startup.Checked; } }
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public SettingsForm(AppSettings settings)
        {
            Text = "SaruTask の設定"; Font = new Font("Yu Gothic UI", 10); ClientSize = new Size(760, 380);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false; FormBorderStyle = FormBorderStyle.FixedDialog;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), RowCount = 6, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.Controls.Add(new Label { Text = "検索対象フォルダ", AutoSize = true }, 0, 0);
            roots.Dock = DockStyle.Fill; roots.HorizontalScrollbar = true; roots.Items.AddRange(settings.Roots.Cast<object>().ToArray()); layout.Controls.Add(roots, 0, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
            var add = new Button { Text = "追加…", Width = 100, Height = 30 }; var remove = new Button { Text = "削除", Width = 100, Height = 30 };
            add.Click += delegate {
                using (var picker = new FolderBrowserDialog { Description = "検索対象にするフォルダを選択", ShowNewFolderButton = false })
                    if (picker.ShowDialog(this) == DialogResult.OK && !Roots.Contains(picker.SelectedPath, StringComparer.OrdinalIgnoreCase)) roots.Items.Add(picker.SelectedPath);
            };
            remove.Click += delegate { if (roots.SelectedIndex >= 0) roots.Items.RemoveAt(roots.SelectedIndex); };
            actions.Controls.Add(add); actions.Controls.Add(remove); layout.Controls.Add(actions, 0, 2);
            startup.Text = "Windows サインイン時にトレイで起動する"; startup.AutoSize = true;
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) startup.Checked = key != null && key.GetValue("SaruTask") != null;
            layout.Controls.Add(startup, 0, 3);
            layout.Controls.Add(new Label { Text = "初期状態では直下のみ検索します。呼び出し: Ctrl + Alt + T", AutoSize = true, ForeColor = Color.DimGray }, 0, 4);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var save = new Button { Text = "保存", Width = 100, Height = 32 }; var cancel = new Button { Text = "キャンセル", Width = 110, Height = 32, DialogResult = DialogResult.Cancel };
            save.Click += delegate { if (Roots.Count == 0) { MessageBox.Show(this, "検索対象を1つ以上追加してください。"); return; } DialogResult = DialogResult.OK; };
            footer.Controls.Add(save); footer.Controls.Add(cancel); layout.Controls.Add(footer, 0, 5); Controls.Add(layout);
            AcceptButton = save; CancelButton = cancel;
        }

        public static void SetStartup(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue("SaruTask", "\"" + Application.ExecutablePath + "\" --background");
                else key.DeleteValue("SaruTask", false);
            }
        }
    }
}
