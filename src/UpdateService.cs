using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SaruTask
{
    internal static class UpdateService
    {
        internal const string ReleasesUrl = "https://github.com/sarulab-ou/SaruTask/releases";
        internal static Version CurrentVersion { get { return typeof(UpdateService).Assembly.GetName().Version; } }
        internal static string VersionText { get { return CurrentVersion.ToString(3); } }
        internal const int MaximumPackageSize = 32 * 1024 * 1024;

        [DataContract]
        internal sealed class Release
        {
            [DataMember(Name = "tag_name")] public string Tag { get; set; }
            [DataMember(Name = "draft")] public bool Draft { get; set; }
            [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
            [DataMember(Name = "assets")] public List<Asset> Assets { get; set; }
            internal Version Version;
            internal Asset Package;
        }

        [DataContract]
        internal sealed class Asset
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "browser_download_url")] public string Url { get; set; }
            [DataMember(Name = "size")] public long Size { get; set; }
            [DataMember(Name = "digest")] public string Digest { get; set; }
        }

        internal static Release ParseRelease(string json)
        {
            Release release;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json))) release = (Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(stream);
            Version version;
            if (release == null || release.Draft || release.Prerelease || string.IsNullOrEmpty(release.Tag) || !Version.TryParse(release.Tag.TrimStart('v', 'V'), out version) || version.Build < 0) throw new InvalidDataException("正式リリースのバージョンを確認できません。");
            release.Version = new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
            string name = "SaruTask-" + version.ToString(3) + "-win.zip";
            release.Package = (release.Assets ?? new List<Asset>()).SingleOrDefault(x => x.Name == name);
            if (release.Package != null) ValidateAsset(release.Package);
            return release;
        }

        private static void ValidateAsset(Asset asset)
        {
            if (asset.Size <= 0 || asset.Size > MaximumPackageSize || string.IsNullOrEmpty(asset.Url) || !asset.Url.StartsWith(ReleasesUrl + "/download/", StringComparison.Ordinal)) throw new InvalidDataException("更新ZIPの配布情報が不正です。");
        }

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45), MaxResponseContentBufferSize = MaximumPackageSize };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SaruTask/" + VersionText);
            return client;
        }

        internal static async Task<Release> GetLatest(CancellationToken cancellation)
        {
            using (var client = CreateClient())
            using (var response = await client.GetAsync("https://api.github.com/repos/sarulab-ou/SaruTask/releases/latest", cancellation))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("公開済みの正式リリースがまだありません。");
                response.EnsureSuccessStatusCode();
                return ParseRelease(await response.Content.ReadAsStringAsync());
            }
        }

        internal static async Task<string> Download(Release release, CancellationToken cancellation, HttpClient transport = null, string stageDirectory = null)
        {
            if (release.Package == null) throw new InvalidDataException("このリリースには更新用ZIPがありません。");
            ValidateAsset(release.Package);
            string directory = stageDirectory ?? CreateStageDirectory();
            Directory.CreateDirectory(directory);
            string zip = Path.Combine(directory, release.Package.Name);
            using (var client = transport ?? CreateClient())
            {
                string expected = release.Package.Digest;
                if (!string.IsNullOrEmpty(expected) && expected.StartsWith("sha256:", StringComparison.Ordinal)) expected = expected.Substring(7);
                else
                {
                    var checksum = (release.Assets ?? new List<Asset>()).SingleOrDefault(x => x.Name == release.Package.Name + ".sha256");
                    if (checksum == null) throw new InvalidDataException("更新ZIPのSHA-256情報がありません。");
                    ValidateAsset(checksum);
                    using (var checksumResponse = await client.GetAsync(checksum.Url, cancellation))
                    {
                        checksumResponse.EnsureSuccessStatusCode();
                        expected = (await checksumResponse.Content.ReadAsStringAsync()).Split((char[])null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    }
                }
                if (expected == null || expected.Length != 64 || expected.Any(x => !Uri.IsHexDigit(x))) throw new InvalidDataException("SHA-256情報が不正です。");
                using (var response = await client.GetAsync(release.Package.Url, cancellation))
                {
                    response.EnsureSuccessStatusCode();
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    cancellation.ThrowIfCancellationRequested();
                    if (bytes.LongLength != release.Package.Size) throw new InvalidDataException("ZIPのサイズが配布情報と一致しません。");
                    File.WriteAllBytes(zip, bytes);
                }
                if (!string.Equals(Hash(zip), expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ZIPのSHA-256が配布情報と一致しません。");
            }
            return ExtractPackage(zip, directory, CurrentVersion, release.Version);
        }

        internal static string CreateStageDirectory()
        {
            string directory = Path.Combine(AppSettings.DataDirectory, "updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); return directory;
        }

        internal static string ExtractPackage(string zip, string directory, Version installedVersion, Version expectedVersion)
        {
            if (new FileInfo(zip).Length > MaximumPackageSize) throw new InvalidDataException("ZIPが大きすぎます。");
            string candidate = Path.Combine(directory, "SaruTask.exe");
            using (var archive = ZipFile.OpenRead(zip))
            {
                var executables = archive.Entries.Where(x => string.Equals(x.FullName, "SaruTask.exe", StringComparison.OrdinalIgnoreCase)).ToList();
                if (executables.Count != 1 || executables[0].Length <= 0 || executables[0].Length > MaximumPackageSize) throw new InvalidDataException("ZIP直下にSaruTask.exeが1つ必要です。");
                // Extract only this exact entry to a fixed destination; never interpret archive paths.
                using (var input = executables[0].Open())
                using (var output = File.Create(candidate))
                {
                    var buffer = new byte[8192]; int read; long total = 0;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0) { total += read; if (total > MaximumPackageSize) throw new InvalidDataException("展開したEXEが大きすぎます。"); output.Write(buffer, 0, read); }
                }
            }
            var version = ValidateCandidate(candidate, installedVersion);
            if (expectedVersion != null && version != expectedVersion) throw new InvalidDataException("ZIP内のEXEとリリースのバージョンが一致しません。");
            return candidate;
        }

        internal static Version ValidateCandidate(string path, Version installedVersion)
        {
            var assembly = AssemblyName.GetAssemblyName(path);
            if (!string.Equals(assembly.Name, "SaruTask", StringComparison.Ordinal)) throw new InvalidDataException("SaruTaskの更新EXEを選択してください。");
            if (assembly.Version == null || assembly.Version <= installedVersion) throw new InvalidDataException("現在より新しいバージョンのEXEを選択してください。");
            return assembly.Version;
        }

        internal static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        internal static string QuoteArgument(string value)
        {
            // Paths used here are full file paths, never directories ending in a backslash.
            if (value.IndexOf('"') >= 0 || value.EndsWith("\\", StringComparison.Ordinal)) throw new ArgumentException("Invalid update argument");
            return "\"" + value + "\"";
        }

        internal static void StartUpdate(string candidate, string installed)
        {
            var version = ValidateCandidate(candidate, CurrentVersion);
            string directory = CreateStageDirectory();
            string staged = Path.Combine(directory, "SaruTask.exe");
            string helper = Path.Combine(directory, "SaruTask.UpdateHelper.exe");
            File.Copy(candidate, staged);
            ValidateCandidate(staged, CurrentVersion);
            string hash = Hash(staged);
            File.Copy(installed, helper);
            string arguments = "--apply-update " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + " " + QuoteArgument(staged) + " " + QuoteArgument(installed) + " " + hash + " " + version;
            Process.Start(new ProcessStartInfo(helper, arguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory });
        }

        internal static void ApplyAfterExit(int processId, string staged, string installed, string hash, Version expectedVersion)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    if (!process.HasExited)
                    {
                        if (!string.Equals(Path.GetFullPath(process.MainModule.FileName), Path.GetFullPath(installed), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("更新対象のプロセスが一致しません。");
                        if (!process.WaitForExit(60000)) throw new TimeoutException("SaruTaskの終了を確認できませんでした。");
                    }
                }
            }
            catch (ArgumentException) { /* The original process has already exited. */ }
            Install(staged, installed, hash, expectedVersion);
            try { Process.Start(new ProcessStartInfo(installed) { UseShellExecute = true }); }
            catch
            {
                // Restore the previous executable if Windows cannot restart the replacement.
                File.Copy(installed + ".previous", installed, true);
                throw;
            }
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SaruTask", true))
                    if (key != null && string.Equals(key.GetValue("InstallLocation") as string, Path.GetDirectoryName(installed), StringComparison.OrdinalIgnoreCase)) key.SetValue("DisplayVersion", expectedVersion.ToString(3));
            }
            catch (Exception ex) { Trace.WriteLine("Installer version: " + ex.Message); }
        }

        internal static void Install(string staged, string installed, string hash, Version expectedVersion)
        {
            if (!string.Equals(Hash(staged), hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新ファイルが変更されています。もう一度選択してください。");
            var current = AssemblyName.GetAssemblyName(installed);
            if (!string.Equals(current.Name, "SaruTask", StringComparison.Ordinal)) throw new InvalidDataException("更新対象がSaruTaskではありません。");
            var version = ValidateCandidate(staged, current.Version);
            if (version != expectedVersion) throw new InvalidDataException("更新ファイルのバージョンが一致しません。");
            string replacement = installed + ".update-" + Guid.NewGuid().ToString("N");
            try
            {
                // A sibling temporary file keeps the atomic replacement on the same volume.
                File.Copy(staged, replacement);
                if (!string.Equals(Hash(replacement), hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新ファイルの検証に失敗しました。");
                File.Replace(replacement, installed, installed + ".previous");
            }
            finally { if (File.Exists(replacement)) File.Delete(replacement); }
        }
    }
}
