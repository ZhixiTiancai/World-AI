using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WorldAI
{
    internal static class Context
    {
        internal static List<Entry> Matches(World world, Session session, IEnumerable<Message> history)
        {
            var role = Find(world.Characters, session.CharacterId);
            var book = role == null ? null : Find(world.Books, role.BookId);
            if (!session.UseWorldBook || book == null) return new List<Entry>();
            string recent = String.Join("\n", Tail(history, 6).Select(message => message.Text));
            return book.Entries.Where(entry => entry.Enabled && !String.IsNullOrWhiteSpace(entry.Content)
                && (entry.Always || (entry.Keywords ?? "").Split(new[] { ',', '，', '\n', '、', ';', '；' },
                    StringSplitOptions.RemoveEmptyEntries).Select(keyword => keyword.Trim())
                    .Any(keyword => keyword.Length > 0 && recent.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)))
                .ToList();
        }

        internal static List<Message> Build(World world, Session session, IEnumerable<Message> history,
            out int omittedMessages, out int omittedEntries)
        {
            var role = Find(world.Characters, session.CharacterId);
            var profile = Player(world, session);
            var preset = Find(world.Presets, session.PresetId);
            var recent = Tail(history, session.HistoryCount ?? world.Settings.HistoryCount).ToList();
            int limit = session.ContextCharacters ?? world.Settings.ContextCharacters;
            var now = DateTime.Now;
            var prompt = new StringBuilder("请用对话所用的语言进行角色扮演，延续当前场景。由玩家决定自己的行动与台词。");
            Add(prompt, "全局提示词", world.Settings.SystemPrompt);
            if (role != null)
            {
                Add(prompt, "扮演角色", role.Name);
                Add(prompt, "背景与场景", role.Background);
                Add(prompt, "示例对话", role.Examples);
            }
            if (profile != null)
            {
                Add(prompt, "玩家身份", profile.Name);
                Add(prompt, "玩家背景", profile.Description);
            }
            if (preset != null)
            {
                Add(prompt, "自定义指令", preset.SystemPrompt);
                Add(prompt, "写作风格", preset.Style);
                Add(prompt, "回复长度", preset.Length);
            }
            Add(prompt, "会话提示词", session.SystemPrompt);
            if (session.SendTime) Add(prompt, "当前本地时间", now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
            if (session.UseMemory)
            {
                Add(prompt, "剧情摘要", session.Summary);
                Add(prompt, "钉住的事件", String.Join("\n", session.Memories.Select(memory => "• " + memory)));
            }

            string instructions = Expand(prompt.ToString(), role, profile, now);
            int latestSize = recent.Count == 0 ? 0 : (recent[recent.Count - 1].Text ?? "").Length;
            if (limit > 0 && (long)instructions.Length + latestSize > limit)
                throw new InvalidOperationException("提示词和最新一条消息已超过发送字数上限。请缩短内容或在会话设置里提高上限。");

            long remaining = limit == 0 ? long.MaxValue : limit - instructions.Length - latestSize;
            var system = new StringBuilder(instructions);
            omittedEntries = 0;
            foreach (var entry in Matches(world, session, recent))
            {
                var block = new StringBuilder();
                Add(block, "世界设定「" + entry.Name + "」", entry.Content);
                string text = Expand(block.ToString(), role, profile, now);
                if (text.Length > remaining) { omittedEntries++; continue; }
                system.Append(text);
                remaining -= text.Length;
            }

            int start = Math.Max(0, recent.Count - 1);
            while (start > 0 && (recent[start - 1].Text ?? "").Length <= remaining)
            {
                start--;
                remaining -= (recent[start].Text ?? "").Length;
            }
            omittedMessages = start;
            var messages = new List<Message> { new Message { Role = "system", Text = system.ToString() } };
            messages.AddRange(recent.Skip(start));
            return messages;
        }

        internal static string Expand(string value, Character role, Profile profile)
        {
            return Expand(value, role, profile, DateTime.Now);
        }

        private static string Expand(string value, Character role, Profile profile, DateTime now)
        {
            return System.Text.RegularExpressions.Regex.Replace(value ?? "",
                @"\{\{(char|user|year|month|day|hour|minute|second)\}\}", match =>
                {
                    switch (match.Groups[1].Value.ToLowerInvariant())
                    {
                        case "char": return role == null ? "角色" : role.Name;
                        case "user": return profile == null ? "玩家" : profile.Name;
                        case "year": return now.ToString("yyyy", CultureInfo.InvariantCulture);
                        case "month": return now.ToString("MM", CultureInfo.InvariantCulture);
                        case "day": return now.ToString("dd", CultureInfo.InvariantCulture);
                        case "hour": return now.ToString("HH", CultureInfo.InvariantCulture);
                        case "minute": return now.ToString("mm", CultureInfo.InvariantCulture);
                        default: return now.ToString("ss", CultureInfo.InvariantCulture);
                    }
                }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        internal static IEnumerable<T> Tail<T>(IEnumerable<T> items, int count)
        {
            var list = items as IList<T> ?? items.ToList();
            return list.Skip(Math.Max(0, list.Count - count));
        }

        internal static T Find<T>(IEnumerable<T> items, string id) where T : Item
        {
            return items.FirstOrDefault(item => item.Id == id);
        }

        internal const string NoProfile = "no-profile";

        internal static Profile Player(World world, Session session)
        {
            return Find(world.Profiles, session.ProfileId ?? world.Settings.DefaultProfileId);
        }

        private static void Add(StringBuilder prompt, string label, string value)
        {
            if (!String.IsNullOrWhiteSpace(value))
                prompt.Append("\n\n【").Append(label).Append("】\n").Append(value.Trim());
        }
    }
}
