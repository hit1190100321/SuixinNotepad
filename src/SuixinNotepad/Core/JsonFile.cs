using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SuixinNotepad.Core
{
    /// <summary>基于 DataContract 的 JSON 读写，写入使用原子替换。</summary>
    internal static class JsonFile
    {
        private static DataContractJsonSerializer Serializer<T>() =>
            new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings
            {
                UseSimpleDictionaryFormat = true,
                DateTimeFormat = new DateTimeFormat("yyyy-MM-dd'T'HH:mm:ss"),
            });

        public static T Deserialize<T>(byte[] json)
        {
            using (var ms = new MemoryStream(json))
            {
                object? value = Serializer<T>().ReadObject(ms);
                if (value == null) throw new SerializationException("JSON 内容为空。");
                return (T)value;
            }
        }

        public static byte[] Serialize<T>(T value)
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, new UTF8Encoding(false), false, true, "  "))
                {
                    Serializer<T>().WriteObject(writer, value);
                }
                return ms.ToArray();
            }
        }

        public static T Load<T>(string path) => Deserialize<T>(File.ReadAllBytes(path));

        public static void Save<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            TextFile.WriteAtomic(path, Serialize(value));
        }

        /// <summary>
        /// 读取配置类文件。文件不存在时返回 fallback；文件损坏时把它改名留档并返回 fallback，
        /// 同时通过 brokenCopy 告知调用方，由调用方提示用户。
        /// </summary>
        public static T LoadOrDefault<T>(string path, Func<T> fallback, out string? brokenCopy)
        {
            brokenCopy = null;
            if (!File.Exists(path)) return fallback();
            try
            {
                return Load<T>(path);
            }
            catch (SerializationException)
            {
                brokenCopy = path + ".损坏-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Move(path, brokenCopy);
                return fallback();
            }
        }
    }
}
