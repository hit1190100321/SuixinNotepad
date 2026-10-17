using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace SuixinNotepad.Core
{
    /// <summary>更新清单 latest.json，由 build.ps1 随安装包一起生成。</summary>
    [DataContract]
    internal sealed class UpdateManifest
    {
        [DataMember(Name = "version")] public string Version { get; set; } = "";

        /// <summary>安装包文件名，相对 latest.json 所在位置。</summary>
        [DataMember(Name = "file")] public string File { get; set; } = "";

        [DataMember(Name = "sha256")] public string Sha256 { get; set; } = "";
    }

    internal sealed class UpdateInfo
    {
        public UpdateInfo(Version version, string setupLocation, string sha256)
        {
            Version = version;
            SetupLocation = setupLocation;
            Sha256 = sha256;
        }

        public Version Version { get; }
        public string SetupLocation { get; }
        public string Sha256 { get; }
    }

    /// <summary>
    /// 从更新地址检查并下载新版安装包。更新地址可以是网址，也可以是本地或共享文件夹，
    /// 其中放置 build.ps1 生成的 latest.json 和安装包。
    /// </summary>
    internal static class UpdateService
    {
        public const string ManifestName = "latest.json";

        static UpdateService()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public static Version CurrentVersion
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version!;
                return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            }
        }

        /// <summary>读取更新清单。有更新时返回 UpdateInfo，否则返回 null。</summary>
        public static UpdateInfo? Check(string source)
        {
            string manifestLocation = ResolveManifest(source.Trim());
            UpdateManifest manifest = JsonFile.Deserialize<UpdateManifest>(ReadBytes(manifestLocation));

            if (!Version.TryParse(manifest.Version, out Version? latest) || latest == null)
                throw new InvalidDataException("更新清单中的版本号无效：" + manifest.Version);
            if (string.IsNullOrWhiteSpace(manifest.File) || string.IsNullOrWhiteSpace(manifest.Sha256))
                throw new InvalidDataException("更新清单缺少安装包信息。");

            if (latest <= CurrentVersion) return null;
            return new UpdateInfo(latest, Combine(manifestLocation, manifest.File), manifest.Sha256.Trim());
        }

        /// <summary>下载安装包并校验 SHA-256，返回本地路径。</summary>
        public static string Download(UpdateInfo info, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            byte[] bytes = ReadBytes(info.SetupLocation);
            string actual = Sha256Hex(bytes);
            if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("安装包校验失败，文件可能不完整或被篡改。");

            // 上一次更新留下的安装包已经没用了；正在运行的安装包删不掉，留到下次再删
            foreach (string old in Directory.GetFiles(targetDir, "*.exe"))
            {
                try { File.Delete(old); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            string path = Path.Combine(targetDir, "SuixinNotepad-Setup-" + info.Version + ".exe");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static bool IsWeb(string location) =>
            location.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            location.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        private static string ResolveManifest(string source)
        {
            if (source.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return source;
            if (IsWeb(source)) return source.TrimEnd('/') + "/" + ManifestName;
            return Path.Combine(source, ManifestName);
        }

        private static string Combine(string manifestLocation, string file)
        {
            if (IsWeb(file)) return file;
            if (IsWeb(manifestLocation)) return new Uri(new Uri(manifestLocation), file).ToString();
            return Path.Combine(Path.GetDirectoryName(manifestLocation)!, file);
        }

        private static byte[] ReadBytes(string location)
        {
            if (!IsWeb(location)) return File.ReadAllBytes(location);
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "SuixinNotepad/" + CurrentVersion;
                return client.DownloadData(location);
            }
        }
    }
}
