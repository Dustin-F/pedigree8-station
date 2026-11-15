using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Pedigree8.Core;
using Xunit;

namespace Pedigree8.Core.Tests
{
    public sealed class UpdaterTests : IDisposable
    {
        private const string ReleaseJson = @"{
            ""tag_name"": ""v1.2.0"", ""body"": ""Feed all fits on one screen"",
            ""assets"": [
              { ""name"": ""Pedigree8Station-app-1.2.0.zip"", ""browser_download_url"": ""https://example.test/app.zip"" },
              { ""name"": ""Pedigree8Station-app-1.2.0.zip.sha256"", ""browser_download_url"": ""https://example.test/app.zip.sha256"" },
              { ""name"": ""Pedigree8Station-Setup-1.2.0.zip"", ""browser_download_url"": ""https://example.test/setup.zip"" } ] }";

        private readonly string _dir = Path.Combine(Path.GetTempPath(), "p8-update-" + Guid.NewGuid().ToString("N"));

        public UpdaterTests() => Directory.CreateDirectory(_dir);

        public void Dispose() => Directory.Delete(_dir, recursive: true);

        [Fact]
        public void Release_is_read_from_github_json()
        {
            var release = Updater.ParseRelease(ReleaseJson)!;

            Assert.Equal(new Version(1, 2, 0), release.Version);
            Assert.Equal("https://example.test/app.zip", release.ZipUrl);
            Assert.Equal("https://example.test/app.zip.sha256", release.ShaUrl);
            Assert.Equal("Feed all fits on one screen", release.Notes);
            Assert.True(Updater.IsNewer(release, new Version(1, 1, 9, 0)));
            Assert.False(Updater.IsNewer(release, new Version(1, 2, 0, 0)));
        }

        [Fact]
        public void Release_without_checksum_is_ignored()
        {
            Assert.Null(Updater.ParseRelease(ReleaseJson.Replace("app.zip.sha256\"", "other\"").Replace("-1.2.0.zip.sha256", "-1.2.0.txt")));
        }

        [Fact]
        public async Task Download_is_verified_and_unpacked()
        {
            var zip = MakeZip(("Pedigree8Station.exe", "new exe"), ("Pedigree8.Core.dll", "new core"));
            var updater = new Updater(new HttpClient(new FakeHandler(zip, Sha(zip))));

            var appDir = await updater.DownloadAsync(Updater.ParseRelease(ReleaseJson)!, Path.Combine(_dir, "work"));

            Assert.Equal("new exe", File.ReadAllText(Path.Combine(appDir, "Pedigree8Station.exe")));
        }

        [Fact]
        public async Task Damaged_download_is_rejected()
        {
            var zip = MakeZip(("Pedigree8Station.exe", "new exe"));
            var updater = new Updater(new HttpClient(new FakeHandler(zip, new string('0', 64))));

            await Assert.ThrowsAsync<InvalidDataException>(() => updater.DownloadAsync(Updater.ParseRelease(ReleaseJson)!, Path.Combine(_dir, "work")));
        }

        [Fact]
        public void Apply_replaces_files_keeps_others_and_cleanup_removes_leftovers()
        {
            var app = Path.Combine(_dir, "app");
            var update = Path.Combine(_dir, "update");
            Write(app, "Pedigree8Station.exe", "old exe");
            Write(app, "seed\\station.db", "seed data");
            Write(update, "Pedigree8Station.exe", "new exe");
            Write(update, "zxing.dll", "new dll");

            Updater.Apply(update, app);

            Assert.Equal("new exe", File.ReadAllText(Path.Combine(app, "Pedigree8Station.exe")));
            Assert.Equal("new dll", File.ReadAllText(Path.Combine(app, "zxing.dll")));
            Assert.Equal("seed data", File.ReadAllText(Path.Combine(app, "seed", "station.db")));
            Assert.True(File.Exists(Path.Combine(app, "Pedigree8Station.exe.old")));

            Updater.CleanupOldFiles(app);
            Assert.Empty(Directory.GetFiles(app, "*.old", SearchOption.AllDirectories));
        }

        [Fact]
        public void Failed_apply_puts_the_old_version_back()
        {
            var app = Path.Combine(_dir, "app");
            var update = Path.Combine(_dir, "update");
            Write(app, "Pedigree8Station.exe", "old exe");
            Write(app, "locked.dll", "old dll");
            Write(update, "Pedigree8Station.exe", "new exe");
            Write(update, "locked.dll", "new dll");
            Directory.CreateDirectory(Path.Combine(app, "locked.dll.old"));

            Assert.ThrowsAny<Exception>(() => Updater.Apply(update, app));

            Assert.Equal("old exe", File.ReadAllText(Path.Combine(app, "Pedigree8Station.exe")));
            Assert.Equal("old dll", File.ReadAllText(Path.Combine(app, "locked.dll")));
        }

        private static void Write(string dir, string relative, string content)
        {
            var path = Path.Combine(dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        private static byte[] MakeZip(params (string Name, string Content)[] files)
        {
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, content) in files)
                {
                    using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                    writer.Write(content);
                }
            }

            return stream.ToArray();
        }

        private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant() + "  app.zip";

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly byte[] _zip;
            private readonly string _sha;

            public FakeHandler(byte[] zip, string sha)
            {
                _zip = zip;
                _sha = sha;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var content = request.RequestUri!.AbsolutePath.EndsWith(".sha256")
                    ? new StringContent(_sha)
                    : (HttpContent)new ByteArrayContent(_zip);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
        }
    }
}
