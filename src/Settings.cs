using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WorldAI
{
    internal sealed partial class Shell
    {
        private void EditPreferences()
        {
            var prior = world.Settings;
            var form = new Form(Window, "全局设置");
            var model = form.Choice("新会话默认模型", world.Models, prior.DefaultModelId, "自动选择");
            var prompt = form.Text("全局提示词", prior.SystemPrompt, 125);
            var count = form.Text("发送给模型的历史条数（1–500，含最新消息）", prior.HistoryCount.ToString());
            var limit = form.Text("发送字数上限（0 不限，或 1,000–500,000）", prior.ContextCharacters.ToString());
            var pageSize = form.Text("聊天窗口每次加载的消息条数（10–500）", prior.PageSize.ToString());
            var topmost = form.Check("窗口置顶", prior.AlwaysOnTop);
            var autoCheck = form.Check("启动时自动检测更新", prior.CheckUpdatesOnStart);
            if (form.Show(() =>
            {
                world.Settings = new Preferences {
                    DefaultModelId = Form.Selected(model), SystemPrompt = prompt.Text.Trim(),
                    DefaultProfileId = prior.DefaultProfileId, ThemeId = prior.ThemeId,
                    SemanticApiId = prior.SemanticApiId, EmbeddingModel = prior.EmbeddingModel, BackupDirectory = prior.BackupDirectory,
                    CheckUpdatesOnStart = autoCheck.IsChecked == true,
                    HistoryCount = ReadNumber(count.Text, 1, 500, "历史条数"),
                    ContextCharacters = ReadContextLimit(limit.Text),
                    PageSize = ReadNumber(pageSize.Text, 10, 500, "每页消息条数"),
                    AlwaysOnTop = topmost.IsChecked == true
                };
                try { Save(); }
                catch { world.Settings = prior; throw; }
                return true;
            }))
            {
                Window.Topmost = world.Settings.AlwaysOnTop;
                extraVisibleMessages = 0;
                RenderLibrary();
            }
        }

        private void EditSemanticSettings()
        {
            var form = new Form(Window, "语义检索设置", width: 640);
            var api = form.Choice("提供向量接口的 API 配置", world.Models.Where(item => !item.Demo), world.Settings.SemanticApiId, "停用语义检索");
            var name = form.Text("向量模型 ID", world.Settings.EmbeddingModel);
            form.Note("选择支持 /embeddings 的 API，并填写该服务的向量模型 ID。检索会将相关设定片段和刚发送的玩家消息交给此服务计算向量。");
            form.Note("保留现有关键词条目即可离线匹配。关闭此配置或检索失败时，仍使用本轮的关键词和常驻设定。");
            if (form.Show(() =>
            {
                string id = Form.Selected(api), oldId = world.Settings.SemanticApiId, oldModel = world.Settings.EmbeddingModel;
                string model = id == null ? name.Text.Trim() : Form.Required(name.Text, "向量模型 ID");
                world.Settings.SemanticApiId = id;
                world.Settings.EmbeddingModel = model;
                try { Save(); }
                catch { world.Settings.SemanticApiId = oldId; world.Settings.EmbeddingModel = oldModel; throw; }
                return true;
            })) RenderLibrary();
        }

        private static int ReadNumber(string text, int min, int max, string label)
        {
            int value;
            if (!Int32.TryParse(text, out value) || value < min || value > max)
                throw new InvalidOperationException(label + "请输入 " + min + " 到 " + max + " 之间的整数。");
            return value;
        }

        private static int ReadContextLimit(string text)
        {
            int value;
            if (!Int32.TryParse(text, out value) || !Store.IsContextLimit(value))
                throw new InvalidOperationException("发送字数上限请输入 0，或 1000 到 500000 之间的整数。");
            return value;
        }

        private void EditProfile(Profile old)
        {
            var profile = old == null ? new Profile() : Store.Copy(old);
            var form = new Form(Window, old == null ? "新建玩家身份" : "编辑玩家身份");
            var name = form.Text("身份名称", profile.Name);
            var description = form.Text("背景与设定", profile.Description, 145);
            if (form.Show(() =>
            {
                profile.Name = Form.Required(name.Text, "身份名称");
                profile.Description = description.Text.Trim();
                Put(world.Profiles, old, profile);
                return true;
            })) RenderLibrary();
        }

        private void EditPreset(Preset old)
        {
            var preset = old == null ? new Preset() : Store.Copy(old);
            var form = new Form(Window, old == null ? "新建预设" : "编辑预设");
            var name = form.Text("预设名称", preset.Name);
            var prompt = form.Text("自定义系统提示词", preset.SystemPrompt, 130);
            var style = form.Text("写作风格", preset.Style, 83);
            var length = form.Text("回复长度", preset.Length);
            if (form.Show(() =>
            {
                preset.Name = Form.Required(name.Text, "预设名称");
                preset.SystemPrompt = prompt.Text.Trim();
                preset.Style = style.Text.Trim();
                preset.Length = length.Text.Trim();
                Put(world.Presets, old, preset);
                return true;
            })) RenderLibrary();
        }

        private void EditModel(Model old)
        {
            var model = old == null ? new Model() : Store.Copy(old);
            var form = new Form(Window, old == null ? "添加模型" : "编辑模型");
            var name = form.Text("显示名称", model.Name);
            var endpoint = form.Text("API 地址", model.Endpoint);
            form.Note("可填类似 https://api.openai.com/v1 的基础地址，或完整的 /chat/completions 地址。");
            var key = form.Password(String.IsNullOrWhiteSpace(model.EncryptedKey)
                ? "API 密钥（无认证接口可留空）" : "新 API 密钥（地址不变时留空保留密钥）");
            var clear = form.Check("清除已保存的 API 密钥", false);

            var id = new ComboBox { IsEditable = true, Text = model.ModelName ?? "", MaxDropDownHeight = 300 };
            var fetch = new Button { Content = "获取模型列表", Style = (Style)Window.FindResource("ButtonBase"),
                Margin = new Thickness(8, 0, 0, 0) };
            var picker = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(fetch, Dock.Right);
            picker.Children.Add(fetch);
            picker.Children.Add(id);
            form.Add("模型 ID", picker);
            form.Note("从服务端获取模型后可直接选择，也可以手动输入模型 ID。");

            Func<string, string> configuredKey = address =>
            {
                if (!String.IsNullOrWhiteSpace(key.Password)) return Store.Encrypt(key.Password);
                bool sameAddress = old != null && String.Equals(old.Endpoint, address, StringComparison.Ordinal);
                return clear.IsChecked == true || !sameAddress ? null : model.EncryptedKey;
            };

            Func<Model> draft = () =>
            {
                string address = Form.Required(endpoint.Text, "API 地址");
                Models.Endpoint(address);
                string modelId = Form.Required(id.Text, "模型 ID");
                return new Model {
                    Id = model.Id,
                    Name = String.IsNullOrWhiteSpace(name.Text) ? modelId : name.Text.Trim(),
                    Endpoint = address, ModelName = modelId,
                    EncryptedKey = configuredKey(address)
                };
            };

            Action clearList = () =>
            {
                if (id.ItemsSource == null) return;
                string current = id.Text;
                id.ItemsSource = null;
                id.Text = current;
                form.Status("");
            };
            endpoint.TextChanged += (sender, args) => clearList();
            key.PasswordChanged += (sender, args) => clearList();
            clear.Checked += (sender, args) => clearList();
            clear.Unchecked += (sender, args) => clearList();

            CancellationTokenSource pending = null;
            form.Window.Closing += (sender, args) => { if (pending != null) pending.Cancel(); };
            Func<string, int, string, Func<CancellationToken, Task>, Task> runRequest =
                async (status, timeoutSeconds, failureHint, request) =>
            {
                using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                {
                    pending = cancel;
                    form.Busy(true);
                    form.Status(status);
                    try
                    {
                        await request(cancel.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        if (form.Window.IsVisible) form.Status("连接超时，未收到回复。" + failureHint, true);
                    }
                    catch (Exception error)
                    {
                        if (form.Window.IsVisible) form.Status(failureHint + error.Message, true);
                    }
                    finally
                    {
                        pending = null;
                        if (form.Window.IsVisible) form.Busy(false);
                    }
                }
            };

            fetch.Click += async (sender, args) =>
            {
                string[] ids = null;
                await runRequest("正在获取模型列表…", 15, "可手动填写模型 ID。\n", async token =>
                {
                    string address = Form.Required(endpoint.Text, "API 地址");
                    ids = await Models.ListIds(address, configuredKey(address), token);
                });
                if (!form.Window.IsVisible || ids == null) return;
                if (ids.Length == 0)
                {
                    form.Status("服务返回的模型列表为空，可以手动填写模型 ID。", true);
                    return;
                }
                string current = id.Text;
                id.ItemsSource = ids;
                id.SelectedItem = ids.Contains(current, StringComparer.Ordinal) ? current : null;
                id.Text = current;
                form.Status("已获取 " + ids.Length + " 个模型。请选择模型，再测试聊天连接。");
                id.Focus();
                id.IsDropDownOpen = true;
            };

            form.Action("测试连接").Click += async (sender, args) =>
            {
                await runRequest("正在请求模型…", 35, "", async token =>
                {
                    var result = new StringBuilder();
                    await Models.Generate(draft(), new List<Message> {
                        new Message { Role = "user", Text = "请只回答 OK。" }
                    }, fragment => result.Append(fragment), token);
                    if (String.IsNullOrWhiteSpace(result.ToString()))
                        throw new InvalidOperationException("接口连接成功，但模型没有返回正文。");
                    if (form.Window.IsVisible) form.Status("连接成功 · " + Preview(result.ToString(), 100));
                });
            };

            if (form.Show(() =>
            {
                var value = draft();
                Put(world.Models, old, value);
                return true;
            })) RenderLibrary();
        }

        private void Backup()
        {
            string path = ExportFile("WorldAI 备份 (*.json)|*.json", "json", "WorldAI-backup-" + DateTime.Now.ToString("yyyyMMdd"), BackupFolder);
            if (path == null) return;
            Store.Backup(world, path);
            Info("备份已保存。");
        }

        private void Restore()
        {
            string path = OpenFile("WorldAI 备份 (*.json)|*.json", BackupFolder);
            if (path == null) return;
            var recovered = Store.Read(path);
            recovered.Settings.BackupDirectory = world.Settings.BackupDirectory;
            if (!Confirm("用这份备份恢复本地数据？当前数据会保留一份恢复前备份。\n\n" +
                "角色 " + recovered.Characters.Count + " 个，会话 " + recovered.Sessions.Count + " 个。")) return;
            foreach (var model in recovered.Models)
            {
                var current = Context.Find(world.Models, model.Id);
                model.EncryptedKey = current != null &&
                    String.Equals(current.Endpoint, model.Endpoint, StringComparison.Ordinal) ? current.EncryptedKey : null;
            }
            Directory.CreateDirectory(BackupFolder);
            string rollback = Path.Combine(BackupFolder, "before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json");
            Store.Backup(world, rollback);
            Store.Save(recovered);
            world = recovered;
            Theme.Apply(world.Settings.ThemeId);
            Window.Topmost = world.Settings.AlwaysOnTop;
            active = null;
            drafts.Clear();
            RenderLibrary();
            Info("恢复完成。恢复前的备份保存在：\n" + rollback);
        }
    }
}
