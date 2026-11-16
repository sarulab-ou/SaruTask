using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.CSharp;
using SaruTask;

internal static class UpdateTests
{
    private static int assertions;
    private sealed class PackageHandler : HttpMessageHandler
    {
        internal byte[] Bytes;
        internal string Checksum;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = request.RequestUri.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal) ? (HttpContent)new StringContent(Checksum) : new ByteArrayContent(Bytes);
            return Task.FromResult(response);
        }
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); assertions++; Console.WriteLine("PASS " + name); }
    private static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Check(rejected, name); }
    private static string Fixture(string root, string subdir, string version, bool pause)
    {
        string directory = Path.Combine(root, subdir); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "SaruTask.exe");
        string code = "using System; using System.Reflection; [assembly: AssemblyVersion(\"" + version + "\")] class Entry { static void Main() { " + (pause ? "System.Threading.Thread.Sleep(500);" : "System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, \"restarted.txt\"), \"" + version + "\");") + " } }";
        using (var provider = new CSharpCodeProvider())
        {
            var parameters = new CompilerParameters { GenerateExecutable = true, OutputAssembly = path, CompilerOptions = "/target:winexe /optimize" };
            var result = provider.CompileAssemblyFromSource(parameters, code);
            if (result.Errors.HasErrors) throw new Exception(string.Join("\n", result.Errors.Cast<CompilerError>().Select(x => x.ToString())));
        }
        return path;
    }
    private static string Zip(string root, string name, string fixture, bool duplicate, bool rootEntry)
    {
        string path = Path.Combine(root, name);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(fixture, rootEntry ? "SaruTask.exe" : "../SaruTask.exe");
            if (duplicate) archive.CreateEntryFromFile(fixture, "SARUTASK.EXE");
            using (var writer = new StreamWriter(archive.CreateEntry("../escape.txt").Open())) writer.Write("must not be extracted");
        }
        return path;
    }

    private static int Main()
    {
        string temp = Path.Combine(Path.GetTempPath(), "SaruTask-update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var oldVersion = new Version(1, 0, 0, 0); var newVersion = new Version(1, 1, 0, 0);
            string old = Fixture(temp, "old", "1.0.0.0", true); string newer = Fixture(temp, "new", "1.1.0.0", false);
            Check(UpdateService.ValidateCandidate(newer, oldVersion) == newVersion, "newer SaruTask assembly is accepted");
            Reject(() => UpdateService.ValidateCandidate(old, oldVersion), "same version is rejected");
            Reject(() => UpdateService.ValidateCandidate(old, newVersion), "downgrades are rejected");
            File.WriteAllText(Path.Combine(temp, "invalid.exe"), "not executable");
            Reject(() => UpdateService.ValidateCandidate(Path.Combine(temp, "invalid.exe"), oldVersion), "invalid executable is rejected");
            string goodZip = Zip(temp, "good.zip", newer, false, true);
            string stage = Path.Combine(temp, "stage"); Directory.CreateDirectory(stage);
            string extracted = UpdateService.ExtractPackage(goodZip, stage, oldVersion, newVersion);
            Check(UpdateService.Hash(extracted) == UpdateService.Hash(newer), "ZIP extraction preserves exact executable bytes");
            Check(!File.Exists(Path.Combine(temp, "escape.txt")), "archive paths outside the fixed EXE entry are never extracted");
            Reject(() => UpdateService.ExtractPackage(Zip(temp, "duplicate.zip", newer, true, true), stage, oldVersion, newVersion), "duplicate executable entries are rejected");
            Reject(() => UpdateService.ExtractPackage(Zip(temp, "traversal.zip", newer, false, false), stage, oldVersion, newVersion), "ZIP without a root executable is rejected");
            Reject(() => UpdateService.ExtractPackage(goodZip, stage, oldVersion, new Version(9, 0, 0, 0)), "release and EXE version mismatch is rejected");
            extracted = UpdateService.ExtractPackage(goodZip, stage, oldVersion, newVersion);
            string originalHash = UpdateService.Hash(old);
            Reject(() => UpdateService.Install(extracted, old, new string('0', 64), newVersion), "changed staged executable is rejected");
            Check(UpdateService.Hash(old) == originalHash, "failed verification preserves installed executable");
            UpdateService.Install(extracted, old, UpdateService.Hash(extracted), newVersion);
            Check(AssemblyName.GetAssemblyName(old).Version == newVersion, "atomic install replaces executable with new version");
            Check(UpdateService.Hash(old + ".previous") == originalHash, "previous executable is retained as backup");
            string json = "{\"tag_name\":\"v1.1.0\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"SaruTask-1.1.0-win.zip\",\"size\":123,\"browser_download_url\":\"https://github.com/sarulab-ou/SaruTask/releases/download/v1.1.0/SaruTask-1.1.0-win.zip\"}]}";
            Check(UpdateService.ParseRelease(json).Package != null, "release metadata selects the exact versioned ZIP asset");
            Reject(() => UpdateService.ParseRelease(json.Replace("v1.1.0", "v1.1.0-beta")), "prerelease version strings are rejected");
            Reject(() => UpdateService.ParseRelease(json.Replace("github.com/sarulab-ou/SaruTask/releases/download/", "example.com/")), "foreign download URLs are rejected");
            Reject(() => UpdateService.ParseRelease(json.Replace("\"size\":123", "\"size\":999999999")), "oversized release assets are rejected");
            var release = UpdateService.ParseRelease(json); release.Package.Size = new FileInfo(goodZip).Length; release.Package.Digest = "sha256:" + UpdateService.Hash(goodZip);
            string fetched = UpdateService.Download(release, CancellationToken.None, new HttpClient(new PackageHandler { Bytes = File.ReadAllBytes(goodZip) }), Path.Combine(temp, "download-digest")).GetAwaiter().GetResult();
            Check(UpdateService.Hash(fetched) == UpdateService.Hash(newer), "download verifies published SHA-256 and extracts the expected version");
            release.Package.Digest = null;
            release.Assets.Add(new UpdateService.Asset { Name = release.Package.Name + ".sha256", Size = 100, Url = release.Package.Url + ".sha256" });
            fetched = UpdateService.Download(release, CancellationToken.None, new HttpClient(new PackageHandler { Bytes = File.ReadAllBytes(goodZip), Checksum = UpdateService.Hash(goodZip) + "  package.zip" }), Path.Combine(temp, "download-checksum")).GetAwaiter().GetResult();
            Check(UpdateService.Hash(fetched) == UpdateService.Hash(newer), "checksum asset fallback validates release ZIP");
            release.Package.Digest = "sha256:" + new string('0', 64);
            Reject(() => UpdateService.Download(release, CancellationToken.None, new HttpClient(new PackageHandler { Bytes = File.ReadAllBytes(goodZip) }), Path.Combine(temp, "download-bad-hash")).GetAwaiter().GetResult(), "download rejects a package with a mismatched digest");
            release.Package.Size++;
            Reject(() => UpdateService.Download(release, CancellationToken.None, new HttpClient(new PackageHandler { Bytes = File.ReadAllBytes(goodZip) }), Path.Combine(temp, "download-bad-size")).GetAwaiter().GetResult(), "download rejects a package with a mismatched size");
            Check(UpdateService.QuoteArgument(@"C:\path with spaces\日本語.exe") == "\"C:\\path with spaces\\日本語.exe\"", "helper arguments preserve spaces and Japanese text");

            string target = Fixture(temp, "restart target 日本語", "1.0.0.0", true);
            using (var running = Process.Start(new ProcessStartInfo(target) { UseShellExecute = false, CreateNoWindow = true }))
            {
                string app = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SaruTask.exe");
                string arguments = "--apply-update " + running.Id + " " + UpdateService.QuoteArgument(extracted) + " " + UpdateService.QuoteArgument(target) + " " + UpdateService.Hash(extracted) + " " + newVersion;
                using (var helper = Process.Start(new ProcessStartInfo(app, arguments) { UseShellExecute = false, CreateNoWindow = true }))
                {
                    if (!helper.WaitForExit(10000)) throw new Exception("Update helper did not finish");
                    Check(helper.ExitCode == 0 && AssemblyName.GetAssemblyName(target).Version == newVersion, "actual helper waits for old process then applies replacement");
                }
            }
            string marker = Path.Combine(Path.GetDirectoryName(target), "restarted.txt"); var wait = Stopwatch.StartNew();
            while (!File.Exists(marker) && wait.ElapsedMilliseconds < 5000) Thread.Sleep(30);
            Check(File.Exists(marker) && File.ReadAllText(marker) == "1.1.0.0", "actual helper restarts the updated executable");
            Console.WriteLine(assertions + " update assertions passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
