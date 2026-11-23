using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;

namespace WorldAI
{
    internal sealed class Palette
    {
        internal readonly string Id;
        internal readonly string Name;
        internal readonly string Swatch;
        internal readonly string[] Colors;

        internal Palette(string id, string name, string swatch, params string[] colors)
        {
            Id = id;
            Name = name;
            Swatch = swatch;
            Colors = colors;
        }
    }

    internal static class Theme
    {
        private static readonly string[] Keys = { "Base", "Sidebar", "Panel", "Card", "Input", "Button",
            "Line", "Selection", "Text", "Muted", "Accent", "AccentText", "Danger" };
        internal static readonly Palette[] Choices = {
            new Palette("purple", "雾紫", "#AD9DCE", "#211A2D", "#292136", "#30273C", "#342B42", "#211A2C", "#3B314A",
                "#55475F", "#4A3E59", "#F4EFF9", "#C1B6D1", "#BFA6D6", "#2D203C", "#F0A29F"),
            new Palette("red", "柔红", "#C59298", "#2D1D22", "#35242A", "#3D2B31", "#423036", "#2B1D22", "#4A343B",
                "#65474F", "#574049", "#F8EFF2", "#CCB5BF", "#D5A1AE", "#352028", "#F4ACA4"),
            new Palette("green", "灰绿", "#91B6A4", "#192B24", "#20352B", "#263D32", "#2C4337", "#192B23", "#314B3E",
                "#476653", "#39564A", "#EDF2F0", "#ACBFB5", "#9BC2AE", "#182820", "#F0A29F"),
            new Palette("yellow", "浅金", "#C7B585", "#2C281D", "#353024", "#3F392B", "#453E2F", "#2C271C", "#4D4634",
                "#6A6046", "#554D36", "#F7F4E9", "#C9C2AA", "#D7C58B", "#302A17", "#F0A29F"),
            new Palette("blue", "雾蓝", "#91ABC8", "#1B2533", "#222E3D", "#293748", "#2E3C4E", "#1B2635", "#34475B",
                "#4C6177", "#3B4F67", "#EDF2FA", "#B2C2D4", "#9EBBDB", "#182B40", "#F0A29F"),
            new Palette("black", "墨黑", "#454A54", "#101113", "#17191C", "#1E2125", "#24272C", "#131518", "#30343A",
                "#484D55", "#383D46", "#F4F4F6", "#BABAC5", "#CCD0DB", "#202026", "#F0A29F"),
            new Palette("white", "月白", "#D2D5DC", "#EEF0F3", "#E1E5EB", "#F9FAFC", "#FFFFFF", "#EDF0F4", "#E2E6EC",
                "#C5CCD6", "#D4DCE6", "#26303C", "#526175", "#426677", "#FFFFFF", "#A03345")
        };

        internal static void Initialize(Application app)
        {
            Apply("green");
            using (var stream = typeof(Theme).Assembly.GetManifestResourceStream("WorldAI.Theme.xaml"))
                app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        }

        internal static Palette Pick(string id)
        {
            return Choices.FirstOrDefault(choice => choice.Id == id) ?? Choices.First(choice => choice.Id == "green");
        }

        internal static void Apply(string id)
        {
            var palette = Pick(id);
            var resources = Application.Current.Resources;
            for (int i = 0; i < Keys.Length; i++)
            {
                string key = "Brush." + Keys[i];
                var color = (Color)ColorConverter.ConvertFromString(palette.Colors[i]);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                resources[key] = brush;
            }
        }

        internal static T UseBrush<T>(this T element, DependencyProperty property, string name) where T : FrameworkElement
        {
            element.SetResourceReference(property, "Brush." + name);
            return element;
        }

        internal static SolidColorBrush Swatch(Palette palette)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.Swatch));
        }
    }

    internal static class WindowFrame
    {
        internal static void Attach(Window window, UIElement content, bool main)
        {
            window.WindowStyle = WindowStyle.None;
            window.UseBrush(Control.BackgroundProperty, "Base");
            window.UseBrush(Control.ForegroundProperty, "Text");
            window.UseLayoutRounding = true;
            var chrome = new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = ResizeBorder(window), CornerRadius = new CornerRadius(0) };
            WindowChrome.SetWindowChrome(window, chrome);
            window.Loaded += (sender, args) => chrome.ResizeBorderThickness = ResizeBorder(window);

            var panel = new DockPanel();
            var header = new DockPanel { Height = main ? 38 : 36 }.UseBrush(Panel.BackgroundProperty, "Sidebar");
            DockPanel.SetDock(header, Dock.Top);
            var commands = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0) };
            DockPanel.SetDock(commands, Dock.Right);
            header.Children.Add(commands);

            if (main)
            {
                commands.Children.Add(WindowButton("–", "最小化", () => window.WindowState = WindowState.Minimized));
                var maximize = WindowButton("□", "最大化", () => ToggleMaximize(window));
                commands.Children.Add(maximize);
                window.StateChanged += (sender, args) =>
                {
                    bool maximized = window.WindowState == WindowState.Maximized;
                    maximize.Content = maximized ? "❐" : "□";
                    maximize.ToolTip = maximized ? "还原窗口" : "最大化";
                };
            }
            commands.Children.Add(WindowButton("×", "关闭", () => window.Close(), true));

            var caption = new Border { Background = Brushes.Transparent };
            var title = new TextBlock { Text = main ? "WorldAI" : window.Title, FontSize = 12,
                Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis }.UseBrush(TextBlock.ForegroundProperty, "Muted");
            caption.Child = title;
            caption.MouseLeftButtonDown += (sender, args) =>
            {
                if (main && args.ClickCount == 2) { ToggleMaximize(window); return; }
                if (args.LeftButton == MouseButtonState.Pressed && window.WindowState == WindowState.Normal)
                    window.DragMove();
            };
            header.Children.Add(caption);
            panel.Children.Add(header);
            var divider = new Border { Height = 1 }.UseBrush(Border.BackgroundProperty, "Line");
            DockPanel.SetDock(divider, Dock.Top);
            panel.Children.Add(divider);
            panel.Children.Add(content);
            window.Content = new Border { BorderThickness = new Thickness(1), Child = panel }
                .UseBrush(Border.BorderBrushProperty, "Line").UseBrush(Border.BackgroundProperty, main ? "Base" : "Panel");
        }

        private static Thickness ResizeBorder(Window window)
        {
            return new Thickness(window.ResizeMode == ResizeMode.CanResize || window.ResizeMode == ResizeMode.CanResizeWithGrip ? 6 : 0);
        }

        private static void ToggleMaximize(Window window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private static Button WindowButton(string icon, string label, Action action, bool close = false)
        {
            var button = new Button { Style = (Style)Application.Current.FindResource(close ? "ChromeClose" : "ChromeButton"),
                Content = icon, ToolTip = label };
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            button.Click += (sender, args) => action();
            return button;
        }
    }
}
