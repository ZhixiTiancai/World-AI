using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace WorldAI
{
    internal static class WorkFiles
    {
        internal static string UserFolder(string category)
        {
            return Path.Combine(PortablePaths.Files, category);
        }

        internal static string Available(string path)
        {
            try { Directory.CreateDirectory(path); return path; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        internal static void Open(string path)
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
    }

    internal sealed partial class Shell
    {
        private string BackupFolder
        {
            get { return String.IsNullOrWhiteSpace(world.Settings.BackupDirectory) ? WorkFiles.UserFolder("备份") : world.Settings.BackupDirectory; }
        }

        private void EditBackupFolder()
        {
            var form = new Form(Window, "自定义备份位置", width: 650);
            form.Note("今后的整库备份会默认保存到这里。已有备份保留原位置。");
            var path = form.Text("备份文件夹", BackupFolder);
            form.Action("选择文件夹", () =>
            {
                string choice = FilePicker.ChooseFolder(form.Window, path.Text.Trim());
                if (choice != null) path.Text = choice;
            });
            form.Action("恢复默认路径", () => path.Text = WorkFiles.UserFolder("备份"));
            if (form.Show(() =>
            {
                string value = Path.GetFullPath(Environment.ExpandEnvironmentVariables(Form.Required(path.Text, "备份文件夹").Trim('"')));
                Directory.CreateDirectory(value);
                string previous = world.Settings.BackupDirectory;
                world.Settings.BackupDirectory = String.Equals(value, WorkFiles.UserFolder("备份"), StringComparison.OrdinalIgnoreCase) ? null : value;
                try { Save(); }
                catch { world.Settings.BackupDirectory = previous; throw; }
                return true;
            })) RenderLibrary();
        }

        private void ShowFolders()
        {
            FolderRow("角色卡", WorkFiles.UserFolder("角色卡"), "PNG 或 JSON 角色卡的导入与导出");
            FolderRow("世界书", WorkFiles.UserFolder("世界书"), "独立世界书 JSON");
            FolderRow("聊天导出", WorkFiles.UserFolder("聊天记录"), "保存为 TXT 或 JSON 的聊天记录");
            FolderRow("背景图", WorkFiles.UserFolder("背景图"), "选择会话背景时的默认位置");
            FolderRow("备份", BackupFolder, "用于迁移和恢复的整库备份");
            FolderRow("应用数据", Store.Folder, "程序管理的当前数据与更新缓存");
            FolderRow("程序目录", AppDomain.CurrentDomain.BaseDirectory, "WorldAI.exe 所在位置");
        }

        private void FolderRow(string title, string path, string help)
        {
            Row(title, help + "\n" + path, null, Button("打开文件夹", () => WorkFiles.Open(path)));
        }
    }
}
