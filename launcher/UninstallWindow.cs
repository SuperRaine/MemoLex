using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MemoLex.Packaging
{
    public sealed class UninstallWindow : Form
    {
        public CheckBox KeepUserData { get; private set; }
        private readonly Button remove;
        private readonly Button cancel;
        private readonly Label state;
        private readonly ProgressBar progress;
        private bool busy;
        private bool complete;
        public UninstallWindow(string root, PackageManifest manifest)
        {
            var fingerprint = PackageManifest.Hash(System.IO.Path.Combine(root, "app", "package.json"));
            Text = "卸载墨墨搜词"; Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.White;
            ClientSize = new Size(560, 340); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            var title = new Label { Text = "卸载墨墨搜词", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), ForeColor = Color.FromArgb(31, 63, 86), AutoSize = true, Location = new Point(28, 24) }; Controls.Add(title);
            Controls.Add(new Label { Text = "软件位置：" + root, ForeColor = Color.FromArgb(100, 122, 140), AutoEllipsis = true, Location = new Point(28, 76), Size = new Size(504, 42) });
            KeepUserData = new CheckBox { Text = "保留用户资料", Checked = false, AutoSize = true, Location = new Point(28, 130) }; Controls.Add(KeepUserData);
            Controls.Add(new Label { Text = "未勾选时，将删除本机账号、助记、查词历史和设置。\n云端数据不受影响；同一 Windows 账号下的墨墨搜词共用这些资料。", ForeColor = Color.FromArgb(100, 122, 140), Location = new Point(28, 163), Size = new Size(504, 54) });
            state = new Label { AutoEllipsis = true, Location = new Point(28, 221), Size = new Size(504, 24) }; Controls.Add(state);
            progress = new ProgressBar { Style = ProgressBarStyle.Marquee, Visible = false, Location = new Point(28, 253), Size = new Size(504, 4) }; Controls.Add(progress);
            remove = new Button { Text = "卸载", BackColor = Color.FromArgb(42, 184, 242), FlatStyle = FlatStyle.Flat, Location = new Point(424, 283), Size = new Size(108, 35) }; remove.FlatAppearance.BorderSize = 0; Controls.Add(remove);
            cancel = new Button { Text = "取消", Location = new Point(304, 283), Size = new Size(108, 35) }; Controls.Add(cancel);
            cancel.Click += delegate { Close(); };
            remove.Click += async delegate
            {
                if (complete) { Close(); return; }
                busy = true; KeepUserData.Enabled = remove.Enabled = cancel.Enabled = false; progress.Visible = true;
                try
                {
                    if (!PackageManifest.Hash(System.IO.Path.Combine(root, "app", "package.json")).Equals(fingerprint, StringComparison.OrdinalIgnoreCase)) throw new System.IO.IOException("软件已更新，请重新打开卸载程序。");
                    var keep = KeepUserData.Checked;
                    var result = await Task.Run(() => UninstallOperations.Remove(root, manifest, keep, message => BeginInvoke(new Action(() => state.Text = message))));
                    state.Text = "已卸载。" + (keep ? "用户资料已保留。" : "用户资料已删除。") + (result.PreservedFiles > 0 ? "目录中保留了已修改的文件。" : "");
                    complete = true; remove.Text = "完成";
                }
                catch (Exception error) { state.Text = error is System.IO.IOException || error is UnauthorizedAccessException ? error.Message : "卸载未能完成，请关闭相关程序后重试。"; }
                finally { busy = false; remove.Enabled = true; cancel.Enabled = !complete; KeepUserData.Enabled = !complete; progress.Visible = false; }
            };
            FormClosing += delegate(object sender, FormClosingEventArgs args) { if (busy) args.Cancel = true; };
        }
    }
}
