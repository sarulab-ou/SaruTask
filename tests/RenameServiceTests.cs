using System;
using System.IO;
using System.Linq;
using SaruTask;

internal static class RenameServiceTests
{
    private static int assertions;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); assertions++; Console.WriteLine("PASS " + name); }
    private static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Check(rejected, name); }
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "SaruTask-rename-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "元の名前.md"); File.WriteAllText(source, "preserve contents");
            string renamed = RenameService.Rename(source, "変更後の名前.md");
            Check(!File.Exists(source) && File.ReadAllText(renamed) == "preserve contents", "Japanese file rename preserves contents in the same directory");
            Check(RenameService.Rename(renamed, Path.GetFileName(renamed)) == renamed, "unchanged name is a no-op");
            string occupied = Path.Combine(root, "existing.md"); File.WriteAllText(occupied, "do not overwrite");
            Reject(() => RenameService.Rename(renamed, "existing.md"), "existing file is never overwritten");
            Check(File.ReadAllText(occupied) == "do not overwrite" && File.ReadAllText(renamed) == "preserve contents", "collision preserves both files");
            foreach (string invalid in new[] { "", " ", ".", "..", "../escape.md", "other\\escape.md", "C:\\escape.md", "bad?.md", "trailing.", "trailing " })
                Reject(() => RenameService.Rename(renamed, invalid), "invalid filename rejected: " + invalid);
            Check(File.Exists(renamed), "validation errors preserve the source");
            string folder = Path.Combine(root, "子フォルダ"); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "nested.md"), "nested contents");
            Reject(() => RenameService.Rename(renamed, "子フォルダ"), "file cannot replace an existing directory");
            Reject(() => RenameService.Rename(folder, "existing.md"), "directory cannot replace an existing file");
            string newFolder = RenameService.Rename(folder, "新しい子フォルダ");
            Check(!Directory.Exists(folder) && File.ReadAllText(Path.Combine(newFolder, "nested.md")) == "nested contents", "folder rename preserves its children");
            string upper = RenameService.Rename(renamed, "CASE.md"); string lower = RenameService.Rename(upper, "case.md");
            Check(Directory.EnumerateFiles(root).Any(x => Path.GetFileName(x) == "case.md") && File.ReadAllText(lower) == "preserve contents", "case-only file rename is reflected on disk");
            string caseFolder = RenameService.Rename(newFolder, "Folder"); caseFolder = RenameService.Rename(caseFolder, "folder");
            Check(Directory.EnumerateDirectories(root).Any(x => Path.GetFileName(x) == "folder") && File.Exists(Path.Combine(caseFolder, "nested.md")), "case-only directory rename preserves its children");
            Reject(() => RenameService.Rename(Path.Combine(root, "missing.md"), "new.md"), "missing source does not create a destination");
            Check(!Directory.EnumerateFileSystemEntries(root).Any(x => Path.GetFileName(x).StartsWith(".sarutask-rename-", StringComparison.Ordinal)), "case-only renames leave no temporary entries");
            Console.WriteLine(assertions + " rename assertions passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
