using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;

namespace WorldAI
{
    internal static class Cards
    {
        internal static object Get(object value, string key)
        {
            var map = value as Dictionary<string, object>;
            object result;
            return map != null && map.TryGetValue(key, out result) ? result : null;
        }

        private static string ReadText(object value, string key)
        {
            return Get(value, key) as string ?? "";
        }

        internal static Character Import(string path, out Lore book)
        {
            CheckSize(path);
            bool png = String.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase);
            string json = png ? ReadPngCard(File.ReadAllBytes(path)) : File.ReadAllText(path, Encoding.UTF8);
            var root = Store.Json().DeserializeObject(json.TrimStart('\uFEFF').Trim());
            var data = Get(root, "data") ?? root;
            string name = ReadText(data, "name").Trim();
            if (name.Length == 0) throw new InvalidDataException("角色卡缺少角色名。");

            string background = String.Join("\n\n", new[] { ReadText(data, "description"), ReadText(data, "scenario") }
                .Where(value => !String.IsNullOrWhiteSpace(value)));
            book = null;
            var embedded = Get(data, "character_book");
            if (embedded != null && Get(embedded, "entries") != null)
                book = ReadLore(embedded, name + "的世界书");
            var alternatives = Get(data, "alternate_greetings") as object[];
            var role = new Character {
                Name = name, Personality = ReadText(data, "personality"), Background = background,
                Greeting = ReadText(data, "first_mes"), Examples = ReadText(data, "mes_example"),
                AlternateGreetings = alternatives == null ? new List<string>() : alternatives.OfType<string>().ToList(),
                Avatar = png ? AvatarFromFile(path) : ReadText(Get(data, "extensions"), "worldai_avatar_png"), BookId = book == null ? null : book.Id
            };
            role.Normalize();
            return role;
        }

        internal static string Export(Character role, Lore book)
        {
            var data = new Dictionary<string, object> {
                { "name", role.Name }, { "description", role.Background ?? "" },
                { "personality", role.Personality ?? "" }, { "scenario", "" },
                { "first_mes", role.Greeting ?? "" }, { "mes_example", role.Examples ?? "" },
                { "creator_notes", "" }, { "system_prompt", "" }, { "post_history_instructions", "" },
                { "alternate_greetings", role.AlternateGreetings }, { "tags", new string[0] },
                { "creator", "" }, { "character_version", "" }, { "extensions", String.IsNullOrWhiteSpace(role.Avatar)
                    ? new Dictionary<string, object>()
                    : new Dictionary<string, object> { { "worldai_avatar_png", role.Avatar } } }
            };
            if (book != null)
            {
                data["character_book"] = new {
                    name = book.Name, extensions = new Dictionary<string, object>(),
                    entries = book.Entries.Select((entry, index) => new {
                        id = index, name = entry.Name, content = entry.Content ?? "",
                        keys = SplitKeywords(entry.Keywords), secondary_keys = new string[0],
                        enabled = entry.Enabled, constant = entry.Always, selective = false,
                        insertion_order = index, extensions = new { worldai_retrieval = Entry.Mode(entry.Retrieval) }
                    }).ToArray()
                };
            }
            return Store.Json().Serialize(new { spec = "chara_card_v2", spec_version = "2.0", data = data });
        }

        internal static Lore ImportLore(string path)
        {
            CheckSize(path);
            var root = Store.Json().DeserializeObject(File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF').Trim());
            return ReadLore(root, Path.GetFileNameWithoutExtension(path));
        }

        internal static bool IsWorldBook(string path)
        {
            CheckSize(path);
            var data = Store.Json().DeserializeObject(File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF').Trim());
            return Get(data, "entries") != null;
        }

        internal static string ExportLore(Lore book)
        {
            var entries = new Dictionary<string, object>();
            for (int i = 0; i < book.Entries.Count; i++)
            {
                var entry = book.Entries[i];
                entries.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture), new {
                    uid = i, key = SplitKeywords(entry.Keywords), keysecondary = new string[0],
                    comment = entry.Name, content = entry.Content ?? "", disable = !entry.Enabled,
                    constant = entry.Always, selective = false, order = i, position = 0, probability = 100,
                    useProbability = false, displayIndex = i, extensions = new { worldai_retrieval = Entry.Mode(entry.Retrieval) }
                });
            }
            return Store.Json().Serialize(new { name = book.Name, entries = entries });
        }

        private static Lore ReadLore(object value, string fallbackName)
        {
            string name = ReadText(value, "name");
            var lore = new Lore { Name = String.IsNullOrWhiteSpace(name) ? fallbackName : name.Trim() };
            var rawEntries = Get(value, "entries");
            var map = rawEntries as Dictionary<string, object>;
            var array = rawEntries as object[];
            if (map == null && array == null) throw new InvalidDataException("找不到可识别的世界书条目。");

            foreach (object raw in array != null ? array.AsEnumerable() : map.Values)
            {
                if (!(raw is Dictionary<string, object>)) throw new InvalidDataException("世界书中有无效条目。");
                string entryName = ReadText(raw, "comment");
                if (String.IsNullOrWhiteSpace(entryName)) entryName = ReadText(raw, "name");
                var keywords = Get(raw, "keys") ?? Get(raw, "key");
                var words = keywords as object[];
                lore.Entries.Add(new Entry {
                    Name = String.IsNullOrWhiteSpace(entryName) ? "条目 " + (lore.Entries.Count + 1) : entryName,
                    Keywords = words == null ? (keywords as string ?? "") : String.Join(", ", words.OfType<string>()),
                    Content = ReadText(raw, "content"), Enabled = !Equals(Get(raw, "disable"), true) && !Equals(Get(raw, "enabled"), false),
                    Always = Equals(Get(raw, "constant"), true),
                    Retrieval = Entry.Mode(Get(Get(raw, "extensions"), "worldai_retrieval"))
                });
            }
            return lore;
        }

        private static string[] SplitKeywords(string keywords)
        {
            return (keywords ?? "").Split(new[] { ',', '，', '、', '\n', ';', '；' },
                StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).Where(value => value.Length > 0).ToArray();
        }

        internal static string AvatarFromFile(string path)
        {
            CheckSize(path);
            using (var stream = File.OpenRead(path))
            {
                var source = new BitmapImage();
                source.BeginInit();
                source.StreamSource = stream;
                source.DecodePixelWidth = 256;
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.EndInit();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                using (var output = new MemoryStream())
                {
                    encoder.Save(output);
                    return Convert.ToBase64String(output.ToArray());
                }
            }
        }

        private static void CheckSize(string path)
        {
            if (new FileInfo(path).Length > 32 * 1024 * 1024)
                throw new InvalidDataException("导入文件超过 32 MB。");
        }

        private static string ReadPngCard(byte[] png)
        {
            var signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (!png.Take(8).SequenceEqual(signature)) throw new InvalidDataException("这不是有效的 PNG。");
            string legacy = null;
            string v3 = null;
            for (int pos = 8; pos <= png.Length - 12;)
            {
                uint size = ((uint)png[pos] << 24) | ((uint)png[pos + 1] << 16) | ((uint)png[pos + 2] << 8) | png[pos + 3];
                if (size > png.Length - pos - 12) throw new InvalidDataException("PNG 数据不完整。");
                int start = pos + 8;
                int length = (int)size;
                int end = start + length;
                string type = Encoding.ASCII.GetString(png, pos + 4, 4);
                if (type == "IEND") break;
                if (type == "tEXt" || type == "iTXt" || type == "zTXt")
                {
                    int zero = Array.IndexOf(png, (byte)0, start, length);
                    string name = zero < 0 ? "" : Encoding.ASCII.GetString(png, start, zero - start);
                    if (name == "chara" || name == "ccv3")
                    {
                        string value = DecodeChunk(png, zero + 1, end, type);
                        if (name == "ccv3") v3 = value;
                        else legacy = value;
                    }
                }
                pos = end + 4;
            }
            string card = v3 ?? legacy;
            if (String.IsNullOrWhiteSpace(card)) throw new InvalidDataException("这张 PNG 没有内嵌角色卡数据。");
            card = card.Trim();
            return card.StartsWith("{") ? card : Encoding.UTF8.GetString(Convert.FromBase64String(card));
        }

        private static string DecodeChunk(byte[] png, int from, int end, string type)
        {
            if (type == "tEXt") return Encoding.UTF8.GetString(png, from, end - from);
            bool compressed = type == "zTXt";
            if (type == "iTXt")
            {
                if (end - from < 2) throw new InvalidDataException("PNG 文本块不完整。");
                compressed = png[from] == 1;
                from += 2;
                for (int i = 0; i < 2; i++)
                {
                    int zero = Array.IndexOf(png, (byte)0, from, end - from);
                    if (zero < 0) throw new InvalidDataException("PNG 文本块不完整。");
                    from = zero + 1;
                }
            }
            else from++;
            if (!compressed) return Encoding.UTF8.GetString(png, from, end - from);
            if (end - from < 6) throw new InvalidDataException("PNG 压缩文本块不完整。");
            using (var stream = new MemoryStream(png, from + 2, end - from - 6))
            using (var deflate = new DeflateStream(stream, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                int count;
                while ((count = deflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + count > 8 * 1024 * 1024)
                        throw new InvalidDataException("PNG 内嵌卡片过大。");
                    output.Write(buffer, 0, count);
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }
}
