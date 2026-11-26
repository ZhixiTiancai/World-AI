using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace WorldAI
{
    internal static class InstallerBundle
    {
        private static readonly byte[] Marker = Encoding.ASCII.GetBytes("WorldAI.Bundle.v1");
        private const int MaxPayload = 64 * 1024 * 1024;

        internal static Stream Open(string file)
        {
            var stream = File.OpenRead(file);
            if (stream.ReadByte() == 'P' && stream.ReadByte() == 'K')
            {
                stream.Position = 0;
                return stream;
            }
            using (stream)
            using (var reader = new BinaryReader(stream))
            {
                int footer = sizeof(long) + Marker.Length;
                if (stream.Length <= footer) throw new InvalidDataException("安装包不完整，请重新下载。");
                stream.Position = stream.Length - footer;
                long size = reader.ReadInt64();
                if (size < 1024 || size > MaxPayload || size > stream.Length - footer ||
                    !reader.ReadBytes(Marker.Length).SequenceEqual(Marker))
                    throw new InvalidDataException("无法识别这个 WorldAI 安装包。");
                stream.Position = stream.Length - footer - size;
                return new MemoryStream(reader.ReadBytes((int)size), false);
            }
        }

        internal static void Extract(ZipArchive package, string name, string target, long limit)
        {
            var entry = package.GetEntry(name);
            if (entry == null || entry.Length == 0 || entry.Length > limit)
                throw new InvalidDataException("安装包中缺少有效的 " + name + "。");
            using (var source = entry.Open())
            using (var output = new FileStream(target, FileMode.CreateNew))
                source.CopyTo(output);
        }
    }

    internal static class SetupProgram
    {
        [STAThread]
        private static void Main()
        {
            var app = new Application();
            Theme.Initialize(app);
            try { Theme.Apply(Store.Load().Settings.ThemeId); } catch (Exception) { }
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WorldAI");
                var form = new Form(null, "安装 WorldAI " + Updates.Current, "安装", 650);
                form.Window.ShowInTaskbar = true;
                form.Note("选择安装位置。安装完成后可从桌面快捷方式打开。");
                var path = form.Text("安装文件夹", root);
                var shortcut = form.Check("创建或更新桌面快捷方式", true);
                var launch = form.Check("安装后打开 WorldAI", true);
                form.Action("浏览文件夹", () =>
                {
                    string choice = FilePicker.ChooseFolder(form.Window, Directory.Exists(path.Text) ? path.Text : Path.GetDirectoryName(root));
                    if (choice != null) path.Text = choice;
                });
                string program = null;
                string warning = null;
                if (!form.Show(() =>
                {
                    string selected = Environment.ExpandEnvironmentVariables(Form.Required(path.Text, "安装文件夹").Trim('"'));
                    if (!Path.IsPathRooted(selected)) throw new InvalidOperationException("请输入完整的安装文件夹路径。");
                    string folder = Path.GetFullPath(selected);
                    string target = Path.Combine(folder, "WorldAI.exe");
                    if (File.Exists(target) && !Form.Message(form.Window, "替换现有程序",
                        "此处已有 WorldAI.exe。用这个安装包替换程序文件？", confirm: true)) return false;
                    program = Install(folder);
                    if (shortcut.IsChecked == true)
                    {
                        try { CreateShortcut(program); }
                        catch (Exception error) { warning = "\n桌面快捷方式未能创建：" + error.Message; }
                    }
                    return true;
                })) return;
                if (launch.IsChecked == true)
                    Process.Start(new ProcessStartInfo { FileName = program, WorkingDirectory = Path.GetDirectoryName(program), UseShellExecute = false });
                if (launch.IsChecked != true || warning != null)
                    Form.Message(null, "WorldAI 已安装", program + (warning ?? ""));
            }
            catch (Exception error) { Form.Message(null, "WorldAI 安装未完成", error.Message, error: true); }
        }

        private static string Install(string path)
        {
            using (var mutex = new Mutex(false, @"Local\WorldAI.Desktop.v1"))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new InvalidOperationException("请先退出正在运行的 WorldAI，再安装或覆盖。");
                try { return InstallFiles(path); }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static string InstallFiles(string path)
        {
            Directory.CreateDirectory(path);
            string destination = Path.Combine(path, "WorldAI.exe");
            string scratch = destination + "." + Guid.NewGuid().ToString("N") + ".install";
            string configuration = destination + ".config";
            string configScratch = scratch + ".config";
            using (var payload = InstallerBundle.Open(Assembly.GetExecutingAssembly().Location))
            using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                try
                {
                    InstallerBundle.Extract(archive, "WorldAI.exe", scratch, 64L * 1024 * 1024);
                    if (!File.Exists(configuration))
                    {
                        InstallerBundle.Extract(archive, "WorldAI.exe.config", configScratch, 32768);
                        File.Move(configScratch, configuration);
                    }
                    if (File.Exists(destination)) File.Replace(scratch, destination, destination + ".update-backup");
                    else File.Move(scratch, destination);
                }
                finally
                {
                    if (File.Exists(scratch)) File.Delete(scratch);
                    if (File.Exists(configScratch)) File.Delete(configScratch);
                }
            }
            return destination;
        }

        private static void CreateShortcut(string target)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            Directory.CreateDirectory(desktop);
            Type shellType = Type.GetTypeFromProgID("WScript.Shell", true);
            object shell = Activator.CreateInstance(shellType);
            object shortcut = null;
            try
            {
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                    new object[] { Path.Combine(desktop, "WorldAI.lnk") });
                Type type = shortcut.GetType();
                type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
                type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(target) });
                type.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { target + ",0" });
                type.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "WorldAI · 角色与故事" });
                type.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
