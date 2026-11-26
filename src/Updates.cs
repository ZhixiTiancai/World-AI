using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace WorldAI
{
    public sealed class UpdateJob
    {
        public int ParentId { get; set; }
        public long ParentStart { get; set; }
        public string Target { get; set; }
        public string Digest { get; set; }
    }

    internal static class Updates
    {
        internal const string Repository = "ZhixiTiancai/World-AI";
        internal const string WindowsAsset = "WorldAI-windows-x64.zip";
        internal const string SetupAsset = "WorldAI-Setup.exe";
        private const long MaxBytes = 64 * 1024 * 1024;
        private static readonly string Staging = Path.Combine(Store.Folder, "updates");
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        internal static readonly Version Current = ReadVersion();

        internal sealed class Release
        {
            internal Version Version;
            internal string Notes;
            internal Uri Download;
            internal string Digest;
        }

        private static Version ReadVersion()
        {
            using (var stream = typeof(Updates).Assembly.GetManifestResourceStream("WorldAI.Version"))
            using (var reader = new StreamReader(stream)) return new Version(reader.ReadToEnd().Trim());
        }

        internal static async Task<Release> Latest(CancellationToken token)
        {
            const string api = "https://api.github.com/repos/" + Repository + "/releases/latest";
            using (var request = new HttpRequestMessage(HttpMethod.Get, api))
            {
                request.Headers.UserAgent.ParseAdd("WorldAI/" + Current);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using (var response = await Client.SendAsync(request, token))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound) return null;
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("GitHub 返回 HTTP " + (int)response.StatusCode + "。");
                    var data = Store.Json().DeserializeObject(await response.Content.ReadAsStringAsync());
                    string tag = Cards.Get(data, "tag_name") as string ?? "";
                    Version version;
                    if (!Version.TryParse(tag.TrimStart('v', 'V'), out version) || version.Build < 0)
                        throw new InvalidDataException("发行版缺少有效的版本号。");
                    var release = new Release { Version = version, Notes = Cards.Get(data, "body") as string };
                    if (version <= Current) return release;
                    var assets = Cards.Get(data, "assets") as object[] ?? new object[0];
                    var asset = assets.FirstOrDefault(item => Equals(Cards.Get(item, "name"), SetupAsset))
                        ?? assets.FirstOrDefault(item => Equals(Cards.Get(item, "name"), WindowsAsset));
                    string name = Cards.Get(asset, "name") as string;
                    string digest = Cards.Get(asset, "digest") as string ?? "";
                    Uri url;
                    if (!ValidDigest(digest) || !Uri.TryCreate(Cards.Get(asset, "browser_download_url") as string,
                        UriKind.Absolute, out url) || url.Scheme != "https" || url.Host != "github.com" ||
                        !String.Equals(url.AbsolutePath, "/" + Repository + "/releases/download/" + tag + "/" + name,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("新版本未附带可校验的 Windows 更新包。");
                    release.Download = url;
                    release.Digest = digest;
                    return release;
                }
            }
        }

        internal static async Task<string> Prepare(Release release, CancellationToken token)
        {
            string id = Guid.NewGuid().ToString("N");
            string directory = Path.Combine(Staging, id);
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "package.zip");
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(2));
                    using (var response = await Client.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                    using (timeout.Token.Register(response.Dispose))
                    {
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidDataException("更新包太大。");
                        using (var input = await response.Content.ReadAsStreamAsync())
                        using (var output = File.Create(file))
                        {
                            var buffer = new byte[65536];
                            int count;
                            long total = 0;
                            while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token)) > 0)
                            {
                                total += count;
                                if (total > MaxBytes) throw new InvalidDataException("更新包太大。");
                                await output.WriteAsync(buffer, 0, count, timeout.Token);
                            }
                        }
                    }
                }
                token.ThrowIfCancellationRequested();
                Verify(file, release.Digest);
                using (var process = Process.GetCurrentProcess())
                {
                    var job = new UpdateJob { ParentId = process.Id, ParentStart = process.StartTime.ToUniversalTime().Ticks,
                        Target = Assembly.GetExecutingAssembly().Location, Digest = release.Digest };
                    Store.Write(Path.Combine(directory, "job.json"), Store.Json().Serialize(job), false);
                    File.Copy(job.Target, Path.Combine(directory, "update-helper.exe"));
                    if (File.Exists(job.Target + ".config"))
                        File.Copy(job.Target + ".config", Path.Combine(directory, "update-helper.exe.config"));
                }
                return id;
            }
            catch
            {
                try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }

        internal static void StartInstall(string id)
        {
            Process.Start(new ProcessStartInfo {
                FileName = Path.Combine(Staging, id, "update-helper.exe"), Arguments = "--apply-update " + id,
                WorkingDirectory = Path.Combine(Staging, id), UseShellExecute = false
            });
        }

        internal static void Install(string id)
        {
            Guid parsed;
            if (!Guid.TryParseExact(id, "N", out parsed)) throw new InvalidDataException("无效的更新任务。");
            string stage = Path.Combine(Staging, parsed.ToString("N"));
            var job = Store.Json().Deserialize<UpdateJob>(File.ReadAllText(Path.Combine(stage, "job.json")));
            string helper = Path.Combine(stage, "update-helper.exe");
            if (!String.Equals(Assembly.GetExecutingAssembly().Location, helper, StringComparison.OrdinalIgnoreCase) ||
                job == null || !Path.IsPathRooted(job.Target) || !ValidDigest(job.Digest) ||
                !String.Equals(Path.GetFileName(job.Target), "WorldAI.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("无法确认 WorldAI 的更新位置。");

            Process parent;
            try { parent = Process.GetProcessById(job.ParentId); }
            catch (ArgumentException) { parent = null; }
            if (parent != null)
            {
                using (parent)
                {
                    if (!parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == job.ParentStart &&
                        !parent.WaitForExit(45000))
                        throw new TimeoutException("旧窗口仍在运行，请关闭后重试。");
                }
            }
            using (var guard = new Mutex(false, @"Local\WorldAI.Desktop.v1"))
            {
                bool acquired;
                try { acquired = guard.WaitOne(1000); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new InvalidOperationException("WorldAI 还在运行，请关闭后重试。");
                try { ReplaceFromPackage(stage, job); }
                finally { guard.ReleaseMutex(); }
            }
            Process.Start(new ProcessStartInfo { FileName = job.Target, WorkingDirectory = Path.GetDirectoryName(job.Target), UseShellExecute = false });
        }

        private static void ReplaceFromPackage(string stage, UpdateJob job)
        {
            string file = Path.Combine(stage, "package.zip");
            Verify(file, job.Digest);
            string incoming = job.Target + "." + Guid.NewGuid().ToString("N") + ".next";
            try
            {
                using (var stream = InstallerBundle.Open(file))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                    InstallerBundle.Extract(zip, "WorldAI.exe", incoming, MaxBytes);
                File.Replace(incoming, job.Target, job.Target + ".update-backup");
                try { File.Delete(file); File.Delete(Path.Combine(stage, "job.json")); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            finally { if (File.Exists(incoming)) File.Delete(incoming); }
        }

        private static bool ValidDigest(string value)
        {
            return value != null && value.Length == 71 && value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                && value.Skip(7).All(Uri.IsHexDigit);
        }

        private static void Verify(string path, string checksum)
        {
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                string value = "sha256:" + BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
                if (!String.Equals(value, checksum, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新包校验失败，未安装。");
            }
        }
    }
}
