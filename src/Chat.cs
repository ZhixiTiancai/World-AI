using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WorldAI
{
    internal sealed partial class Shell
    {
        private int extraVisibleMessages;

        private void RefreshSessions(Session selected)
        {
            var box = UI<ListBox>("Sessions");
            rebuildingSessions = true;
            try
            {
                box.Items.Clear();
                foreach (var session in world.Sessions.OrderByDescending(item => item.Updated))
                {
                    var role = Context.Find(world.Characters, session.CharacterId);
                    var card = new StackPanel();
                    var title = Label(session.Name);
                    title.FontWeight = FontWeights.SemiBold;
                    title.TextWrapping = TextWrapping.NoWrap;
                    title.TextTrimming = TextTrimming.CharacterEllipsis;
                    card.Children.Add(title);
                    var caption = Label(role == null ? "" : role.Name, 11, "Muted");
                    caption.Margin = new Thickness(0, 4, 0, 0);
                    card.Children.Add(caption);
                    var item = new ListBoxItem { Content = card, Tag = session };
                    box.Items.Add(item);
                    if (session == selected) box.SelectedItem = item;
                }
                if (box.SelectedIndex < 0 && box.Items.Count > 0) box.SelectedIndex = 0;
            }
            finally { rebuildingSessions = false; }
            Select(SelectedTag<Session>(box));
        }

        private void Select(Session session)
        {
            if (active == null || session == null || active.Id != session.Id)
                extraVisibleMessages = 0;
            active = session;
            string draft;
            UI<TextBox>("Composer").Text = session != null && drafts.TryGetValue(session, out draft) ? draft : "";
            UI<Grid>("ActiveChat").Visibility = session == null ? Visibility.Collapsed : Visibility.Visible;
            UI<Grid>("EmptyChat").Visibility = session == null ? Visibility.Visible : Visibility.Collapsed;
            UI<StackPanel>("ContextPanel").Visibility = session == null ? Visibility.Collapsed : Visibility.Visible;
            RenderChat();
        }

        private void EditSession(Session existing)
        {
            if (world.Characters.Count == 0)
            {
                ShowPage("Characters");
                Info("先新建或导入一张角色卡。");
                return;
            }
            var session = existing == null ? new Session() : Store.Copy(existing);
            if (existing == null)
            {
                var defaultModel = Context.Find(world.Models, world.Settings.DefaultModelId)
                    ?? world.Models.FirstOrDefault(item => !item.Demo) ?? world.Models.FirstOrDefault();
                session.ModelId = defaultModel == null ? null : defaultModel.Id;
            }
            var form = new Form(Window, existing == null ? "新建会话" : "会话设置", existing == null ? "创建" : "保存");
            var name = form.Text("会话名称", session.Name);
            var character = form.Choice("角色", world.Characters, session.CharacterId, null);
            character.IsEnabled = existing == null;
            ComboBox opening = null;
            if (existing == null)
            {
                opening = new ComboBox();
                form.Add("本次会话的开场白", opening);
                var preview = form.Text("开场白预览", "", 100);
                preview.IsReadOnly = true;
                opening.SelectionChanged += (sender, args) => preview.Text = OpeningText(opening) ?? "";
                Action loadOpenings = () =>
                {
                    opening.Items.Clear();
                    var card = Context.Find(world.Characters, Form.Selected(character));
                    if (card != null)
                    {
                        if (!String.IsNullOrWhiteSpace(card.Greeting))
                            opening.Items.Add(OpeningOption("主开场白", card.Greeting));
                        for (int i = 0; i < card.AlternateGreetings.Count; i++)
                            opening.Items.Add(OpeningOption("备选 " + (i + 1), card.AlternateGreetings[i]));
                    }
                    opening.Items.Add(new ComboBoxItem { Content = "不使用开场白" });
                    opening.SelectedIndex = 0;
                };
                character.SelectionChanged += (sender, args) => loadOpenings();
                loadOpenings();
            }
            var model = form.Choice("模型", world.Models, session.ModelId, "未设置");
            var globalPlayer = Context.Find(world.Profiles, world.Settings.DefaultProfileId);
            var players = world.Profiles.Concat(new[] { new Profile { Id = Context.NoProfile, Name = "不使用玩家身份" } });
            var profile = form.Choice("玩家身份", players, session.ProfileId,
                globalPlayer == null ? "跟随全局身份（未设置）" : "跟随全局身份 · " + globalPlayer.Name);
            var preset = form.Choice("提示词预设", world.Presets, session.PresetId, "无预设");
            form.Section("模型与上下文");
            var stream = form.Check("逐段显示模型回复（流式输出）", session.StreamReplies);
            var independent = form.Check("使用本会话独立的上下文限制",
                session.HistoryCount.HasValue || session.ContextCharacters.HasValue);
            var count = form.Text("历史条数（1–500，含最新消息）", (session.HistoryCount ?? world.Settings.HistoryCount).ToString());
            var limit = form.Text("发送字数上限（0 不限，或 1000–500000）",
                (session.ContextCharacters ?? world.Settings.ContextCharacters).ToString());
            Action toggleLimits = () => { count.IsEnabled = independent.IsChecked == true; limit.IsEnabled = count.IsEnabled; };
            independent.Checked += (sender, args) => toggleLimits();
            independent.Unchecked += (sender, args) => toggleLimits();
            toggleLimits();
            form.Note("关闭独立限制时，使用全局设置。限制只影响回复请求，历史消息仍保存在本机。");
            var prompt = form.Text("会话提示词", session.SystemPrompt, 95);
            var time = form.Check("把电脑当前时间加入提示词", session.SendTime);
            var memory = form.Check("在回复时使用剧情记忆和摘要", session.UseMemory);
            var book = form.Check("在回复时使用角色挂载的世界书", session.UseWorldBook);
            form.Section("聊天界面");
            var fontSize = form.Text("消息字号（10–32）", session.MessageFontSize.ToString());
            var speaker = form.Check("显示说话人名称", session.ShowSpeaker);
            var avatar = form.Check("在角色消息旁显示头像", session.ShowCharacterAvatar);
            var refocus = form.Check("发送后把焦点放回输入框", session.RefocusComposer);
            var wallpaper = form.InlineAction(BackgroundLabel(session.ChatBackground));
            wallpaper.Click += (sender, args) => Guard(() =>
            {
                ChatBackdrop updated;
                if (!PickChatBackground(form.Window, session.ChatBackground, out updated)) return;
                session.ChatBackground = updated;
                wallpaper.Content = BackgroundLabel(updated);
            });
            wallpaper.HorizontalAlignment = HorizontalAlignment.Left;
            form.Add("聊天背景", wallpaper);
            bool removed = false;
            if (existing != null)
                form.Action("删除会话", () =>
                {
                    if (!Confirm("删除这段会话和它的记忆？")) return;
                    int index = world.Sessions.IndexOf(existing);
                    world.Sessions.RemoveAt(index);
                    try { Save(); }
                    catch { world.Sessions.Insert(index, existing); throw; }
                    drafts.Remove(existing);
                    removed = true;
                    form.Window.Close();
                });

            if (!form.Show(() =>
            {
                session.CharacterId = Form.Selected(character);
                var role = Context.Find(world.Characters, session.CharacterId);
                if (role == null) throw new InvalidOperationException("请选择角色。");
                session.Name = String.IsNullOrWhiteSpace(name.Text) ? role.Name + "的会话" : name.Text.Trim();
                session.ModelId = Form.Selected(model);
                session.ProfileId = Form.Selected(profile);
                session.PresetId = Form.Selected(preset);
                session.HistoryCount = independent.IsChecked == true ? ReadNumber(count.Text, 1, 500, "历史条数") : (int?)null;
                session.ContextCharacters = independent.IsChecked == true ? ReadContextLimit(limit.Text) : (int?)null;
                session.SystemPrompt = prompt.Text.Trim();
                session.StreamReplies = stream.IsChecked == true;
                session.SendTime = time.IsChecked == true;
                session.UseMemory = memory.IsChecked == true;
                session.UseWorldBook = book.IsChecked == true;
                session.MessageFontSize = ReadNumber(fontSize.Text, 10, 32, "消息字号");
                session.ShowSpeaker = speaker.IsChecked == true;
                session.ShowCharacterAvatar = avatar.IsChecked == true;
                session.RefocusComposer = refocus.IsChecked == true;
                session.Updated = DateTime.UtcNow;
                if (existing == null)
                {
                    session.Messages.Clear();
                    string greeting = OpeningText(opening);
                    if (!String.IsNullOrWhiteSpace(greeting))
                        session.Messages.Add(new Message { Role = "assistant",
                            Text = Context.Expand(greeting, role, Context.Player(world, session)) });
                }
                Put(world.Sessions, existing, session);
                if (existing != null)
                {
                    string draft;
                    if (drafts.TryGetValue(existing, out draft)) drafts[session] = draft;
                    drafts.Remove(existing);
                }
                return true;
            }))
            {
                if (removed) RefreshSessions(null);
                return;
            }
            RefreshSessions(session);
            UI<TextBox>("Composer").Focus();
        }

        private ComboBoxItem OpeningOption(string label, string text)
        {
            return new ComboBoxItem { Content = label + "  ·  " + Preview(text, 44), Tag = text };
        }

        private static string OpeningText(ComboBox opening)
        {
            var option = opening.SelectedItem as ComboBoxItem;
            return option == null ? null : option.Tag as string;
        }

        private void RenderChat()
        {
            RenderChatBackground();
            if (active == null) { RenderMessages(); return; }
            var role = Context.Find(world.Characters, active.CharacterId);
            var profile = Context.Player(world, active);
            var model = Context.Find(world.Models, active.ModelId);
            var preset = Context.Find(world.Presets, active.PresetId);
            UI<TextBlock>("ChatTitle").Text = active.Name;
            UI<TextBlock>("ChatSubtitle").Text = (model == null ? "未配置模型" : model.Name) +
                (profile == null ? "" : "  ·  " + profile.Name) + (preset == null ? "" : "  ·  " + preset.Name);
            UI<TextBlock>("RoleName").Text = role == null ? "未找到角色" : role.Name;
            UI<TextBlock>("RoleInitial").Text = role == null ? "?" : StringInfoInitial(role.Name);
            UI<Image>("RoleAvatar").Source = Avatar(role == null ? null : role.Avatar);
            UI<TextBlock>("RoleInfo").Text = role == null ? "" : Preview(role.Background, 155);
            RenderMessages();
            RefreshContext();
            SetStatus(model != null && model.Demo ? "离线演示 · Ctrl + Enter 发送" : "Ctrl + Enter 发送");
            UpdateRetry();
            UI<ScrollViewer>("ChatScroll").ScrollToEnd();
        }

        private void RenderMessages()
        {
            var list = UI<StackPanel>("Messages");
            list.Children.Clear();
            if (active == null) return;
            if (active.Messages.Count == 0)
            {
                var empty = Label("给角色发一条消息，开始这一段故事。", 14, "Muted");
                empty.Margin = new Thickness(6, 18, 6, 18);
                list.Children.Add(empty);
            }
            int hidden = Math.Max(0, active.Messages.Count - world.Settings.PageSize - extraVisibleMessages);
            if (hidden > 0)
            {
                var older = Button("加载更早的消息（还有 " + hidden + " 条）", LoadOlder);
                older.HorizontalAlignment = HorizontalAlignment.Center;
                older.Margin = new Thickness(0, 0, 0, 12);
                list.Children.Add(older);
            }
            foreach (var message in active.Messages.Skip(hidden)) AddBubble(message, true);
        }

        private void LoadOlder()
        {
            if (work != null || active == null) return;
            var scroll = UI<ScrollViewer>("ChatScroll");
            var list = UI<StackPanel>("Messages");
            double previousHeight = list.ActualHeight;
            double previousOffset = scroll.VerticalOffset;
            extraVisibleMessages += world.Settings.PageSize;
            RenderMessages();
            scroll.UpdateLayout();
            scroll.ScrollToVerticalOffset(previousOffset + Math.Max(0, list.ActualHeight - previousHeight));
        }

        private static string StringInfoInitial(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? "?" : System.Globalization.StringInfo.GetNextTextElement(value);
        }

        private TextBox AddBubble(Message message, bool commands)
        {
            bool user = message.Role == "user";
            var role = Context.Find(world.Characters, active.CharacterId);
            var profile = Context.Player(world, active);
            var block = new StackPanel {
                HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = user ? new Thickness(40, 5, 2, 10) : new Thickness(2, 5, 40, 10)
            };
            var byline = new DockPanel { LastChildFill = true };
            if (commands)
            {
                var menu = new ContextMenu();
                MenuItem item = new MenuItem { Header = "编辑消息" };
                item.Click += (sender, args) => Guard(() => EditMessage(message));
                MenuItem remove = new MenuItem { Header = "删除消息" };
                remove.Click += (sender, args) => Guard(() => DeleteMessage(message));
                menu.Items.Add(item);
                menu.Items.Add(remove);
                var more = Button("···", () => menu.IsOpen = true);
                more.BorderBrush = Brushes.Transparent;
                more.Background = Brushes.Transparent;
                more.Padding = new Thickness(5, 1, 5, 1);
                more.Margin = new Thickness(5, 0, 0, 0);
                menu.PlacementTarget = more;
                more.ContextMenu = menu;
                more.ToolTip = "管理消息";
                DockPanel.SetDock(more, Dock.Right);
                byline.Children.Add(more);
            }
            var senderLine = new StackPanel { Orientation = Orientation.Horizontal };
            string speaker = user ? (profile == null ? "你" : profile.Name) : (role == null ? "角色" : role.Name);
            if (!user && active.ShowCharacterAvatar)
            {
                var badge = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5),
                    ClipToBounds = true, Margin = new Thickness(2, 0, 4, 3), ToolTip = speaker }
                    .UseBrush(Border.BackgroundProperty, "Selection");
                var source = UI<Image>("RoleAvatar").Source;
                badge.Child = source == null
                    ? (UIElement)new TextBlock { Text = StringInfoInitial(speaker), HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center, FontSize = 11 }.UseBrush(TextBlock.ForegroundProperty, "Muted")
                    : new Image { Source = source, Stretch = Stretch.UniformToFill };
                senderLine.Children.Add(badge);
            }
            if (active.ShowSpeaker)
            {
                var name = Label(speaker, 12, "Muted");
                name.Margin = new Thickness(2, 2, 2, 4);
                senderLine.Children.Add(name);
            }
            byline.Children.Add(senderLine);
            block.Children.Add(byline);
            var text = new TextBox { Text = message.Text ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(11, 8, 11, 8), FontSize = active.MessageFontSize };
            block.Children.Add(new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = text }
                .UseBrush(Border.BackgroundProperty, user ? "Selection" : "Card").UseBrush(Border.BorderBrushProperty, "Line"));
            UI<StackPanel>("Messages").Children.Add(block);
            return text;
        }

        private void RefreshContext()
        {
            var role = Context.Find(world.Characters, active.CharacterId);
            var lore = role == null ? null : Context.Find(world.Books, role.BookId);
            var matches = Context.Matches(world, active, Context.Tail(active.Messages, active.HistoryCount ?? world.Settings.HistoryCount));
            UI<TextBlock>("LoreStatus").Text = lore == null ? "未挂载世界书" :
                lore.Name + "\n" + (!active.UseWorldBook ? "此会话已暂停使用" : matches.Count == 0 ? "暂无匹配" : "匹配 " + matches.Count + " 条\n" +
                    String.Join("、", matches.Take(4).Select(entry => entry.Name)));
            UI<TextBlock>("MemoryStatus").Text = active.Memories.Count + " 条手动记忆 · " +
                (String.IsNullOrWhiteSpace(active.Summary) ? "暂无摘要" : "已有摘要") +
                (active.UseMemory ? "" : "\n此会话已暂停用作上下文");
        }

        private void SetStatus(string value, bool error = false)
        {
            var label = UI<TextBlock>("ChatStatus");
            label.Text = value;
            label.UseBrush(TextBlock.ForegroundProperty, error ? "Danger" : "Muted");
        }

        private void UpdateRetry()
        {
            UI<Button>("Retry").IsEnabled = work == null && active != null && active.Messages.Any(message => message.Role == "user");
        }

        private async Task Send(bool retry)
        {
            if (work != null || active == null) return;
            var session = active;
            var model = Context.Find(world.Models, session.ModelId);
            if (model == null) { SetStatus("请先在会话设置里选一个模型。", true); return; }
            var composer = UI<TextBox>("Composer");
            if (!retry && String.IsNullOrWhiteSpace(composer.Text)) return;
            if (retry && !session.Messages.Any(message => message.Role == "user")) return;
            var previous = retry && session.Messages.Last().Role == "assistant" ? session.Messages.Last() : null;
            var reply = new StringBuilder();
            string status = "Ctrl + Enter 发送";
            bool errorStatus = false;
            work = new CancellationTokenSource();
            try
            {
                var userMessage = retry ? null : new Message { Role = "user", Text = composer.Text.Trim() };
                var history = previous == null ? session.Messages.AsEnumerable() : session.Messages.Take(session.Messages.Count - 1);
                if (userMessage != null) history = history.Concat(new[] { userMessage });
                var recent = Context.Tail(history, session.HistoryCount ?? world.Settings.HistoryCount).ToList();
                var hits = Context.Matches(world, session, recent);
                int omittedMessages;
                int omittedEntries;
                var request = Context.Build(world, session, recent, hits, out omittedMessages, out omittedEntries);

                if (userMessage != null)
                {
                    session.Messages.Add(userMessage);
                    var previousUpdate = session.Updated;
                    session.Updated = DateTime.UtcNow;
                    try { Save(); }
                    catch
                    {
                        session.Messages.Remove(userMessage);
                        session.Updated = previousUpdate;
                        throw;
                    }
                    composer.Clear();
                }

                RefreshSessions(session);
                if (previous != null)
                {
                    var messages = UI<StackPanel>("Messages").Children;
                    messages.RemoveAt(messages.Count - 1);
                }
                var output = AddBubble(new Message { Role = "assistant", Text = "…" }, false);
                SetBusy(true);
                if (!retry && session.RefocusComposer) composer.Focus();
                SetStatus("");
                var scroller = UI<ScrollViewer>("ChatScroll");
                scroller.ScrollToEnd();
                var notices = new List<string>();
                if (!model.Demo)
                {
                    try
                    {
                        var semantic = await SemanticLore.Find(world, session, recent, hits, work.Token);
                        if (semantic.Count > 0)
                        {
                            hits.AddRange(semantic);
                            request = Context.Build(world, session, recent, hits, out omittedMessages, out omittedEntries);
                            notices.Add("语义匹配：" + String.Join("、", semantic.Select(entry => entry.Name)));
                        }
                    }
                    catch (Exception error)
                    {
                        work.Token.ThrowIfCancellationRequested();
                        notices.Add("本轮跳过语义检索：" + (error is OperationCanceledException ? "等待超时" : Preview(error.Message, 100)));
                    }
                }
                var exclusions = new List<string>();
                if (omittedMessages > 0) exclusions.Add(omittedMessages + " 条旧消息");
                if (omittedEntries > 0) exclusions.Add(omittedEntries + " 条世界书设定");
                if (exclusions.Count > 0) notices.Add("达到字数上限，已略过 " + String.Join("、", exclusions));
                string contextStatus = String.Join("\n", notices);
                SetStatus(contextStatus);
                await Models.Generate(model, request, fragment =>
                {
                    bool follow = scroller.ScrollableHeight - scroller.VerticalOffset < 52;
                    reply.Append(fragment);
                    output.Text = reply.ToString();
                    if (follow) scroller.ScrollToEnd();
                }, work.Token, stream: session.StreamReplies);
                if (String.IsNullOrWhiteSpace(reply.ToString())) throw new InvalidOperationException("模型没有返回正文。");
                if (contextStatus.Length > 0) status = contextStatus;
            }
            catch (OperationCanceledException)
            {
                status = reply.Length == 0 ? "已停止" : "已停止，保留了收到的回复";
            }
            catch (Exception error)
            {
                status = "生成失败，可重新生成";
                errorStatus = true;
                if (!closing) ShowError(error.Message);
            }
            finally
            {
                if (!String.IsNullOrWhiteSpace(reply.ToString()))
                {
                    var generated = new Message { Role = "assistant", Text = reply.ToString() };
                    if (previous == null) session.Messages.Add(generated);
                    else session.Messages[session.Messages.Count - 1] = generated;
                    session.Updated = DateTime.UtcNow;
                    try { Save(); }
                    catch (Exception error)
                    {
                        status = "回复尚未保存";
                        errorStatus = true;
                        ShowError(error.Message);
                        closing = false;
                    }
                }
                RefreshSessions(session);
                SetStatus(status, errorStatus);
                FinishWork();
            }
        }

        private void EditMessage(Message message)
        {
            if (work != null) return;
            var form = new Form(Window, "编辑消息");
            var text = form.Text("消息正文", message.Text, 170);
            if (form.Show(() =>
            {
                message.Text = Form.Required(text.Text, "消息正文");
                Save();
                return true;
            })) RenderChat();
        }

        private void DeleteMessage(Message message)
        {
            if (work != null || !Confirm("删除这条消息？")) return;
            active.Messages.Remove(message);
            Save();
            RenderChat();
        }

        private void ManageMemory()
        {
            var session = active;
            var form = new Form(Window, "剧情记忆");
            form.Note("记忆只属于这段会话，并会和近期消息一同交给模型。");
            var memories = form.Text("手动记忆（每行一条）", String.Join("\n", session.Memories), 130);
            var summary = form.Text("剧情摘要", session.Summary, 160);
            var model = Context.Find(world.Models, session.ModelId);
            var generate = form.Action("生成摘要");
            generate.IsEnabled = model != null && session.Messages.Any(message => message.Role == "user");
            if (model != null && model.Demo) form.Note("离线演示会填入预设摘要。");
            using (var cancel = new CancellationTokenSource())
            {
                bool generating = false;
                form.Window.Closing += (sender, args) =>
                {
                    if (!generating) return;
                    cancel.Cancel();
                };
                generate.Click += async (sender, args) =>
                {
                    form.Busy(true);
                    generating = true;
                    form.Status("正在整理最近的对话…");
                    try
                    {
                        var request = new List<Message> { new Message { Role = "system",
                            Text = "你负责整理角色扮演的剧情进展。仅输出精炼摘要，包含已发生事件、人物关系与待解决的线索。" } };
                        var notes = new StringBuilder();
                        if (!String.IsNullOrWhiteSpace(summary.Text)) notes.AppendLine("上一次摘要：").AppendLine(summary.Text);
                        foreach (var message in Context.Tail(session.Messages, 40))
                            notes.AppendLine((message.Role == "user" ? "玩家：" : "角色：") + message.Text);
                        request.Add(new Message { Role = "user", Text = notes.ToString() });
                        var result = new StringBuilder();
                        await Models.Generate(model, request, fragment => result.Append(fragment), cancel.Token, true);
                        if (String.IsNullOrWhiteSpace(result.ToString())) throw new InvalidOperationException("模型没有返回摘要。");
                        summary.Text = result.ToString();
                        form.Status("摘要已生成，可以编辑后保存。");
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception error)
                    {
                        if (!cancel.IsCancellationRequested) form.Status(error.Message, true);
                    }
                    finally
                    {
                        generating = false;
                        if (!cancel.IsCancellationRequested) form.Busy(false);
                    }
                };
                if (form.Show(() =>
                {
                    session.Memories = memories.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(value => value.Trim()).Where(value => value.Length > 0).ToList();
                    session.Summary = summary.Text.Trim();
                    Save();
                    return true;
                })) RefreshContext();
            }
        }

        private void ExportChat()
        {
            var session = active;
            string path = ExportFile("聊天记录 (*.txt)|*.txt|JSON (*.json)|*.json", "txt", session.Name, WorkFiles.UserFolder("聊天记录"));
            if (path == null) return;
            var role = Context.Find(world.Characters, session.CharacterId);
            var profile = Context.Player(world, session);
            string content = System.IO.Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? Store.Json().Serialize(session.WithoutBackground())
                : session.Name + "\n\n" + String.Join("\n\n", session.Messages.Select(message =>
                    (message.Role == "user" ? (profile == null ? "你" : profile.Name) : (role == null ? "角色" : role.Name)) + "\n" + message.Text));
            Store.Write(path, content, false);
            SetStatus("聊天记录已导出");
        }
    }
}
