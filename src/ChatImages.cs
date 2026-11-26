using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WorldAI
{
    internal static class ChatImages
    {
        private const int MaxBytes = 1500000;
        private const int MaxSide = 1920;

        internal static ChatBackdrop Normalize(ChatBackdrop background)
        {
            if (background == null || String.IsNullOrEmpty(background.Image) ||
                background.Image.Length > (MaxBytes + 2) / 3 * 4 ||
                !background.Image.StartsWith("/9j/", StringComparison.Ordinal)) return null;
            background.Shade = Math.Max(0, Math.Min(90, background.Shade));
            return background;
        }

        internal static ImageSource Decode(ChatBackdrop background)
        {
            if (Normalize(background) == null) return null;
            try
            {
                using (var stream = new MemoryStream(Convert.FromBase64String(background.Image)))
                {
                    var source = ReadImage(stream);
                    source.Freeze();
                    return source;
                }
            }
            catch (Exception error) { if (!IsInvalidImage(error)) throw; return null; }
        }

        private static bool IsInvalidImage(Exception error)
        {
            return error is FormatException || error is IOException || error is ArgumentException ||
                error is NotSupportedException || error is InvalidOperationException || error is InvalidDataException;
        }

        internal static string Import(string path)
        {
            if (new FileInfo(path).Length > 24 * 1024 * 1024)
                throw new InvalidDataException("请选择不超过 24 MB 的背景图片。");
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var source = ReadImage(stream);
                    foreach (int quality in new[] { 86, 72, 56 })
                    {
                        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
                        encoder.Frames.Add(BitmapFrame.Create(source));
                        using (var output = new MemoryStream())
                        {
                            encoder.Save(output);
                            if (output.Length <= MaxBytes) return Convert.ToBase64String(output.ToArray());
                        }
                    }
                }
            }
            catch (Exception error)
            {
                if (!IsInvalidImage(error)) throw;
                throw new InvalidDataException("无法读取背景图片。请换一张 PNG 或 JPG 图片。", error);
            }
            throw new InvalidDataException("图片压缩后仍然太大，请换一张较小的图片。");
        }

        private static BitmapImage ReadImage(Stream stream)
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            int width = frame.PixelWidth;
            int height = frame.PixelHeight;
            if (width < 1 || height < 1 || (long)width * height > 40000000)
                throw new InvalidDataException("图片尺寸超出限制。");
            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (Math.Max(width, height) > MaxSide)
            {
                if (width >= height) image.DecodePixelWidth = MaxSide;
                else image.DecodePixelHeight = MaxSide;
            }
            image.EndInit();
            return image;
        }
    }

    internal sealed partial class Shell
    {
        private string lastBackground;

        private void ChangeChatBackground()
        {
            if (active == null || work != null) return;
            ChatBackdrop updated;
            if (!PickChatBackground(Window, active.ChatBackground, out updated)) return;
            var previous = active.ChatBackground;
            active.ChatBackground = updated;
            try { Save(); }
            catch { active.ChatBackground = previous; throw; }
            RenderChatBackground();
        }

        private void RenderChatBackground()
        {
            var display = UI<Image>("ChatWallpaper");
            var background = active == null ? null : active.ChatBackground;
            string data = background == null ? null : background.Image;
            if (!String.Equals(lastBackground, data, StringComparison.Ordinal))
            {
                display.Source = ChatImages.Decode(background);
                lastBackground = data;
            }
            display.Opacity = background == null ? 1 : 1 - background.Shade / 100.0;
        }

        private static string BackgroundLabel(ChatBackdrop background)
        {
            return background == null ? "跟随主题 · 修改" : "自定义图片 · 修改";
        }

        private static bool PickChatBackground(Window owner, ChatBackdrop current, out ChatBackdrop result)
        {
            var form = new Form(owner, "聊天背景");
            form.Note("背景只用于当前会话的消息区。没有选图时，背景跟随主题。");
            string data = current == null ? null : current.Image;
            int initial = current == null ? 45 : current.Shade;
            var preview = new Image { Source = ChatImages.Decode(current), Stretch = Stretch.UniformToFill };
            var panel = new Border { Child = preview, Height = 168, ClipToBounds = true, BorderThickness = new Thickness(1) }
                .UseBrush(Border.BackgroundProperty, "Base").UseBrush(Border.BorderBrushProperty, "Line");
            form.Add("预览", panel);

            var shades = new[] { 0, 20, 45, 65, 85, initial }.Distinct().OrderBy(value => value).Select(value =>
                new Item { Id = value.ToString(CultureInfo.InvariantCulture), Name = value + "%" }).ToList();
            var level = form.Choice("主题遮罩（数值越高，图片越淡）", shades, initial.ToString(CultureInfo.InvariantCulture), null);
            Action refresh = () =>
            {
                level.IsEnabled = data != null;
                preview.Opacity = 1 - Int32.Parse(Form.Selected(level), CultureInfo.InvariantCulture) / 100.0;
            };
            level.SelectionChanged += (sender, args) => refresh();
            form.Action("恢复主题背景", () =>
            {
                data = null;
                preview.Source = null;
                refresh();
            });
            form.Action("选择图片", () =>
            {
                string path = FilePicker.Open(form.Window, "图片 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg");
                if (path == null) return;
                data = ChatImages.Import(path);
                preview.Source = ChatImages.Decode(new ChatBackdrop { Image = data });
                refresh();
            });
            refresh();

            ChatBackdrop chosen = null;
            bool saved = form.Show(() =>
            {
                if (data != null)
                    chosen = new ChatBackdrop { Image = data, Shade = Int32.Parse(Form.Selected(level), CultureInfo.InvariantCulture) };
                return true;
            });
            result = chosen;
            return saved;
        }
    }
}
