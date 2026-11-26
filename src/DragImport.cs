using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace WorldAI
{
    internal sealed partial class Shell
    {
        private bool CanImportDrop(DragEventArgs args)
        {
            return work == null && (page == "Characters" || page == "Books") && args.Data.GetDataPresent(DataFormats.FileDrop);
        }

        private void OnFileDrag(object sender, DragEventArgs args)
        {
            if (!args.Data.GetDataPresent(DataFormats.FileDrop)) return;
            args.Effects = CanImportDrop(args) && (args.AllowedEffects & DragDropEffects.Copy) != 0
                ? DragDropEffects.Copy : DragDropEffects.None;
            args.Handled = true;
        }

        private void OnFileDrop(object sender, DragEventArgs args)
        {
            if (!CanImportDrop(args)) return;
            args.Handled = true;
            var paths = args.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null) Guard(() => ImportDropped(paths));
        }

        private void ImportDropped(string[] paths)
        {
            var roles = new List<Character>();
            var books = new List<Lore>();
            var failures = new List<string>();
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension != ".png" && extension != ".json")
                        throw new InvalidDataException("仅支持 PNG 角色卡或 JSON 文件。");
                    if (extension == ".json" && Cards.IsWorldBook(path)) books.Add(Cards.ImportLore(path));
                    else
                    {
                        Lore embedded;
                        roles.Add(Cards.Import(path, out embedded));
                        if (embedded != null) books.Add(embedded);
                    }
                }
                catch (Exception error) { failures.Add(Path.GetFileName(path) + "：" + error.Message); }
            }
            if (roles.Count + books.Count > 0)
            {
                int startRoles = world.Characters.Count, startBooks = world.Books.Count;
                world.Characters.AddRange(roles);
                world.Books.AddRange(books);
                try { Save(); }
                catch
                {
                    world.Characters.RemoveRange(startRoles, roles.Count);
                    world.Books.RemoveRange(startBooks, books.Count);
                    throw;
                }
                RenderLibrary();
            }
            string result = "已导入 " + roles.Count + " 张角色卡、" + books.Count + " 本世界书。";
            if (failures.Count > 0) result += "\n\n未能导入 " + failures.Count + " 个文件：\n" + String.Join("\n", failures.Take(8));
            Info(result);
        }
    }
}
