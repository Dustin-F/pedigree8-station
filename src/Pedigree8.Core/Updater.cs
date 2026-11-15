using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Pedigree8.Core
{
    public sealed class ReleaseInfo
    {
        public Version Version { get; set; } = new Version(0, 0, 0);
        public string Tag { get; set; } = "";
        public string Notes { get; set; } = "";
        public string ZipUrl { get; set; } = "";
        public string ShaUrl { get; set; } = "";
    }

    // Updates come from GitHub Releases: each release has Pedigree8Station-app-<version>.zip
    // plus a .sha256 file. Only the program folder is replaced; station data lives elsewhere.
    public sealed class Updater
    {
        public const string DefaultRepo = "Dustin-F/pedigree8-station";
        public const string AssetPrefix = "Pedigree8Station-app-";
        public const string ExeName = "Pedigree8Station.exe";
        private const string OldSuffix = ".old";
        private readonly HttpClient _http;

        public Updater(HttpClient? http = null)
        {
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            if (!_http.DefaultRequestHeaders.UserAgent.Any())
            {
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("Pedigree8Station-Updater");
            }
        }

        public static Version Normalize(Version v) => new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

        public static bool IsNewer(ReleaseInfo release, Version current) => release.Version > Normalize(current);

        public async Task<ReleaseInfo?> GetLatestAsync(string repo = DefaultRepo)
        {
            using var response = await _http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest").ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return ParseRelease(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        public static ReleaseInfo? ParseRelease(string json)
        {
            GitHubRelease release;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                release = (GitHubRelease)new DataContractJsonSerializer(typeof(GitHubRelease)).ReadObject(stream)!;
            }

            if (!Version.TryParse((release.TagName ?? "").TrimStart('v', 'V'), out var version))
            {
                return null;
            }

            var assets = release.Assets ?? new List<GitHubAsset>();
            var zip = assets.FirstOrDefault(a => a.Name != null && a.Name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase)
                                                 && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            var sha = zip is null ? null : assets.FirstOrDefault(a => string.Equals(a.Name, zip.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
            if (zip?.Url is null || sha?.Url is null)
            {
                return null;
            }

            return new ReleaseInfo { Version = Normalize(version), Tag = release.TagName!, Notes = release.Body ?? "", ZipUrl = zip.Url, ShaUrl = sha.Url };
        }

        // Downloads, verifies the checksum and unpacks. Returns the folder holding the new program files.
        public async Task<string> DownloadAsync(ReleaseInfo release, string workDir)
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }

            Directory.CreateDirectory(workDir);
            var zipPath = Path.Combine(workDir, "app.zip");
            var bytes = await _http.GetByteArrayAsync(release.ZipUrl).ConfigureAwait(false);
            var expected = (await _http.GetStringAsync(release.ShaUrl).ConfigureAwait(false)).Trim().Split(' ', '\t')[0].ToLowerInvariant();
            string actual;
            using (var sha = SHA256.Create())
            {
                actual = string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
            }

            if (actual != expected)
            {
                throw new InvalidDataException("The downloaded update is damaged (checksum mismatch). Nothing was changed.");
            }

            File.WriteAllBytes(zipPath, bytes);
            var appDir = Path.Combine(workDir, "app");
            ZipFile.ExtractToDirectory(zipPath, appDir);
            if (!File.Exists(Path.Combine(appDir, ExeName)))
            {
                throw new InvalidDataException("The update does not contain the program. Nothing was changed.");
            }

            return appDir;
        }

        // Replaces program files. Running files can be renamed (not overwritten) on Windows, so each
        // existing file is renamed to *.old first; any failure puts everything back.
        public static void Apply(string newAppDir, string appDir)
        {
            var replaced = new List<string>();
            var added = new List<string>();
            try
            {
                foreach (var source in Directory.EnumerateFiles(newAppDir, "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(appDir, source.Substring(newAppDir.Length).TrimStart('\\', '/'));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (File.Exists(target))
                    {
                        var old = target + OldSuffix;
                        if (File.Exists(old))
                        {
                            File.Delete(old);
                        }

                        File.Move(target, old);
                        replaced.Add(target);
                    }
                    else
                    {
                        added.Add(target);
                    }

                    File.Copy(source, target);
                }
            }
            catch
            {
                foreach (var target in added.Where(File.Exists))
                {
                    File.Delete(target);
                }

                foreach (var target in replaced)
                {
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }

                    File.Move(target + OldSuffix, target);
                }

                throw;
            }
        }

        // Called at start-up: removes files left over from the previous version.
        public static void CleanupOldFiles(string appDir)
        {
            foreach (var old in Directory.EnumerateFiles(appDir, "*" + OldSuffix, SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(old);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        [DataContract]
        private sealed class GitHubRelease
        {
            [DataMember(Name = "tag_name")]
            public string? TagName { get; set; }

            [DataMember(Name = "body")]
            public string? Body { get; set; }

            [DataMember(Name = "assets")]
            public List<GitHubAsset>? Assets { get; set; }
        }

        [DataContract]
        private sealed class GitHubAsset
        {
            [DataMember(Name = "name")]
            public string? Name { get; set; }

            [DataMember(Name = "browser_download_url")]
            public string? Url { get; set; }
        }
    }
}
