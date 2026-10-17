using System.IO;
using SuixinNotepad.Core;
using Xunit;

namespace SuixinNotepad.Tests
{
    /// <summary>清单中的 SHA-256 与安装包来自同一处，只有来源可信时校验才有意义，所以更新地址和安装包位置都要受限。</summary>
    public class UpdateServiceTests
    {
        [Theory]
        [InlineData("http://example.com/updates")]
        [InlineData("updates")]
        [InlineData("https://")]
        public void 不安全或不完整的更新地址被拒绝(string source)
        {
            Assert.NotNull(UpdateService.ValidateSource(source));
            Assert.Throws<UpdateException>(() => UpdateService.Check(source));
        }

        [Theory]
        [InlineData("https://example.com/updates")]
        [InlineData(@"\\server\share\updates")]
        [InlineData(@"D:\updates")]
        public void 合法的更新地址通过检查(string source)
        {
            Assert.Null(UpdateService.ValidateSource(source));
        }

        private static string WriteManifest(TempDir temp, string version, string file)
        {
            File.WriteAllText(temp.File(UpdateService.ManifestName),
                "{\"version\":\"" + version + "\",\"file\":\"" + file.Replace("\\", "\\\\") + "\",\"sha256\":\"00\"}");
            return temp.Path;
        }

        [Theory]
        [InlineData("99.0.0", @"..\evil.exe")]
        [InlineData("99.0.0", @"C:\evil.exe")]
        [InlineData("99.0.0", "http://example.com/setup.exe")]
        [InlineData("99.0.0.1", "setup.exe")]
        [InlineData("abc", "setup.exe")]
        [InlineData("99.0.0", "")]
        public void 文件夹清单中的无效内容被拒绝(string version, string file)
        {
            using (var temp = new TempDir())
            {
                string source = WriteManifest(temp, version, file);
                Assert.Throws<UpdateException>(() => UpdateService.Check(source));
            }
        }

        [Fact]
        public void 文件夹清单中的安装包与清单放在一起()
        {
            using (var temp = new TempDir())
            {
                string source = WriteManifest(temp, "99.0.0", "setup.exe");
                UpdateInfo? info = UpdateService.Check(source);
                Assert.NotNull(info);
                Assert.Equal(temp.File("setup.exe"), info!.SetupLocation);
            }
        }
    }
}
