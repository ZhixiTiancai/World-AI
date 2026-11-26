using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WorldAI
{
    internal static class FilePicker
    {
        private sealed class Format : Item
        {
            internal string[] Extensions;

            internal bool Accepts(string name)
            {
                return Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
            }
        }

        private sealed class FileRow
        {
            internal string Path;
            internal bool IsFolder;
            public string Label { get; set; }
        }

        internal static string Open(Window owner, string filter, string initialFolder = null)
        {
            return Select(owner, filter, null, null, initialFolder, false);
        }

        internal static string Save(Window owner, string filter, string extension, string name, string initialFolder = null)
        {
            return Select(owner, filter, extension, name, initialFolder, false);
        }

        internal static string ChooseFolder(Window owner, string initialFolder)
        {
            return Select(owner, null, null, null, initialFolder, true);
        }

        private static string Select(Window owner, string filter, string extension, string suggestedName, string initialFolder, bool foldersOnly)
        {
            bool saving = extension != null;
            var form = new Form(owner, foldersOnly ? "选择文件夹" : saving ? "保存文件" : "选择文件",
                foldersOnly ? "选择此文件夹" : saving ? "保存" : "打开", 750);
            var formats = foldersOnly ? new List<Format>() : ParseFormats(filter);
            var places = new List<Item>();
            AddPlace(places, "桌面", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddPlace(places, "文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddPlace(places, "下载", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            foreach (var drive in DriveInfo.GetDrives()) AddPlace(places, drive.Name, drive.Name);
            var place = form.Choice("常用位置", places, null, "选择位置");
            var folderInput = form.Text("文件夹路径", "");
            var navigation = new WrapPanel();
            form.Add(null, navigation);
            var files = new ListBox { Height = 260, DisplayMemberPath = "Label" };
            ScrollViewer.SetHorizontalScrollBarVisibility(files, ScrollBarVisibility.Disabled);
            form.Add(foldersOnly ? "子文件夹（双击进入）" : "文件与文件夹", files);
            var name = foldersOnly ? null : form.Text("文件名或完整路径", suggestedName);
            var format = foldersOnly ? null : form.Choice("文件类型", formats, formats[0].Id, null);

            string folder = Directory.Exists(initialFolder) ? initialFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!Directory.Exists(folder)) folder = AppDomain.CurrentDomain.BaseDirectory;
            Func<Format> currentFormat = () => format == null ? null : Context.Find(formats, Form.Selected(format));
            Action<string> navigate = address =>
            {
                string next = Path.GetFullPath(Environment.ExpandEnvironmentVariables(address.Trim().Trim('"')));
                if (!Directory.Exists(next)) throw new InvalidOperationException("找不到这个文件夹。");
                var type = currentFormat();
                var entries = new DirectoryInfo(next).EnumerateFileSystemInfos()
                    .Where(item => (item.Attributes & FileAttributes.Hidden) == 0)
                    .Select(item => new FileRow { Path = item.FullName, IsFolder = item is DirectoryInfo,
                        Label = (item is DirectoryInfo ? "▸  " : "     ") + item.Name })
                    .Where(item => item.IsFolder || (type != null && type.Accepts(item.Path)))
                    .OrderByDescending(item => item.IsFolder).ThenBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                files.ItemsSource = entries;
                folder = next;
                folderInput.Text = next;
                form.Status("");
            };
            Action<Action> safely = action =>
            {
                try { action(); }
                catch (Exception error) { form.Status(error.Message, true); }
            };
            navigation.Children.Add(form.InlineAction("前往", () => navigate(folderInput.Text)));
            navigation.Children.Add(form.InlineAction("上一级", () =>
            {
                var parent = Directory.GetParent(folder);
                if (parent != null) navigate(parent.FullName);
            }));
            navigation.Children.Add(form.InlineAction("刷新", () => navigate(folder)));
            folderInput.KeyDown += (sender, args) =>
            {
                if (args.Key != Key.Enter) return;
                args.Handled = true;
                safely(() => navigate(folderInput.Text));
            };
            place.SelectionChanged += (sender, args) =>
            {
                string path = Form.Selected(place);
                if (path != null) safely(() => navigate(path));
            };
            if (format != null) format.SelectionChanged += (sender, args) => safely(() =>
            {
                if (saving && !String.IsNullOrWhiteSpace(name.Text) && !currentFormat().Accepts(name.Text))
                    name.Text = Path.ChangeExtension(name.Text, currentFormat().Extensions[0]);
                navigate(folder);
            });
            files.SelectionChanged += (sender, args) =>
            {
                var row = files.SelectedItem as FileRow;
                if (name != null && row != null && !row.IsFolder) name.Text = Path.GetFileName(row.Path);
            };

            string selected = null;
            Func<bool> accept = () =>
            {
                if (foldersOnly)
                {
                    string pathChoice = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
                        Form.Required(folderInput.Text, "文件夹路径").Trim('"')));
                    if (!Directory.Exists(pathChoice)) throw new InvalidOperationException("找不到这个文件夹。");
                    selected = pathChoice;
                    return true;
                }
                if (!String.Equals(folderInput.Text.Trim(), folder, StringComparison.OrdinalIgnoreCase))
                {
                    navigate(folderInput.Text);
                    return false;
                }
                var row = files.SelectedItem as FileRow;
                string entered = name.Text.Trim().Trim('"');
                if (entered.Length == 0 && row != null && row.IsFolder)
                {
                    navigate(row.Path);
                    return false;
                }
                string path = Path.GetFullPath(Path.Combine(folder, Form.Required(entered, "文件名")));
                if (Directory.Exists(path))
                {
                    navigate(path);
                    return false;
                }
                if (saving && !Path.HasExtension(path))
                    path += currentFormat().Extensions[0];
                if (!formats.Any(type => type.Accepts(path)))
                    throw new InvalidOperationException("请选择符合文件类型的文件。");
                if (saving)
                {
                    if (!Directory.Exists(Path.GetDirectoryName(path)))
                        throw new InvalidOperationException("保存位置的文件夹不存在。");
                    if (File.Exists(path) && !Form.Message(form.Window, "覆盖文件",
                        "用新文件替换「" + Path.GetFileName(path) + "」？", confirm: true))
                        return false;
                }
                else if (!File.Exists(path))
                    throw new FileNotFoundException("找不到这个文件。");
                selected = path;
                return true;
            };
            Action submit = () => safely(() => { if (accept()) form.Window.DialogResult = true; });
            files.MouseDoubleClick += (sender, args) =>
            {
                if (!(args.OriginalSource is DependencyObject)) return;
                var container = ItemsControl.ContainerFromElement(files, (DependencyObject)args.OriginalSource);
                if (container == null) return;
                var row = files.SelectedItem as FileRow;
                if (row == null) return;
                if (row.IsFolder) safely(() => navigate(row.Path));
                else submit();
            };
            if (name != null) name.KeyDown += (sender, args) =>
            {
                if (args.Key != Key.Enter) return;
                args.Handled = true;
                submit();
            };
            folderInput.Text = folder;
            safely(() => navigate(folder));
            return form.Show(accept) ? selected : null;
        }

        private static void AddPlace(List<Item> places, string name, string path)
        {
            if (!String.IsNullOrWhiteSpace(path)) places.Add(new Item { Id = path, Name = name });
        }

        private static List<Format> ParseFormats(string filter)
        {
            var parts = filter.Split('|');
            var result = new List<Format>();
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                result.Add(new Format { Name = parts[i],
                    Extensions = parts[i + 1].Split(';').Select(pattern => pattern.TrimStart('*')).ToArray() });
            }
            return result;
        }
    }
}
