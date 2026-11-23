using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace WorldAI
{
    public class Item
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public Item() { Id = Guid.NewGuid().ToString("N"); Name = ""; }
        public override string ToString() { return Name; }
    }

    public class Character : Item
    {
        public string Avatar { get; set; }
        // Kept for reading older WorldAI data.
        public string Personality { get; set; }
        public string Background { get; set; }
        public string Greeting { get; set; }
        public List<string> AlternateGreetings { get; set; }
        public string Examples { get; set; }
        public string BookId { get; set; }

        public Character()
        {
            AlternateGreetings = new List<string>();
        }

        internal void Normalize()
        {
            Background = (Background ?? "").Trim();
            string personality = (Personality ?? "").Trim();
            if (personality.Length > 0 && Background.IndexOf(personality, StringComparison.Ordinal) < 0)
                Background = Background.Length == 0 ? personality : Background + "\n\n性格：" + personality;
            Personality = null;
            AlternateGreetings = (AlternateGreetings ?? new List<string>())
                .Where(text => !String.IsNullOrWhiteSpace(text)).Select(text => text.Trim()).ToList();
        }

        internal IEnumerable<string> Greetings()
        {
            if (!String.IsNullOrWhiteSpace(Greeting)) yield return Greeting;
            foreach (string greeting in AlternateGreetings) yield return greeting;
        }
    }

    public class Profile : Item
    {
        public string Description { get; set; }
    }

    public class Preset : Item
    {
        public string SystemPrompt { get; set; }
        public string Style { get; set; }
        public string Length { get; set; }
    }

    public class Model : Item
    {
        public string Endpoint { get; set; }
        public string ModelName { get; set; }
        public string EncryptedKey { get; set; }
        public bool Demo { get; set; }
    }

    public class Lore : Item
    {
        public List<Entry> Entries { get; set; }
        public Lore() { Entries = new List<Entry>(); }
    }

    public class Entry : Item
    {
        public string Keywords { get; set; }
        public string Content { get; set; }
        public bool Enabled { get; set; }
        public bool Always { get; set; }
        public Entry() { Enabled = true; }
    }

    public class Message
    {
        public string Role { get; set; }
        public string Text { get; set; }
    }

    public class Preferences
    {
        public string DefaultModelId { get; set; }
        public string DefaultProfileId { get; set; }
        public string ThemeId { get; set; }
        public bool CheckUpdatesOnStart { get; set; }
        public string SystemPrompt { get; set; }
        public int HistoryCount { get; set; }
        public int ContextCharacters { get; set; }
        public int PageSize { get; set; }
        public bool AlwaysOnTop { get; set; }

        public Preferences()
        {
            HistoryCount = 40;
            PageSize = 100;
            ThemeId = "green";
            CheckUpdatesOnStart = true;
        }
    }

    public class Session : Item
    {
        public string CharacterId { get; set; }
        public string ModelId { get; set; }
        public string ProfileId { get; set; }
        public string PresetId { get; set; }
        public string Summary { get; set; }
        public List<string> Memories { get; set; }
        public List<Message> Messages { get; set; }
        public DateTime Updated { get; set; }
        public string SystemPrompt { get; set; }
        public int? HistoryCount { get; set; }
        public int? ContextCharacters { get; set; }
        public bool StreamReplies { get; set; }
        public bool SendTime { get; set; }
        public bool UseMemory { get; set; }
        public bool UseWorldBook { get; set; }
        public int MessageFontSize { get; set; }
        public bool ShowSpeaker { get; set; }
        public bool ShowCharacterAvatar { get; set; }
        public bool RefocusComposer { get; set; }

        public Session()
        {
            Memories = new List<string>();
            Messages = new List<Message>();
            Updated = DateTime.UtcNow;
            StreamReplies = true;
            UseMemory = true;
            UseWorldBook = true;
            MessageFontSize = 14;
            ShowSpeaker = true;
            RefocusComposer = true;
        }
    }

    public class World
    {
        public int Version { get; set; }
        public List<Character> Characters { get; set; }
        public List<Profile> Profiles { get; set; }
        public List<Preset> Presets { get; set; }
        public List<Model> Models { get; set; }
        public List<Lore> Books { get; set; }
        public List<Session> Sessions { get; set; }
        public Preferences Settings { get; set; }
        public World()
        {
            Version = 1;
            Characters = new List<Character>();
            Profiles = new List<Profile>();
            Presets = new List<Preset>();
            Models = new List<Model>();
            Books = new List<Lore>();
            Sessions = new List<Session>();
            Settings = new Preferences();
        }
    }

    internal static class Store
    {
        internal static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorldAI");
        internal static readonly string DataPath = Path.Combine(Folder, "world.json");

        internal static JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        }

        internal static T Copy<T>(T item)
        {
            var json = Json();
            return json.Deserialize<T>(json.Serialize(item));
        }

        internal static World Load()
        {
            return File.Exists(DataPath) ? Read(DataPath) : Seed();
        }

        internal static World Read(string path)
        {
            var json = Json();
            var content = File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF').Trim();
            var map = json.DeserializeObject(content) as Dictionary<string, object>;
            if (map == null || !map.ContainsKey("Version") || !Equals(map["Version"], 1) ||
                new[] { "Characters", "Profiles", "Presets", "Models", "Books", "Sessions" }
                    .Any(key => !map.ContainsKey(key) || !(map[key] is object[])))
                throw new InvalidDataException("该文件不是可识别的 WorldAI v1 数据。");

            var world = json.Deserialize<World>(content);
            if (world.Settings == null) world.Settings = new Preferences();
            if (world.Settings.HistoryCount < 1 || world.Settings.HistoryCount > 500)
                world.Settings.HistoryCount = new Preferences().HistoryCount;
            if (world.Settings.PageSize < 10 || world.Settings.PageSize > 500)
                world.Settings.PageSize = new Preferences().PageSize;
            if (!IsContextLimit(world.Settings.ContextCharacters)) world.Settings.ContextCharacters = 0;
            var items = world.Characters.Cast<Item>().Concat(world.Profiles).Concat(world.Presets)
                .Concat(world.Models).Concat(world.Books).Concat(world.Sessions).ToList();
            if (items.Any(item => item == null || String.IsNullOrWhiteSpace(item.Id) ||
                String.IsNullOrWhiteSpace(item.Name)) || items.Select(item => item.Id).Distinct().Count() != items.Count ||
                world.Books.Any(book => book.Entries == null || book.Entries.Any(entry => entry == null)) ||
                world.Sessions.Any(session => session.Messages == null || session.Memories == null ||
                    session.Messages.Any(message => message == null ||
                        (message.Role != "user" && message.Role != "assistant"))))
                throw new InvalidDataException("数据中有损坏的角色、世界书或会话。");
            foreach (var role in world.Characters) role.Normalize();
            foreach (var session in world.Sessions)
            {
                if (session.HistoryCount < 1 || session.HistoryCount > 500) session.HistoryCount = null;
                if (session.ContextCharacters.HasValue && !IsContextLimit(session.ContextCharacters.Value))
                    session.ContextCharacters = null;
                if (session.MessageFontSize < 10 || session.MessageFontSize > 32) session.MessageFontSize = 14;
            }
            return world;
        }

        internal static bool IsContextLimit(int value)
        {
            return value == 0 || (value >= 1000 && value <= 500000);
        }

        internal static void Save(World world)
        {
            Directory.CreateDirectory(Folder);
            Write(DataPath, Json().Serialize(world), true);
        }

        internal static void Write(string path, string content, bool keepPrevious)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, content, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temp, path, keepPrevious ? path + ".previous" : null);
                else
                    File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        internal static void Backup(World world, string path)
        {
            if (String.Equals(Path.GetFullPath(path), DataPath, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(Path.GetFullPath(path), DataPath + ".previous", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请选择一个独立的备份文件。");
            var portable = Copy(world);
            foreach (var model in portable.Models) model.EncryptedKey = null;
            Write(path, Json().Serialize(portable), false);
        }

        internal static string Encrypt(string key)
        {
            if (String.IsNullOrWhiteSpace(key)) return "";
            return Convert.ToBase64String(ProtectedData.Protect(
                Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));
        }

        internal static string Decrypt(string key)
        {
            if (String.IsNullOrWhiteSpace(key)) return "";
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                    Convert.FromBase64String(key), null, DataProtectionScope.CurrentUser));
            }
            catch (Exception error)
            {
                if (!(error is CryptographicException) && !(error is FormatException)) throw;
                throw new InvalidOperationException("当前 Windows 账户无法读取这个 API 密钥，请在模型设置中重新填写。");
            }
        }

        private static World Seed()
        {
            var world = new World();
            var book = new Lore { Name = "雾港设定" };
            book.Entries.Add(new Entry {
                Name = "灯塔", Keywords = "灯塔, 灯光",
                Content = "雾港的旧灯塔已经封闭七年。今夜塔灯亮了三次，这是旧航线的求救信号。"
            });
            var role = new Character {
                Name = "林渡",
                Background = "住在雾港的信使。今夜在停运的渡船旁遇见了玩家。\n\n性格：冷静、观察细致，熟悉码头上的每一条小路。",
                Greeting = "雾把渡口吞得只剩一盏灯。林渡把一封受潮的信塞进你手里，压低声音说：“如果有人问起，你今晚没见过我。”",
                BookId = book.Id
            };
            var demo = new Model { Name = "离线演示", Demo = true };
            world.Books.Add(book);
            world.Characters.Add(role);
            world.Models.Add(demo);
            world.Presets.Add(new Preset { Name = "沉浸式叙事", Style = "用简洁的场景描写推进情节，为玩家留下行动空间。", Length = "约 150–250 字" });
            var session = new Session { Name = "雾港来信", CharacterId = role.Id, ModelId = demo.Id, PresetId = world.Presets[0].Id };
            session.Messages.Add(new Message { Role = "assistant", Text = role.Greeting });
            world.Sessions.Add(session);
            return world;
        }
    }
}
