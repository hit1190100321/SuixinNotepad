using System.IO;
using System.Runtime.Serialization;
using System.Text;
using SuixinNotepad.Core;
using Xunit;

namespace SuixinNotepad.Tests
{
    /// <summary>设置与会话文件：缺少字段时取默认值，字段内容无法使用时按损坏处理，不能让程序启动即崩溃。</summary>
    public class ConfigFileTests
    {
        private static T Parse<T>(string json) => JsonFile.Deserialize<T>(Encoding.UTF8.GetBytes(json));

        [Fact]
        public void 空对象取得全部默认值()
        {
            SessionState session = Parse<SessionState>("{}");
            Assert.Empty(session.Tabs);

            AppSettings settings = Parse<AppSettings>("{}");
            Assert.Equal(AppSettings.DefaultFontName, settings.FontName);
            Assert.Equal(AppSettings.DefaultFontSize, settings.FontSize);
            Assert.Equal(100, settings.ZoomPercent);
        }

        [Theory]
        [InlineData("{\"Tabs\":null}")]
        [InlineData("{\"Tabs\":[null]}")]
        [InlineData("{\"Tabs\":[{\"Path\":null}]}")]
        public void 会话字段无效时按损坏处理(string json)
        {
            Assert.Throws<SerializationException>(() => Parse<SessionState>(json));
        }

        [Theory]
        [InlineData("{\"FontName\":null}")]
        [InlineData("{\"FontName\":\"\"}")]
        [InlineData("{\"FontSize\":0}")]
        [InlineData("{\"FontSize\":100000}")]
        [InlineData("{\"NotesFolder\":null}")]
        public void 设置字段无效时按损坏处理(string json)
        {
            Assert.Throws<SerializationException>(() => Parse<AppSettings>(json));
        }

        [Fact]
        public void 更新地址留空时使用GitHub且默认自动检查()
        {
            // 早期版本把空地址（不检查更新）写进了设置文件
            AppSettings empty = Parse<AppSettings>("{\"UpdateSource\":\"\"}");
            Assert.True(empty.AutoCheckUpdates);
            Assert.Equal(AppSettings.DefaultUpdateSource, empty.EffectiveUpdateSource);
        }

        [Theory]
        [InlineData(AppSettings.FormerUpdateSource)]
        [InlineData(AppSettings.DefaultUpdateSource)]
        public void 写进设置文件的GitHub地址清空后跟随内置地址(string saved)
        {
            AppSettings settings = Parse<AppSettings>("{\"UpdateSource\":\"" + saved + "\"}");
            Assert.Equal("", settings.UpdateSource);
            Assert.Equal(AppSettings.DefaultUpdateSource, settings.EffectiveUpdateSource);
        }

        [Fact]
        public void 自行架设的更新地址保持不变()
        {
            AppSettings settings = Parse<AppSettings>("{\"UpdateSource\":\"https://example.com/suixin/\",\"AutoCheckUpdates\":false}");
            Assert.Equal("https://example.com/suixin/", settings.EffectiveUpdateSource);
            Assert.False(settings.AutoCheckUpdates);
        }

        [Fact]
        public void 损坏的文件改名留档后使用默认内容()
        {
            using (var temp = new TempDir())
            {
                string path = temp.File("session.json");
                File.WriteAllText(path, "{\"Tabs\":null}");

                SessionState session = JsonFile.LoadOrDefault(path, () => new SessionState(), out string? broken);

                Assert.Empty(session.Tabs);
                Assert.NotNull(broken);
                Assert.False(File.Exists(path));
                Assert.Equal("{\"Tabs\":null}", File.ReadAllText(broken!));
            }
        }
    }
}
