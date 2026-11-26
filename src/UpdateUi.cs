using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WorldAI
{
    internal sealed partial class Shell
    {
        private bool hasUpdate;

        private void AdvertiseUpdate(bool available)
        {
            hasUpdate = available;
            UI<Button>("NavPreferences").Content = available ? "全局设置  ·  有更新" : "全局设置";
            if (page == "Preferences") RenderLibrary();
        }

        private async void CheckUpdatesOnStartup()
        {
            if (!world.Settings.CheckUpdatesOnStart) return;
            try
            {
                using (var token = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
                {
                    var release = await Updates.Latest(token.Token);
                    if (Window.IsVisible) AdvertiseUpdate(release != null && release.IsNewer);
                }
            }
            catch (Exception) { /* Manual checking displays network and release errors. */ }
        }

        private void ShowUpdates()
        {
            var form = new Form(Window, "WorldAI 更新", "下载并重启", 650);
            form.Note("当前版本 " + Updates.Current);
            var notes = form.Text("发行说明", "", 190);
            notes.IsReadOnly = true;
            form.AcceptButton.IsEnabled = false;
            Updates.Release release = null;
            CancellationTokenSource pending = null;
            form.Window.Closing += (sender, args) => { if (pending != null) pending.Cancel(); };

            Action check = async () =>
            {
                if (pending != null) return;
                using (var token = new CancellationTokenSource(TimeSpan.FromSeconds(25)))
                {
                    pending = token;
                    form.Busy(true);
                    form.Status("正在检查 GitHub 上的最新版本…");
                    try
                    {
                        release = await Updates.Latest(token.Token);
                        if (!form.Window.IsVisible) return;
                        bool available = release != null && release.IsNewer;
                        AdvertiseUpdate(available);
                        notes.Text = release == null ? "" : (release.Notes ?? "");
                        form.Status(available ? (release.Version == Updates.Current ? "发现 " + release.Version + " 的同版本修复" : "发现新版 " + release.Version) :
                            release == null ? "仓库暂未发布正式版本。" : "已经是最新版本。");
                    }
                    catch (Exception error)
                    {
                        release = null;
                        if (form.Window.IsVisible)
                            form.Status(token.IsCancellationRequested ? "检测超时，请重试。" : error.Message, true);
                    }
                    finally
                    {
                        pending = null;
                        if (form.Window.IsVisible)
                        {
                            form.Busy(false);
                            form.AcceptButton.IsEnabled = release != null && release.IsNewer;
                        }
                    }
                }
            };
            form.Action("重新检测", check);
            form.Window.Loaded += (sender, args) => check();
            Action install = async () =>
            {
                using (var token = new CancellationTokenSource())
                {
                    pending = token;
                    form.Busy(true);
                    form.Status("正在下载并校验更新包…");
                    try
                    {
                        string id = await Updates.Prepare(release, token.Token);
                        if (!form.Window.IsVisible || token.IsCancellationRequested) return;
                        Updates.StartInstall(id);
                        pending = null;
                        form.Window.DialogResult = true;
                    }
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
            if (form.Show(() =>
            {
                if (pending != null || release == null || !release.IsNewer) return false;
                if (work != null || drafts.Values.Any(text => !String.IsNullOrWhiteSpace(text)))
                    throw new InvalidOperationException("请先处理当前回复和未发送的草稿，再更新重启。");
                install();
                return false;
            })) Window.Close();
        }
    }
}
