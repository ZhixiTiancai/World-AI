using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WorldAI
{
    internal sealed partial class Shell
    {
        private void EditCharacter(Character existing)
        {
            var role = existing == null ? new Character() : Store.Copy(existing);
            role.Normalize();
            var form = new Form(Window, existing == null ? "新建角色" : "编辑角色", width: 900);
            var name = form.Text("角色名", role.Name);
            var background = form.Text("背景与场景（含性格）", role.Background, 260);
            var textTools = new WrapPanel();
            form.Add(null, textTools);
            textTools.Children.Add(form.InlineAction("放大编辑背景", () =>
            {
                string text = EditRoleText(form.Window, "背景与场景", background.Text);
                if (text != null) background.Text = text;
            }));
            var examplesButton = form.InlineAction("示例对话");
            Action refreshExamples = () => examplesButton.Content =
                String.IsNullOrWhiteSpace(role.Examples) ? "示例对话 · 未设置" : "示例对话 · 已设置";
            refreshExamples();
            examplesButton.Click += (sender, args) => Guard(() =>
            {
                string text = EditRoleText(form.Window, "示例对话", role.Examples);
                if (text == null) return;
                role.Examples = text;
                refreshExamples();
            });
            textTools.Children.Add(examplesButton);

            var greeting = form.Text("主开场白", role.Greeting, 105);
            var openingTools = new WrapPanel();
            form.Add(null, openingTools);
            openingTools.Children.Add(form.InlineAction("放大编辑主开场白", () =>
            {
                string text = EditRoleText(form.Window, "主开场白", greeting.Text);
                if (text != null) greeting.Text = text;
            }));
            var options = form.InlineAction("备选开场白");
            Action refreshOptions = () => options.Content = "备选开场白（" + role.AlternateGreetings.Count + "）";
            refreshOptions();
            options.Click += (sender, args) => Guard(() =>
            {
                var edited = EditAlternates(form.Window, role.AlternateGreetings);
                if (edited == null) return;
                role.AlternateGreetings = edited;
                refreshOptions();
            });
            openingTools.Children.Add(options);

            form.Action("转换中文…", () =>
            {
                var fields = new[] { background.Text, greeting.Text, role.Examples ?? "" }
                    .Concat(role.AlternateGreetings).ToArray();
                if (fields.All(String.IsNullOrWhiteSpace))
                {
                    form.Status("角色卡里还没有可以翻译的内容。", true);
                    return;
                }
                string[] translated = TranslateRole(form.Window, fields);
                if (translated == null) return;
                background.Text = translated[0];
                greeting.Text = translated[1];
                role.Examples = translated[2];
                role.AlternateGreetings = translated.Skip(3).ToList();
                refreshExamples();
                refreshOptions();
                form.Status("已填入中文译文。检查后点「保存」应用到角色卡。");
            });

            var book = form.Choice("挂载世界书", world.Books, role.BookId, "不挂载");
            var avatar = new Image { Source = Avatar(role.Avatar), Width = 70, Height = 70,
                HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.UniformToFill };
            form.Add("头像", avatar);
            form.Action("选择头像", () =>
            {
                string file = FilePicker.Open(form.Window, "图片 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg");
                if (file == null) return;
                role.Avatar = Cards.AvatarFromFile(file);
                avatar.Source = Avatar(role.Avatar);
            });
            form.Action("清除头像", () => { role.Avatar = null; avatar.Source = null; });

            if (form.Show(() =>
            {
                role.Name = Form.Required(name.Text, "角色名");
                role.Background = background.Text.Trim();
                role.Greeting = greeting.Text.Trim();
                role.BookId = Form.Selected(book);
                Put(world.Characters, existing, role);
                return true;
            })) RenderLibrary();
        }

        private static string EditRoleText(Window owner, string title, string initial, bool required = false)
        {
            var form = new Form(owner, title, "应用", 920);
            var text = form.FullText(initial);
            string result = null;
            return form.Show(() =>
            {
                result = required ? Form.Required(text.Text, title) : text.Text.Trim();
                return true;
            }) ? result : null;
        }

        private List<string> EditAlternates(Window owner, List<string> original)
        {
            var list = original.ToList();
            var form = new Form(owner, "备选开场白", "应用", 720);
            form.Note("新建会话时可以预览并选择任意开场白。");
            var choices = new ListBox { Height = 230, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ScrollViewer.SetHorizontalScrollBarVisibility(choices, ScrollBarVisibility.Disabled);
            form.Add("备选列表", choices);
            Action<int> refresh = selected =>
            {
                choices.Items.Clear();
                for (int i = 0; i < list.Count; i++)
                    choices.Items.Add(Label("备选 " + (i + 1) + "   " + Preview(list[i], 100)));
                if (list.Count > 0) choices.SelectedIndex = Math.Max(0, Math.Min(selected, list.Count - 1));
            };
            form.Action("移除", () =>
            {
                int index = choices.SelectedIndex;
                if (index < 0) return;
                list.RemoveAt(index);
                refresh(index);
            });
            form.Action("编辑", () =>
            {
                int index = choices.SelectedIndex;
                if (index < 0) return;
                string text = EditRoleText(form.Window, "编辑备选开场白", list[index], true);
                if (text == null) return;
                list[index] = text;
                refresh(index);
            });
            form.Action("添加", () =>
            {
                string text = EditRoleText(form.Window, "添加备选开场白", "", true);
                if (text == null) return;
                list.Add(text);
                refresh(list.Count - 1);
            });
            refresh(0);
            return form.Show(() => true) ? list : null;
        }

        private string[] TranslateRole(Window owner, string[] fields)
        {
            var models = world.Models.Where(model => !model.Demo).ToList();
            if (models.Count == 0)
                throw new InvalidOperationException("请先到「模型设置」添加一个真实模型，再转换中文。");

            var form = new Form(owner, "转换中文", "开始翻译");
            var pick = form.Choice("翻译模型", models, world.Settings.DefaultModelId, null);
            form.Note("会把背景与场景、示例对话和所有开场白发给所选模型。翻译完成后，可以在角色卡草稿里检查和修改。");
            string[] result = null;
            CancellationTokenSource pending = null;
            form.Window.Closing += (sender, args) => { if (pending != null) pending.Cancel(); };
            Action<Model> run = async model =>
            {
                using (var cancel = new CancellationTokenSource())
                {
                    pending = cancel;
                    form.Busy(true);
                    try
                    {
                        result = await Translation.ToChinese(model, fields, (part, total) =>
                            form.Status("正在翻译（" + part + " / " + total + "）…"), cancel.Token);
                        if (form.Window.IsVisible && !cancel.IsCancellationRequested)
                        {
                            pending = null;
                            form.Window.DialogResult = true;
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception error)
                    {
                        if (form.Window.IsVisible) form.Status(error.Message, true);
                    }
                    finally
                    {
                        pending = null;
                        if (form.Window.IsVisible) form.Busy(false);
                    }
                }
            };
            return form.Show(() =>
            {
                var model = Context.Find(models, Form.Selected(pick));
                if (model == null || pending != null) return false;
                run(model);
                return false;
            }) ? result : null;
        }

        private void ImportCharacter()
        {
            string path = OpenFile("角色卡 (*.png;*.json)|*.png;*.json", WorkFiles.UserFolder("角色卡"));
            if (path == null) return;
            Lore book;
            var role = Cards.Import(path, out book);
            if (book != null) world.Books.Add(book);
            try { Put(world.Characters, null, role); }
            catch { if (book != null) world.Books.Remove(book); throw; }
            RenderLibrary();
            Info("已导入「" + role.Name + "」，其中有 " + role.AlternateGreetings.Count + " 条备选开场白。\n\n" +
                "卡片的高级指令、标签和扩展脚本未导入。");
        }

        private void DuplicateRole(Character role)
        {
            var copy = Store.Copy(role);
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name += "（副本）";
            Put(world.Characters, null, copy);
            RenderLibrary();
        }

        private void ExportCharacter(Character role)
        {
            string path = ExportFile("角色卡 (*.json)|*.json", "json", role.Name, WorkFiles.UserFolder("角色卡"));
            if (path == null) return;
            Store.Write(path, Cards.Export(role, Context.Find(world.Books, role.BookId)), false);
            Info("角色卡已导出。");
        }

        private void RemoveCharacter(Character role)
        {
            if (world.Sessions.Any(session => session.CharacterId == role.Id))
            {
                Info("这个角色还有会话。请先导出需要保留的会话，并在会话设置中删除它们。");
                return;
            }
            DeleteItem(world.Characters, role);
        }

        private void ImportLore()
        {
            string path = OpenFile("世界书 (*.json)|*.json", WorkFiles.UserFolder("世界书"));
            if (path == null) return;
            var book = Cards.ImportLore(path);
            Put(world.Books, null, book);
            RenderLibrary();
            Info("已导入 " + book.Entries.Count + " 个条目。当前支持条目内容、普通关键词、启用和常驻状态。");
        }

        private void ExportLore(Lore book)
        {
            string path = ExportFile("世界书 (*.json)|*.json", "json", book.Name, WorkFiles.UserFolder("世界书"));
            if (path == null) return;
            Store.Write(path, Cards.ExportLore(book), false);
            Info("世界书已导出。");
        }

        private void EditLore(Lore existing)
        {
            var book = existing == null ? new Lore() : Store.Copy(existing);
            var form = new Form(Window, existing == null ? "新建世界书" : "编辑世界书");
            var name = form.Text("世界书名称", book.Name);
            var box = new ListBox { Height = 160 };
            form.Add("条目", box);
            Action refresh = () =>
            {
                box.Items.Clear();
                foreach (var entry in book.Entries)
                {
                    box.Items.Add(new ListBoxItem { Tag = entry, Content = entry.Name +
                        (entry.Enabled ? (entry.Always ? "  ·  常驻" : "") : "  ·  已停用") });
                }
                if (box.Items.Count > 0) box.SelectedIndex = 0;
            };
            Func<Entry> selected = () => SelectedTag<Entry>(box);
            form.Action("删除条目", () =>
            {
                var entry = selected();
                if (entry == null) return;
                book.Entries.Remove(entry);
                refresh();
            });
            form.Action("编辑条目", () =>
            {
                var entry = selected();
                if (entry != null && EditEntry(form.Window, entry)) refresh();
            });
            form.Action("添加条目", () =>
            {
                var entry = new Entry();
                if (EditEntry(form.Window, entry))
                {
                    book.Entries.Add(entry);
                    refresh();
                    box.SelectedIndex = box.Items.Count - 1;
                }
            });
            refresh();
            if (form.Show(() =>
            {
                book.Name = Form.Required(name.Text, "世界书名称");
                Put(world.Books, existing, book);
                return true;
            })) RenderLibrary();
        }

        private bool EditEntry(Window owner, Entry entry)
        {
            var form = new Form(owner, "世界书条目");
            var name = form.Text("条目标题", entry.Name);
            var keywords = form.Text("关键词（用逗号分隔）", entry.Keywords);
            var content = form.Text("设定内容", entry.Content, 150);
            var modes = new[] {
                new Item { Id = "keyword", Name = "按关键词" },
                new Item { Id = "semantic", Name = "按意思（语义检索）" },
                new Item { Id = "both", Name = "关键词或意思命中即可" }
            };
            var mode = form.Choice("匹配方式", modes, Entry.Mode(entry.Retrieval), null);
            var enabled = form.Check("启用条目", entry.Enabled);
            var always = form.Check("始终加入设定，无需关键词", entry.Always);
            return form.Show(() =>
            {
                string newName = Form.Required(name.Text, "条目标题");
                string newContent = Form.Required(content.Text, "设定内容");
                entry.Name = newName;
                entry.Content = newContent;
                entry.Keywords = keywords.Text.Trim();
                entry.Enabled = enabled.IsChecked == true;
                entry.Always = always.IsChecked == true;
                entry.Retrieval = Entry.Mode(Form.Selected(mode));
                return true;
            });
        }
    }
}
