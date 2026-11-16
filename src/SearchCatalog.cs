using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace SaruTask
{
    public sealed class SearchEntry
    {
        public string Name, FullPath, Parent, SearchName;
        public bool IsDirectory;
        public DateTime Modified;
    }

    public sealed class ScanResult
    {
        public readonly List<SearchEntry> Entries = new List<SearchEntry>();
        public readonly List<string> Warnings = new List<string>();
    }

    public static class SearchCatalog
    {
        public static string Normalize(string text) { return text.Normalize(NormalizationForm.FormKC).ToUpperInvariant(); }
        public static string[] Tokens(string query) { return Normalize(query).Split((char[])null, StringSplitOptions.RemoveEmptyEntries); }
        public static bool Matches(SearchEntry entry, string[] tokens) { return tokens.All(token => entry.SearchName.IndexOf(token, StringComparison.Ordinal) >= 0); }

        public static ScanResult Scan(IEnumerable<string> roots, bool recursive, CancellationToken cancellation)
        {
            var result = new ScanResult();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots)
            {
                cancellation.ThrowIfCancellationRequested();
                var pending = new Stack<string>();
                try { pending.Push(Path.GetFullPath(root)); }
                catch (Exception ex) { result.Warnings.Add(root + ": " + ex.Message); continue; }
                while (pending.Count > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string directory = pending.Pop();
                    if (!visited.Add(directory)) continue;
                    try
                    {
                        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                        {
                            cancellation.ThrowIfCancellationRequested();
                            try
                            {
                                var attributes = File.GetAttributes(path);
                                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                                // Junctions and symlinks are listed but never traversed.
                                if (isDirectory && recursive && (attributes & FileAttributes.ReparsePoint) == 0) pending.Push(path);
                                string extension = Path.GetExtension(path);
                                if (!isDirectory && !extension.Equals(".md", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase)) continue;
                                if (!entries.Add(path)) continue;
                                string name = Path.GetFileName(path);
                                result.Entries.Add(new SearchEntry { Name = name, SearchName = Normalize(name), FullPath = path,
                                    Parent = directory, IsDirectory = isDirectory, Modified = File.GetLastWriteTime(path) });
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex) { result.Warnings.Add(path + ": " + ex.Message); }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { result.Warnings.Add(directory + ": " + ex.Message); }
                }
            }
            return result;
        }
    }
}
