using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WorldAI
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            var app = new Application();
            Theme.Initialize(app);
            if (args.Length == 2 && args[0] == "--apply-update")
            {
                try { Updates.Install(args[1]); }
                catch (Exception error) { Form.Message(null, "更新未完成", error.Message, error: true); }
                return;
            }
            using (var mutex = new Mutex(false, @"Local\WorldAI.Desktop.v1"))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    Form.Message(null, "WorldAI", "WorldAI 已经打开。");
                    return;
                }
                try
                {
                    var shell = new Shell();
                    app.Run(shell.Window);
                }
                catch (Exception error)
                {
                    Form.Message(null, "WorldAI 无法启动", error.Message, error: true);
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }

    internal sealed partial class Shell
    {
        internal readonly Window Window;
        private World world;
        private string page;
        private Session active;
        private CancellationTokenSource work;
        private bool closing;
        private bool rebuildingSessions;
        private readonly Dictionary<Session, string> drafts = new Dictionary<Session, string>();

        private static readonly string[] Pages = { "Chat", "Characters", "Books", "Profiles", "Presets", "Models", "Preferences", "Data" };

        internal Shell()
        {
            using (var stream = typeof(Shell).Assembly.GetManifestResourceStream("WorldAI.Shell.xaml"))
                Window = (Window)XamlReader.Load(stream);

            try { world = Store.Load(); }
            catch (Exception error)
            {
                throw new InvalidOperationException("无法读取本地数据。文件仍在原位，可以从备份恢复。\n\n" +
                    Store.DataPath + "\n\n" + error.Message);
            }
            Theme.Apply(world.Settings.ThemeId);
            var content = (UIElement)Window.Content;
            Window.Content = null;
            WindowFrame.Attach(Window, content, true);

            foreach (string key in Pages)
            {
                string selected = key;
                On("Nav" + key, () => ShowPage(selected));
            }
            On("NewChat", () => EditSession(null));
            On("EmptyNew", () => EditSession(null));
            On("ChatSettings", () => EditSession(active));
            On("ChatBackground", ChangeChatBackground);
            On("ChatExport", ExportChat);
            On("ManageMemory", ManageMemory);
            UI<Button>("Send").Click += async (sender, args) => await Send(false);
            UI<Button>("Retry").Click += async (sender, args) => await Send(true);
            UI<Button>("Stop").Click += (sender, args) => { if (work != null) work.Cancel(); };
            UI<TextBox>("Composer").TextChanged += (sender, args) =>
            {
                if (active != null) drafts[active] = UI<TextBox>("Composer").Text;
            };
            UI<TextBox>("Composer").PreviewKeyDown += async (sender, args) =>
            {
                if (args.Key == System.Windows.Input.Key.Enter &&
                    System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
                {
                    args.Handled = true;
                    if (active != null && work == null) await Send(false);
                }
            };

            UI<ListBox>("Sessions").SelectionChanged += (sender, args) =>
            {
                if (rebuildingSessions) return;
                Guard(() => Select(SelectedTag<Session>(UI<ListBox>("Sessions"))));
            };

            Window.Closing += (sender, args) =>
            {
                if (work == null) return;
                args.Cancel = true;
                closing = true;
                work.Cancel();
            };
            Window.Topmost = world.Settings.AlwaysOnTop;
            UI<TextBlock>("BuildLabel").Text = "WorldAI " + Updates.Current;
            Window.Loaded += (sender, args) => CheckUpdatesOnStartup();
            ShowPage("Chat");
        }

        private T UI<T>(string name) where T : FrameworkElement
        {
            return (T)Window.FindName(name);
        }

        private static T SelectedTag<T>(ListBox list) where T : class
        {
            var item = list.SelectedItem as ListBoxItem;
            return item == null ? null : item.Tag as T;
        }

        private void On(string button, Action run)
        {
            UI<Button>(button).Click += (sender, args) => Guard(run);
        }

        private void Guard(Action run)
        {
            try { run(); }
            catch (Exception error) { ShowError(error.Message); }
        }

        private void ShowError(string message)
        {
            Form.Message(Form.CurrentOwner(Window), "操作未完成", message, error: true);
        }

        private void Info(string message)
        {
            Form.Message(Form.CurrentOwner(Window), "WorldAI", message);
        }

        private bool Confirm(string message)
        {
            return Form.Message(Form.CurrentOwner(Window), "确认操作", message, confirm: true);
        }

        private void ShowPage(string key)
        {
            page = key;
            foreach (string route in Pages)
            {
                var nav = UI<Button>("Nav" + route);
                nav.Tag = route == key ? "Selected" : null;
            }
            UI<Grid>("ChatPage").Visibility = key == "Chat" ? Visibility.Visible : Visibility.Collapsed;
            UI<ScrollViewer>("LibraryPage").Visibility = key == "Chat" ? Visibility.Collapsed : Visibility.Visible;
            if (key == "Chat") RefreshSessions(active);
            else
            {
                RenderLibrary();
                UI<ScrollViewer>("LibraryPage").ScrollToTop();
            }
        }

        private void Save()
        {
            try { Store.Save(world); }
            catch (Exception error)
            {
                throw new IOException("未能写入本地数据。请检查磁盘空间与目录权限。\n\n" + Store.DataPath, error);
            }
        }

        private void SetBusy(bool busy)
        {
            foreach (string key in Pages) UI<Button>("Nav" + key).IsEnabled = !busy;
            foreach (string key in new[] { "NewChat", "EmptyNew", "ChatSettings", "ChatBackground", "ChatExport", "ManageMemory", "Send", "Retry" })
                UI<Button>(key).IsEnabled = !busy;
            UI<ListBox>("Sessions").IsEnabled = !busy;
            UI<Button>("Stop").Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (!busy) UpdateRetry();
        }

        private void FinishWork()
        {
            work.Dispose();
            work = null;
            SetBusy(false);
            if (closing) Window.Close();
        }

        private TextBlock Label(string text, double size = 13, string color = "Text")
        {
            return new TextBlock { Text = text ?? "", FontSize = size, TextWrapping = TextWrapping.Wrap }
                .UseBrush(TextBlock.ForegroundProperty, color);
        }

        private Button Button(string title, Action action, bool primary = false, bool enabled = true)
        {
            var button = new Button { Content = title, IsEnabled = enabled,
                Style = (Style)Window.FindResource(primary ? "Primary" : "ButtonBase") };
            button.Click += (sender, args) => Guard(action);
            return button;
        }

        private static ImageSource Avatar(string data)
        {
            if (String.IsNullOrWhiteSpace(data)) return null;
            try
            {
                using (var stream = new MemoryStream(Convert.FromBase64String(data)))
                {
                    var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    image.Freeze();
                    return image;
                }
            }
            catch (Exception) { return null; }
        }

        private string OpenFile(string filter)
        {
            return FilePicker.Open(Form.CurrentOwner(Window), filter);
        }

        private string ExportFile(string filter, string extension, string filename)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string suggestion = new string(filename.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            string path = FilePicker.Save(Form.CurrentOwner(Window), filter, extension, suggestion);
            if (path == null) return null;
            if (String.Equals(path, Store.DataPath, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(path, Store.DataPath + ".previous", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请选择一个独立的导出文件。");
            return path;
        }

        private static string Preview(string value, int limit)
        {
            value = (value ?? "").Trim().Replace("\r", "").Replace('\n', ' ');
            return value.Length > limit ? value.Substring(0, limit) + "…" : value;
        }
    }
}
