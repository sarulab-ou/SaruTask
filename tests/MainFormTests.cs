using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using SaruTask;

internal static class MainFormTests
{
    private static int assertions;
    private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); assertions++; Console.WriteLine("PASS " + name); }
    private static void Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.ElapsedMilliseconds > 8000) throw new Exception("Timed out waiting for UI update"); Application.DoEvents(); Thread.Sleep(20); }
        Application.DoEvents();
    }

    [STAThread]
    private static int Main()
    {
        string temp = Path.Combine(Path.GetTempPath(), "SaruTask-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            Application.EnableVisualStyles();
            Directory.CreateDirectory(Path.Combine(temp, "子フォルダ"));
            File.WriteAllText(Path.Combine(temp, "タスク.md"), "");
            File.WriteAllText(Path.Combine(temp, "子フォルダ", "nested.md"), "");
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (var form = new MainForm(new AppSettings { Roots = new System.Collections.Generic.List<string> { temp } }, true, signal, null))
            {
                form.Show();
                var list = Field<ListView>(form, "results"); var search = Field<TextBox>(form, "search"); var recursive = Field<CheckBox>(form, "recursive");
                Until(() => list.Items.Count == 2 && !Field<bool>(form, "scanning"));
                Check(!recursive.Checked && !form.Visible, "background startup is shallow and hidden");
                Check(form.MainMenuStrip != null && form.MainMenuStrip.Items[0].Text == "ヘルプ(&H)", "Help menu exposes Alt+H access key");
                var help = (ToolStripMenuItem)form.MainMenuStrip.Items[0];
                Check(help.DropDownItems.Count == 2 && help.DropDownItems[0].Text == "更新(&U)…" && help.DropDownItems[1].Text == "バージョン(&A)…", "Help menu contains update and version actions");
                File.WriteAllText(Path.Combine(temp, "追加.md"), "");
                Until(() => list.Items.Count == 3);
                Check(list.Items.Count == 3, "filesystem watcher automatically picks up a new Markdown");
                File.Move(Path.Combine(temp, "追加.md"), Path.Combine(temp, "変更.md"));
                Until(() => System.Linq.Enumerable.Any(System.Linq.Enumerable.Cast<ListViewItem>(list.Items), x => x.Text == "変更.md"));
                Check(list.Items.Count == 3, "filesystem watcher reflects rename without duplicates");
                recursive.Checked = true;
                Until(() => list.Items.Count == 4 && !Field<bool>(form, "scanning"));
                Check(list.Items.Count == 4, "recursive toggle updates actual UI results");
                search.Text = "nested";
                Check(list.Items.Count == 1 && list.Items[0].Text == "nested.md", "typing filters UI results immediately");
                search.Text = "zz-no-match";
                Check(list.Items.Count == 0, "no-match state clears previous results");
                search.Text = "タスク"; form.BringToFrontForSearch();
                Check(form.Visible && search.SelectionLength == search.TextLength, "recall shows window and selects search text");
                form.Close();
                Check(!form.Visible && !form.IsDisposed, "close hides resident window without disposing");
                signal.Set(); Until(() => form.Visible);
                Check(form.Visible, "second-instance event recalls resident window");
                // Dispose explicitly: Close intentionally only hides the resident window.
            }
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (var empty = new MainForm(new AppSettings { Roots = new System.Collections.Generic.List<string> { Path.Combine(temp, "missing") } }, true, signal, null))
            {
                empty.Show(); Until(() => Field<bool>(empty, "hasScanned"));
                Check(Field<Label>(empty, "empty").Text.Contains("ありません"), "empty initial scan leaves loading state");
                Check(Field<Label>(empty, "summary").Text.Contains("読み取りエラー"), "missing root is visible in status");
            }
            Console.WriteLine(assertions + " UI assertions passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
