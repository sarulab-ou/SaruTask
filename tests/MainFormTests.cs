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
    private static void PressFormKey(Form form, Keys key) { typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { new KeyEventArgs(key) }); }
    private static void PressEditorKey(DataGridView grid, Keys key) { grid.GetType().GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(grid, new object[] { key }); }
    private static bool HasAccessibleValue(AccessibleObject node, string value, int depth)
    {
        if (node.Value == value) return true;
        if (depth == 0) return false;
        int count = Math.Min(200, node.GetChildCount());
        for (int i = 0; i < count; i++)
        {
            var child = node.GetChild(i);
            if (child != null && HasAccessibleValue(child, value, depth - 1)) return true;
        }
        return false;
    }
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
                var list = Field<DataGridView>(form, "results"); var search = Field<TextBox>(form, "search"); var recursive = Field<CheckBox>(form, "recursive");
                Until(() => list.Rows.Count == 2 && !Field<bool>(form, "scanning"));
                Check(!recursive.Checked && !form.Visible, "background startup is shallow and hidden");
                Check(HasAccessibleValue(list.AccessibilityObject, "タスク.md", 3), "screen readers can read result values");
                Check(form.MainMenuStrip != null && System.Linq.Enumerable.Any(System.Linq.Enumerable.Cast<ToolStripMenuItem>(form.MainMenuStrip.Items), x => x.Text == "ヘルプ(&H)"), "Help menu exposes Alt+H access key");
                var help = System.Linq.Enumerable.Single(System.Linq.Enumerable.Cast<ToolStripMenuItem>(form.MainMenuStrip.Items), x => x.Text == "ヘルプ(&H)");
                Check(help.DropDownItems.Count == 2 && help.DropDownItems[0].Text == "更新(&U)…" && help.DropDownItems[1].Text == "バージョン(&A)…", "Help menu contains update and version actions");
                File.WriteAllText(Path.Combine(temp, "追加.md"), "");
                Until(() => list.Rows.Count == 3);
                Check(list.Rows.Count == 3, "filesystem watcher automatically picks up a new Markdown");
                File.Move(Path.Combine(temp, "追加.md"), Path.Combine(temp, "変更.md"));
                Until(() => System.Linq.Enumerable.Any(System.Linq.Enumerable.Cast<DataGridViewRow>(list.Rows), x => Convert.ToString(x.Cells[0].Value) == "変更.md"));
                Check(list.Rows.Count == 3, "filesystem watcher reflects rename without duplicates");
                recursive.Checked = true;
                Until(() => list.Rows.Count == 4 && !Field<bool>(form, "scanning"));
                Check(list.Rows.Count == 4, "recursive toggle updates actual UI results");
                search.Text = "nested";
                Check(list.Rows.Count == 1 && Convert.ToString(list.Rows[0].Cells[0].Value) == "nested.md", "typing filters UI results immediately");
                Check(HasAccessibleValue(list.AccessibilityObject, "nested.md", 3) && !HasAccessibleValue(list.AccessibilityObject, "タスク.md", 3), "screen reader values follow filtering without stale results");
                search.Text = "zz-no-match";
                Check(list.Rows.Count == 0, "no-match state clears previous results");
                PressFormKey(form, Keys.F2);
                Check(!list.IsCurrentCellInEditMode, "F2 with no selected result does nothing");
                search.Text = "タスク"; form.BringToFrontForSearch();
                PressFormKey(form, Keys.F2);
                var editor = list.EditingControl as TextBox;
                Check(editor != null && editor.SelectedText == "タスク", "F2 edits the selected name with the file extension excluded from selection");
                editor.Text = "タスク変更.md";
                File.WriteAllText(Path.Combine(temp, "編集中の追加.md"), "");
                var editingWatch = Stopwatch.StartNew(); Until(() => editingWatch.ElapsedMilliseconds > 550);
                Check(list.IsCurrentCellInEditMode && ReferenceEquals(editor, list.EditingControl) && editor.Text == "タスク変更.md", "watcher refresh cannot interrupt an active rename");
                PressEditorKey(list, Keys.Enter);
                Until(() => !Field<bool>(form, "scanning") && list.Rows.Count == 1 && Convert.ToString(list.Rows[0].Cells[0].Value) == "タスク変更.md");
                Check(File.Exists(Path.Combine(temp, "タスク変更.md")) && !File.Exists(Path.Combine(temp, "タスク.md")), "Enter commits the rename on disk and refreshes the list");
                Check(((SearchEntry)list.CurrentRow.Tag).FullPath == Path.Combine(temp, "タスク変更.md"), "renamed item remains selected after refresh");
                PressFormKey(form, Keys.F2); ((TextBox)list.EditingControl).Text = "取り消し.md";
                PressFormKey(form, Keys.Escape);
                Check(!list.IsCurrentCellInEditMode && form.Visible && File.Exists(Path.Combine(temp, "タスク変更.md")) && !File.Exists(Path.Combine(temp, "取り消し.md")), "Escape cancels rename without hiding the window or changing the file");
                Until(() => !Field<bool>(form, "scanning"));
                search.Text = "子フォルダ"; PressFormKey(form, Keys.F2);
                Check(((TextBox)list.EditingControl).SelectedText == "子フォルダ", "F2 selects the entire folder name");
                ((TextBox)list.EditingControl).Text = "子フォルダ変更"; PressEditorKey(list, Keys.Enter);
                Until(() => !Field<bool>(form, "scanning") && Convert.ToString(list.Rows[0].Cells[0].Value) == "子フォルダ変更");
                Check(File.Exists(Path.Combine(temp, "子フォルダ変更", "nested.md")), "folder rename preserves child files");
                Check(System.Linq.Enumerable.Any(Field<System.Collections.Generic.List<SearchEntry>>(form, "catalog"), x => x.Name == "nested.md" && x.Parent == Path.Combine(temp, "子フォルダ変更")), "recursive results refresh child paths after a folder rename");
                search.Text = "タスク変更"; PressFormKey(form, Keys.F2);
                ((TextBox)list.EditingControl).Text = "別名.md"; PressEditorKey(list, Keys.Enter);
                Until(() => !Field<bool>(form, "scanning") && list.Rows.Count == 0);
                Check(File.Exists(Path.Combine(temp, "別名.md")) && search.Text == "タスク変更", "rename keeps the search query when the new name no longer matches");
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
