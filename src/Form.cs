using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WorldAI
{
    internal sealed class Form
    {
        private readonly Window window;
        private readonly StackPanel fields;
        private readonly TextBlock status;
        private readonly Button save;
        private readonly Button cancel;
        private readonly WrapPanel actions;
        private readonly DockPanel layout;
        private readonly ScrollViewer fieldScroll;
        private readonly ProgressBar progress;
        private Dictionary<Button, bool> busyButtons;

        internal Form(Window owner, string title, string accept = "保存", double width = 565)
        {
            window = new Window { WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Width = Math.Min(width, SystemParameters.WorkArea.Width - 30),
                SizeToContent = SizeToContent.Height, MaxHeight = Math.Min(SystemParameters.WorkArea.Height - 30, 850),
                ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Title = title,
                FontFamily = owner == null ? new FontFamily("Segoe UI, Microsoft YaHei UI") : owner.FontFamily,
                FontSize = owner == null ? 13 : owner.FontSize };
            if (owner != null) window.Owner = owner;
            layout = new DockPanel { Margin = new Thickness(21) };
            var heading = new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(heading, Dock.Top);
            layout.Children.Add(heading);

            var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            status = new TextBlock { TextWrapping = TextWrapping.Wrap }.UseBrush(TextBlock.ForegroundProperty, "Danger");
            statusScroll = new ScrollViewer { Content = status, MaxHeight = 92, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed };
            footer.Children.Add(statusScroll);
            progress = new ProgressBar { Height = 4, Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 10) };
            footer.Children.Add(progress);
            actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            save = Control(accept, true);
            cancel = Control("取消");
            cancel.IsCancel = true;
            actions.Children.Add(cancel);
            actions.Children.Add(save);
            footer.Children.Add(actions);
            DockPanel.SetDock(footer, Dock.Bottom);
            layout.Children.Add(footer);

            fields = new StackPanel();
            fieldScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = fields,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            layout.Children.Add(fieldScroll);
            WindowFrame.Attach(window, layout, false);
        }

        private readonly ScrollViewer statusScroll;
        internal Window Window { get { return window; } }
        internal Button AcceptButton { get { return save; } }

        internal static Window CurrentOwner(Window fallback)
        {
            return Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive) ?? fallback;
        }

        internal static bool Message(Window owner, string title, string text, bool confirm = false, bool error = false)
        {
            var form = new Form(owner, title, confirm ? "确认" : "确定");
            form.cancel.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
            form.save.IsDefault = !confirm;
            if (confirm) form.window.Loaded += (sender, args) => form.cancel.Focus();
            var body = new TextBlock { Text = text ?? "", TextWrapping = TextWrapping.Wrap, LineHeight = 21 }
                .UseBrush(TextBlock.ForegroundProperty, error ? "Danger" : "Text");
            form.Add(null, body);
            return form.Show(() => true);
        }

        private Button Control(string title, bool primary = false)
        {
            return new Button { Content = title, Style = (Style)window.FindResource(primary ? "Primary" : "ButtonBase") };
        }

        internal void Note(string text)
        {
            var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) }
                .UseBrush(TextBlock.ForegroundProperty, "Muted");
            fields.Children.Add(label);
        }

        internal void Section(string title)
        {
            fields.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold,
                FontSize = 16, Margin = new Thickness(0, 10, 0, 9) });
        }

        private void Field(string title, FrameworkElement control)
        {
            if (!String.IsNullOrEmpty(title))
                fields.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            control.Margin = new Thickness(0, 0, 5, 11);
            fields.Children.Add(control);
        }

        internal TextBox Text(string title, string value, double height = 0)
        {
            var box = new TextBox { Text = value ?? "", AcceptsReturn = height > 0,
                VerticalContentAlignment = height > 0 ? VerticalAlignment.Top : VerticalAlignment.Center };
            if (height > 0)
            {
                box.Height = height;
                box.TextWrapping = TextWrapping.Wrap;
                box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            Field(title, box);
            return box;
        }

        internal TextBox FullText(string value)
        {
            window.ResizeMode = ResizeMode.CanResizeWithGrip;
            window.SizeToContent = SizeToContent.Manual;
            window.Height = Math.Min(720, window.MaxHeight);
            window.MinWidth = Math.Min(420, window.Width);
            window.MinHeight = Math.Min(320, window.MaxHeight);
            var box = new TextBox { Text = value ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            layout.Children.Remove(fieldScroll);
            layout.Children.Add(box);
            window.Loaded += (sender, args) => box.Focus();
            return box;
        }

        internal PasswordBox Password(string title)
        {
            var box = new PasswordBox { Padding = new Thickness(7, 5, 7, 5) };
            Field(title, box);
            return box;
        }

        internal CheckBox Check(string title, bool value)
        {
            var control = new CheckBox { Content = title, IsChecked = value };
            Field(null, control);
            return control;
        }

        internal ComboBox Choice<T>(string title, IEnumerable<T> values, string id, string empty) where T : Item
        {
            var control = new ComboBox();
            if (empty != null) control.Items.Add(new ComboBoxItem { Content = empty });
            foreach (var value in values)
                control.Items.Add(new ComboBoxItem { Content = value.Name, Tag = value });
            for (int i = 0; i < control.Items.Count; i++)
            {
                var item = ((ComboBoxItem)control.Items[i]).Tag as Item;
                if (item != null && item.Id == id) { control.SelectedIndex = i; break; }
            }
            if (control.SelectedIndex < 0 && control.Items.Count > 0) control.SelectedIndex = 0;
            Field(title, control);
            return control;
        }

        internal static string Selected(ComboBox control)
        {
            var option = control.SelectedItem as ComboBoxItem;
            var item = option == null ? null : option.Tag as Item;
            return item == null ? null : item.Id;
        }

        internal Button Action(string title, Action action = null)
        {
            var button = InlineAction(title, action);
            actions.Children.Insert(0, button);
            return button;
        }

        internal Button InlineAction(string title, Action action = null)
        {
            var button = Control(title);
            if (action != null)
                button.Click += (sender, args) =>
                {
                    try { action(); }
                    catch (Exception error) { Status(error.Message, true); }
                };
            return button;
        }

        internal void Add(string title, FrameworkElement control)
        {
            Field(title, control);
        }

        internal void Status(string message, bool error = false)
        {
            status.Text = message ?? "";
            status.UseBrush(TextBlock.ForegroundProperty, error ? "Danger" : "Muted");
            statusScroll.Visibility = String.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
            statusScroll.ScrollToTop();
        }

        internal void Busy(bool busy)
        {
            if (busy == (busyButtons != null)) return;
            fields.IsEnabled = !busy;
            progress.IsIndeterminate = busy;
            progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (busy)
            {
                busyButtons = actions.Children.OfType<Button>().Where(button => button != cancel)
                    .ToDictionary(button => button, button => button.IsEnabled);
                foreach (var button in busyButtons.Keys) button.IsEnabled = false;
            }
            else
            {
                foreach (var pair in busyButtons) pair.Key.IsEnabled = pair.Value;
                busyButtons = null;
            }
        }

        internal bool Show(Func<bool> apply)
        {
            save.Click += (sender, args) =>
            {
                try
                {
                    if (apply()) window.DialogResult = true;
                }
                catch (Exception error) { Status(error.Message, true); }
            };
            return window.ShowDialog() == true;
        }

        internal static string Required(string value, string label)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("请填写" + label + "。");
            return value.Trim();
        }
    }
}
