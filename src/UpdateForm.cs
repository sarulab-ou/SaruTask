using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SaruTask
{
    internal sealed class UpdateForm : Form
    {
        internal string CandidatePath { get; private set; }
        private readonly Label status = new Label();
        private readonly Button check = new Button();
        private readonly Button install = new Button();
        private readonly Button local = new Button();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private UpdateService.Release latest;

        internal UpdateForm()
        {
            Text = "SaruTask の更新"; Font = new Font("Yu Gothic UI", 10);
            ClientSize = new Size(600, 310); StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), RowCount = 5, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.Controls.Add(new Label { Text = "現在のバージョン: " + UpdateService.VersionText, AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            status.Text = "GitHub Releasesの正式版を確認します。\n更新にはZIP版を使用し、設定を引き継ぎます。"; status.Dock = DockStyle.Fill; layout.Controls.Add(status, 0, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            check.Text = "最新版を確認"; check.Width = 150; check.Height = 34; check.Click += async delegate { await CheckLatest(); };
            install.Text = "更新して再起動"; install.Width = 180; install.Height = 34; install.Enabled = false; install.Click += async delegate { await InstallLatest(); };
            local.Text = "ZIPから更新…"; local.Width = 160; local.Height = 34; local.Click += async delegate { await SelectPackage(); };
            actions.Controls.Add(check); actions.Controls.Add(install); actions.Controls.Add(local); layout.Controls.Add(actions, 0, 2);
            var releases = new LinkLabel { Text = "配布ページを開く", AutoSize = true };
            releases.LinkClicked += delegate { try { Process.Start(new ProcessStartInfo(UpdateService.ReleasesUrl) { UseShellExecute = true }); } catch (Exception ex) { status.Text = ex.Message; } };
            layout.Controls.Add(releases, 0, 3);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
            var close = new Button { Text = "閉じる", Width = 100, Height = 32, DialogResult = DialogResult.Cancel };
            footer.Controls.Add(close); layout.Controls.Add(footer, 0, 4); Controls.Add(layout); CancelButton = close;
            Shown += async delegate { await CheckLatest(); };
            FormClosed += delegate { cancellation.Cancel(); };
        }

        private void Busy(bool value)
        {
            check.Enabled = local.Enabled = !value;
            install.Enabled = !value && latest != null && latest.Version > UpdateService.CurrentVersion && latest.Package != null;
        }

        private async Task CheckLatest()
        {
            Busy(true); status.Text = "最新版を確認しています…";
            try
            {
                latest = await UpdateService.GetLatest(cancellation.Token);
                if (IsDisposed) return;
                status.Text = latest.Version > UpdateService.CurrentVersion ? "新しいバージョン " + latest.Version.ToString(3) + " があります。\n" + (latest.Package == null ? "更新用ZIPがまだ公開されていません。" : "「更新して再起動」でZIPを取得して適用します。") : "最新バージョンを使用しています。";
            }
            catch (Exception ex) { if (!IsDisposed && !cancellation.IsCancellationRequested) { latest = null; status.Text = "更新を確認できませんでした。\n" + ex.Message; } }
            finally { if (!IsDisposed) Busy(false); }
        }

        private bool Confirm(Version version)
        {
            return MessageBox.Show(this, "SaruTask " + UpdateService.VersionText + " → " + version.ToString(3) + "\n\nZIP版を適用して再起動しますか？\n検索対象の設定は引き継がれます。", "SaruTask の更新", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private async Task InstallLatest()
        {
            if (latest == null || !Confirm(latest.Version)) return;
            Busy(true); status.Text = "更新用ZIPをダウンロード・検証しています…";
            try { string candidate = await UpdateService.Download(latest, cancellation.Token); if (!IsDisposed) { CandidatePath = candidate; DialogResult = DialogResult.OK; } }
            catch (Exception ex) { if (!IsDisposed && !cancellation.IsCancellationRequested) status.Text = "更新できませんでした。\n" + ex.Message; }
            finally { if (!IsDisposed) Busy(false); }
        }

        private async Task SelectPackage()
        {
            using (var picker = new OpenFileDialog { Title = "ダウンロードしたSaruTaskのZIP版を選択", Filter = "SaruTaskのZIP版 (*.zip)|*.zip", CheckFileExists = true, Multiselect = false })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                Busy(true); status.Text = "ZIPの内容を検証しています…";
                try
                {
                    string path = picker.FileName;
                    string candidate = await Task.Run(() => UpdateService.ExtractPackage(path, UpdateService.CreateStageDirectory(), UpdateService.CurrentVersion, null));
                    if (IsDisposed) return;
                    var version = UpdateService.ValidateCandidate(candidate, UpdateService.CurrentVersion);
                    if (Confirm(version)) { CandidatePath = candidate; DialogResult = DialogResult.OK; }
                    else status.Text = "更新をキャンセルしました。";
                }
                catch (Exception ex) { if (!IsDisposed) status.Text = "ZIPを使用できません。\n" + ex.Message; }
                finally { if (!IsDisposed) Busy(false); }
            }
        }
    }
}
