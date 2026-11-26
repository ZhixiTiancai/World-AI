using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WorldAI
{
    internal sealed partial class Shell
    {
        private void RenderLibrary()
        {
            var actions = UI<StackPanel>("LibraryActions");
            var items = UI<StackPanel>("LibraryItems");
            actions.Children.Clear();
            items.Children.Clear();
            var title = UI<TextBlock>("LibraryTitle");
            var hint = UI<TextBlock>("LibraryHint");
            switch (page)
            {
                case "Characters":
                    title.Text = "角色卡";
                    hint.Text = "创建自己的角色，或把 PNG / JSON 角色卡拖到此页导入。";
                    actions.Children.Add(Button("＋ 新建角色", () => EditCharacter(null), true));
                    actions.Children.Add(Button("导入角色卡", ImportCharacter));
                    foreach (var role in world.Characters)
                    {
                        Row(role.Name, Preview(role.Background, 145), Avatar(role.Avatar),
                            Button("编辑", () => EditCharacter(role)),
                            Button("复制", () => DuplicateRole(role)),
                            Button("导出", () => ExportCharacter(role)),
                            Button("删除", () => RemoveCharacter(role)));
                    }
                    break;
                case "Books":
                    title.Text = "世界书";
                    hint.Text = "可直接拖入世界书 JSON。条目支持关键词、语义检索和常驻设定。";
                    actions.Children.Add(Button("＋ 新建世界书", () => EditLore(null), true));
                    actions.Children.Add(Button("导入 JSON", ImportLore));
                    actions.Children.Add(Button("语义检索设置", EditSemanticSettings));
                    foreach (var book in world.Books)
                        Row(book.Name, book.Entries.Count + " 个条目", null,
                            Button("编辑", () => EditLore(book)),
                            Button("导出", () => ExportLore(book)),
                            Button("删除", () => DeleteItem(world.Books, book, () =>
                            {
                                foreach (var role in world.Characters.Where(role => role.BookId == book.Id)) role.BookId = null;
                            })));
                    break;
                case "Profiles":
                    title.Text = "玩家身份";
                    var globalProfile = Context.Find(world.Profiles, world.Settings.DefaultProfileId);
                    hint.Text = globalProfile == null ? "全局身份未设置。可以把常用身份设为全局默认。"
                        : "当前全局身份：" + globalProfile.Name + "。跟随全局的会话会自动使用它。";
                    actions.Children.Add(Button("＋ 新建身份", () => EditProfile(null), true));
                    if (globalProfile != null) actions.Children.Add(Button("清除全局身份", () => SetGlobalProfile(null)));
                    foreach (var profile in world.Profiles)
                        Row(profile.Name + (profile == globalProfile ? "  ·  全局身份" : ""), Preview(profile.Description, 145), null,
                            Button(profile == globalProfile ? "当前全局" : "设为全局", () => SetGlobalProfile(profile.Id),
                                enabled: profile != globalProfile),
                            Button("编辑", () => EditProfile(profile)),
                            Button("删除", () => DeleteItem(world.Profiles, profile, () =>
                            {
                                foreach (var session in world.Sessions.Where(session => session.ProfileId == profile.Id)) session.ProfileId = null;
                                if (world.Settings.DefaultProfileId == profile.Id) world.Settings.DefaultProfileId = null;
                            })));
                    break;
                case "Presets":
                    title.Text = "提示词预设";
                    hint.Text = "保存写作风格、回复长度和自定义指令。";
                    actions.Children.Add(Button("＋ 新建预设", () => EditPreset(null), true));
                    foreach (var preset in world.Presets)
                        Row(preset.Name, Preview(preset.Style, 100) + (String.IsNullOrWhiteSpace(preset.Length) ? "" : "  ·  " + preset.Length), null,
                            Button("编辑", () => EditPreset(preset)),
                            Button("删除", () => DeleteItem(world.Presets, preset, () =>
                            {
                                foreach (var session in world.Sessions.Where(session => session.PresetId == preset.Id)) session.PresetId = null;
                            })));
                    break;
                case "Models":
                    title.Text = "模型设置";
                    hint.Text = "添加 OpenAI 兼容接口。配置好后，在对话页的会话设置里选用。";
                    actions.Children.Add(Button("＋ 添加模型", () => EditModel(null), true));
                    foreach (var model in world.Models)
                    {
                        if (model.Demo) Row(model.Name, "内置的固定剧本，用来体验聊天和摘要流程。", null);
                        else Row(model.Name, model.ModelName + "  ·  " + model.Endpoint, null,
                            Button("编辑及连接测试", () => EditModel(model)),
                            Button("删除", () => DeleteItem(world.Models, model, () =>
                            {
                                foreach (var session in world.Sessions.Where(session => session.ModelId == model.Id)) session.ModelId = null;
                                if (world.Settings.DefaultModelId == model.Id) world.Settings.DefaultModelId = null;
                                if (world.Settings.SemanticApiId == model.Id) world.Settings.SemanticApiId = null;
                            })));
                    }
                    break;
                case "Preferences":
                    title.Text = "全局设置";
                    hint.Text = "管理桌面窗口和聊天默认值。每段会话可以单独覆盖历史条数和发送字数。";
                    actions.Children.Add(Button("编辑全局设置", EditPreferences, true));
                    actions.Children.Add(Button(hasUpdate ? "有新版本 · 检查更新" : "检查更新", ShowUpdates));
                    RenderThemePicker(items);
                    var preferences = world.Settings;
                    var defaultModel = Context.Find(world.Models, preferences.DefaultModelId);
                    Row("聊天默认值", "新会话模型：" + (defaultModel == null ? "自动选择" : defaultModel.Name) +
                        "  ·  最近 " + preferences.HistoryCount + " 条  ·  " +
                        (preferences.ContextCharacters == 0 ? "发送字数不设限" : "最多 " + preferences.ContextCharacters + " 字符"), null);
                    Row("屏幕显示", "每页 " + preferences.PageSize + " 条消息  ·  窗口置顶" +
                        (preferences.AlwaysOnTop ? "开启" : "关闭"), null);
                    Row("全局提示词", String.IsNullOrWhiteSpace(preferences.SystemPrompt)
                        ? "未设置" : Preview(preferences.SystemPrompt, 200), null);
                    break;
                case "Data":
                    title.Text = "备份与恢复";
                    hint.Text = "导出角色、世界书、模型设置、会话和记忆。备份不含 API 密钥。";
                    actions.Children.Add(Button("导出备份", Backup, true));
                    actions.Children.Add(Button("恢复备份", Restore));
                    FolderRow("备份文件夹", BackupFolder, "导出和恢复时的默认位置");
                    actions.Children.Add(Button("自定义备份位置", EditBackupFolder));
                    FolderRow("应用数据", Store.Folder, "聊天和设置保存在这里");
                    break;
                case "Folders":
                    title.Text = "文件目录";
                    hint.Text = "按用途打开常用文件夹。导入和导出也会默认打开对应目录。";
                    ShowFolders();
                    break;
            }
            if (items.Children.Count == 0)
            {
                var empty = Label("这里还没有内容。可以从上方新建。", 13, "Muted");
                empty.Margin = new Thickness(3, 3, 0, 0);
                items.Children.Add(empty);
            }
        }

        private void RenderThemePicker(Panel items)
        {
            var content = new StackPanel();
            var heading = Label("外观", 18);
            heading.FontWeight = FontWeights.SemiBold;
            content.Children.Add(heading);
            var caption = Label("主题颜色", 12, "Muted");
            caption.Margin = new Thickness(0, 7, 0, 12);
            content.Children.Add(caption);
            var choices = new WrapPanel();
            content.Children.Add(choices);
            var themeButtons = new Dictionary<Palette, Button>();
            Action refresh = () =>
            {
                string current = Theme.Pick(world.Settings.ThemeId).Id;
                foreach (var choice in themeButtons)
                {
                    bool selected = choice.Key.Id == current;
                    choice.Value.UseBrush(Control.BackgroundProperty, selected ? "Selection" : "Button");
                    choice.Value.UseBrush(Control.BorderBrushProperty, selected ? "Accent" : "Line");
                    var text = (TextBlock)((StackPanel)choice.Value.Content).Children[1];
                    text.Text = choice.Key.Name + (selected ? "  ✓" : "");
                }
            };
            foreach (var palette in Theme.Choices)
            {
                var theme = palette;
                var button = Button(theme.Name, () =>
                {
                    if (Theme.Pick(world.Settings.ThemeId).Id == theme.Id) return;
                    string previous = world.Settings.ThemeId;
                    world.Settings.ThemeId = theme.Id;
                    try { Save(); }
                    catch { world.Settings.ThemeId = previous; throw; }
                    Theme.Apply(theme.Id);
                    refresh();
                });
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new Ellipse { Width = 14, Height = 14, Fill = Theme.Swatch(theme),
                    StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) }
                    .UseBrush(Shape.StrokeProperty, "Line"));
                row.Children.Add(new TextBlock { Text = theme.Name, VerticalAlignment = VerticalAlignment.Center });
                button.Content = row;
                button.MinWidth = 99;
                button.Margin = new Thickness(0, 0, 8, 7);
                System.Windows.Automation.AutomationProperties.SetName(button, theme.Name);
                choices.Children.Add(button);
                themeButtons.Add(theme, button);
            }
            refresh();
            items.Children.Add(new Border { BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13), Padding = new Thickness(20, 17, 13, 11),
                Margin = new Thickness(0, 0, 0, 12), Child = content }
                .UseBrush(Border.BackgroundProperty, "Card").UseBrush(Border.BorderBrushProperty, "Line"));
        }

        private void SetGlobalProfile(string id)
        {
            string previous = world.Settings.DefaultProfileId;
            world.Settings.DefaultProfileId = id;
            try { Save(); }
            catch { world.Settings.DefaultProfileId = previous; throw; }
            RenderLibrary();
        }

        private void Row(string title, string description, ImageSource avatar, params Button[] commands)
        {
            var content = new DockPanel { LastChildFill = true };
            if (commands.Length > 0)
            {
                var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0) };
                foreach (var button in commands) tools.Children.Add(button);
                DockPanel.SetDock(tools, Dock.Right);
                content.Children.Add(tools);
            }
            if (avatar != null)
            {
                var image = new Image { Source = avatar, Width = 42, Height = 42, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 12, 0) };
                DockPanel.SetDock(image, Dock.Left);
                content.Children.Add(image);
            }
            var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = Label(title, 16);
            name.FontWeight = FontWeights.SemiBold;
            body.Children.Add(name);
            if (!String.IsNullOrWhiteSpace(description))
            {
                var detail = Label(description, 12, "Muted");
                detail.Margin = new Thickness(0, 4, 0, 0);
                body.Children.Add(detail);
            }
            content.Children.Add(body);
            UI<StackPanel>("LibraryItems").Children.Add(new Border {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 9),
                Padding = new Thickness(17, 14, 11, 14), Child = content
            }.UseBrush(Border.BackgroundProperty, "Card").UseBrush(Border.BorderBrushProperty, "Line"));
        }

        private void Put<T>(List<T> items, T old, T value) where T : Item
        {
            int index = old == null ? -1 : items.IndexOf(old);
            if (index < 0) items.Add(value);
            else items[index] = value;
            try { Save(); }
            catch
            {
                if (index < 0) items.Remove(value);
                else items[index] = old;
                throw;
            }
        }

        private void DeleteItem<T>(List<T> items, T value, Action detach = null) where T : Item
        {
            if (!Confirm("删除「" + value.Name + "」？")) return;
            Mutate(() =>
            {
                items.Remove(value);
                if (detach != null) detach();
            });
            RenderLibrary();
        }

        private void Mutate(Action action)
        {
            var snapshot = Store.Copy(world);
            try
            {
                action();
                Save();
            }
            catch
            {
                world = snapshot;
                active = Context.Find(world.Sessions, active == null ? null : active.Id);
                drafts.Clear();
                ShowPage(page);
                throw;
            }
        }
    }
}
