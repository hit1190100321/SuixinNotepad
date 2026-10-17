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

    /// <summary>检查或下载更新失败。Message 可以直接展示给用户。</summary>
    internal sealed class UpdateException : Exception
    {
        public UpdateException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// 从更新地址检查并下载新版安装包。更新地址可以是 https 网址，也可以是本地或共享文件夹，
    /// 其中放置 build.ps1 生成的 latest.json 和安装包。
    /// 清单里的 SHA-256 与安装包来自同一个地方，只能发现传输损坏，防不了篡改，
    /// 所以网址只接受 https，由 TLS 保证来源。
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

        /// <summary>检查更新地址是否可用；不可用时返回原因。</summary>
        public static string? ValidateSource(string source)
        {
            string s = source.Trim();
            if (s.Length == 0) return null;
            if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return "更新地址不能使用 http，请改用 https 网址或文件夹路径。";
            if (IsWeb(s)) return Uri.IsWellFormedUriString(s, UriKind.Absolute) ? null : "更新地址不是有效的网址。";
            return Path.IsPathRooted(s) ? null : "请填写完整的文件夹路径或 https 网址。";
        }

        /// <summary>读取更新清单。有更新时返回 UpdateInfo，否则返回 null。</summary>
        public static UpdateInfo? Check(string source)
        {
            string? invalid = ValidateSource(source);
            if (invalid != null) throw new UpdateException(invalid);

            string manifestLocation = ResolveManifest(source.Trim());
            UpdateManifest manifest;
            try
            {
                manifest = JsonFile.Deserialize<UpdateManifest>(ReadBytes(manifestLocation));
            }
            catch (SerializationException ex)
            {
                throw new UpdateException("更新清单格式不正确：" + ex.Message, ex);
            }

            // 程序版本只有三段，四段的清单版本永远比当前版本“新”，会反复提示更新
            if (manifest.Version == null || !Version.TryParse(manifest.Version, out Version? latest) || latest == null || latest.Revision >= 0)
                throw new UpdateException("更新清单中的版本号无效：" + manifest.Version);
            if (string.IsNullOrWhiteSpace(manifest.File) || string.IsNullOrWhiteSpace(manifest.Sha256))
                throw new UpdateException("更新清单缺少安装包信息。");

            if (latest <= CurrentVersion) return null;
            return new UpdateInfo(latest, Combine(manifestLocation, manifest.File.Trim()), manifest.Sha256.Trim());
        }

        /// <summary>下载安装包并校验 SHA-256，返回本地路径。</summary>
        public static string Download(UpdateInfo info, string targetDir)
        {
            byte[] bytes = ReadBytes(info.SetupLocation);
            string actual = Sha256Hex(bytes);
            if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("安装包校验失败，文件可能不完整或被篡改。");

            try
            {
                Directory.CreateDirectory(targetDir);
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
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new UpdateException("无法保存安装包：" + ex.Message, ex);
            }
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
            location.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        private static string ResolveManifest(string source)
        {
            if (source.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return source;
            if (IsWeb(source)) return source.TrimEnd('/') + "/" + ManifestName;
            return Path.Combine(source, ManifestName);
        }

        /// <summary>安装包位置。清单在网上时安装包也必须走 https；清单在文件夹中时安装包只能是同一文件夹中的文件名。</summary>
        private static string Combine(string manifestLocation, string file)
        {
            if (IsWeb(manifestLocation))
            {
                Uri resolved;
                try
                {
                    resolved = new Uri(new Uri(manifestLocation), file);
                }
                catch (UriFormatException ex)
                {
                    throw new UpdateException("更新清单中的安装包地址无效：" + file, ex);
                }
                if (resolved.Scheme != Uri.UriSchemeHttps) throw new UpdateException("更新清单中的安装包地址必须使用 https：" + file);
                return resolved.ToString();
            }
            if (file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || file == "." || file == "..")
                throw new UpdateException("更新清单中的安装包必须与清单放在同一文件夹：" + file);
            return Path.Combine(Path.GetDirectoryName(manifestLocation)!, file);
        }

        private static byte[] ReadBytes(string location)
        {
            try
            {
                if (!IsWeb(location)) return File.ReadAllBytes(location);
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "SuixinNotepad/" + CurrentVersion;
                    return client.DownloadData(location);
                }
            }
            catch (Exception ex) when (ex is WebException || ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is ArgumentException || ex is NotSupportedException)
            {
                throw new UpdateException("无法读取“" + location + "”：" + ex.Message, ex);
            }
        }
    }
}
