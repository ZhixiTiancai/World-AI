using System;
using System.IO;

namespace WorldAI
{
    internal static class PortablePaths
    {
        internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        internal static readonly string Files = Path.Combine(Root, "files");
        private static readonly string LegacyData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorldAI");

        internal static void ImportLegacy()
        {
            string oldDatabase = Path.Combine(LegacyData, "world.json");
            if (String.Equals(Path.GetFullPath(oldDatabase), Store.DataPath, StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(oldDatabase)) return;

            // Validate the old store before making it the portable copy.
            Store.Read(oldDatabase);
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!String.IsNullOrEmpty(docs))
            {
                string legacy = Path.Combine(docs, "WorldAI");
                foreach (string category in new[] { "角色卡", "世界书", "聊天记录", "背景图", "备份" })
                    CopyDirectory(Path.Combine(legacy, category), Path.Combine(Files, category));
            }
            CopyFileIfMissing(Path.Combine(LegacyData, "world.json.previous"), Store.DataPath + ".previous");
            CopyFileIfMissing(oldDatabase, Store.DataPath);
        }

        internal static string ReadExistingTheme()
        {
            string local = Path.Combine(Root, "data", "world.json");
            string old = Path.Combine(LegacyData, "world.json");
            if (File.Exists(local)) return Store.Read(local).Settings.ThemeId;
            return File.Exists(old) ? Store.Read(old).Settings.ThemeId : null;
        }

        private static void CopyDirectory(string source, string target)
        {
            string prefix = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string destination = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Directory.Exists(source) || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0 ||
                destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
            Directory.CreateDirectory(target);
            foreach (string path in Directory.EnumerateFiles(source))
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                    CopyFileIfMissing(path, Path.Combine(target, Path.GetFileName(path)));
            foreach (string path in Directory.EnumerateDirectories(source))
                CopyDirectory(path, Path.Combine(target, Path.GetFileName(path)));
        }

        private static void CopyFileIfMissing(string source, string target)
        {
            if (!File.Exists(source) || File.Exists(target)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".migration";
            try
            {
                File.Copy(source, temporary);
                File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
