using System;
using System.IO;
using System.Linq;
using System.Threading;
using SaruTask;

internal static class SearchCatalogTests
{
    private static int assertions;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); assertions++; Console.WriteLine("PASS " + name); }
    private static int Main()
    {
        string temp = Path.Combine(Path.GetTempPath(), "SaruTask-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, "2026_博士審査", "深い階層"));
            File.WriteAllText(Path.Combine(temp, "2026_博士審査.md"), "本文専用キーワード");
            File.WriteAllText(Path.Combine(temp, "FULLWIDTH_ＡＢＣ.MD"), "");
            File.WriteAllText(Path.Combine(temp, "notes.markdown"), "");
            File.WriteAllText(Path.Combine(temp, "ignore.txt"), "");
            File.WriteAllText(Path.Combine(temp, "2026_博士審査", "nested.md"), "");
            File.WriteAllText(Path.Combine(temp, "2026_博士審査", "深い階層", "deep.md"), "");
            var shallow = SearchCatalog.Scan(new[] { temp }, false, CancellationToken.None);
            Check(shallow.Entries.Count == 4, "default scope contains only immediate folders and Markdown");
            Check(shallow.Warnings.Count == 0, "readable roots have no warnings");
            Check(shallow.Entries.Count(x => SearchCatalog.Matches(x, SearchCatalog.Tokens("博士"))) == 2, "Japanese substring matches both folder and Markdown");
            Check(shallow.Entries.Count(x => SearchCatalog.Matches(x, SearchCatalog.Tokens("2026 審査"))) == 2, "space-separated terms use AND");
            Check(shallow.Entries.Count(x => SearchCatalog.Matches(x, SearchCatalog.Tokens("abc"))) == 1, "case and full-width characters normalized");
            Check(shallow.Entries.All(x => SearchCatalog.Matches(x, SearchCatalog.Tokens("  \t"))), "empty query shows all entries");
            Check(!shallow.Entries.Any(x => SearchCatalog.Matches(x, SearchCatalog.Tokens("本文専用"))), "file contents are not searched");
            var recursive = SearchCatalog.Scan(new[] { temp, Path.Combine(temp, "2026_博士審査") }, true, CancellationToken.None);
            Check(recursive.Entries.Count == 7, "recursive scope and overlapping roots deduplicate entries");
            var missing = SearchCatalog.Scan(new[] { Path.Combine(temp, "missing"), temp }, false, CancellationToken.None);
            Check(missing.Warnings.Count == 1 && missing.Entries.Count == 4, "missing root reports a warning without losing other roots");
            File.Move(Path.Combine(temp, "notes.markdown"), Path.Combine(temp, "renamed.md"));
            var refreshed = SearchCatalog.Scan(new[] { temp }, false, CancellationToken.None);
            Check(refreshed.Entries.Any(x => x.Name == "renamed.md") && !refreshed.Entries.Any(x => x.Name == "notes.markdown"), "refresh reflects renames");
            bool cancelled = false;
            try { SearchCatalog.Scan(new[] { temp }, true, new CancellationToken(true)); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "scan cancellation is honored");
            Check(AppSettings.Defaults().Roots.Count == 2, "two requested default roots");
            Console.WriteLine(assertions + " assertions passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
