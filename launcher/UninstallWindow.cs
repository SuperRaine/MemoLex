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
            KeepUserData = new RoundedCheckBox { Text = "保留用户资料", Checked = false, AutoSize = true, Location = new Point(28, 130) }; Controls.Add(KeepUserData);
            Controls.Add(new Label { Text = "不保留时，将删除本机账号、助记、查词历史和设置。\n云端数据不受影响；同一 Windows 账号下的墨墨搜词共用这些资料。", ForeColor = Color.FromArgb(100, 122, 140), Location = new Point(28, 163), Size = new Size(504, 54) });
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
                    complete = true; remove.Text = "完成"; cancel.Visible = false;
                }
                catch (Exception error) { state.Text = error is System.IO.IOException || error is UnauthorizedAccessException ? error.Message : "卸载未能完成，请关闭相关程序后重试。"; }
                finally { busy = false; remove.Enabled = true; cancel.Enabled = !complete; KeepUserData.Enabled = !complete; progress.Visible = false; }
            };
            FormClosing += delegate(object sender, FormClosingEventArgs args) { if (busy) args.Cancel = true; };
        }
    }

    internal sealed class RoundedCheckBox : CheckBox
    {
        internal RoundedCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.CheckButton;
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            var scale = DeviceScale();
            var text = TextRenderer.MeasureText(Text, Font);
            return new Size((int)Math.Ceiling(26 * scale) + text.Width, Math.Max((int)Math.Ceiling(26 * scale), text.Height));
        }
        private float DeviceScale() { using (var graphics = CreateGraphics()) return graphics.DpiX / 96f; }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var scale = e.Graphics.DpiX / 96f;
            var size = 18 * scale;
            var box = new RectangleF(scale, (ClientSize.Height - size) / 2f, size, size);
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var color = Checked ? Color.FromArgb(47, 140, 255) : Color.White;
            var border = Checked ? color : Color.FromArgb(199, 213, 226);
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            using (var brush = new SolidBrush(Enabled ? color : Color.FromArgb(236, 240, 244)))
            using (var pen = new Pen(Enabled ? border : Color.FromArgb(213, 222, 231), scale))
            {
                var diameter = 9 * scale;
                path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
                path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
                path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure(); e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path);
            }
            if (Checked)
            {
                using (var pen = new Pen(Enabled ? Color.White : Color.FromArgb(150, 170, 188), 1.8f * scale))
                {
                    pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    e.Graphics.DrawLines(pen, new[] { new PointF(box.Left + 3.5f * scale, box.Top + 8 * scale), new PointF(box.Left + 7.2f * scale, box.Top + 11.5f * scale), new PointF(box.Left + 14.3f * scale, box.Top + 4 * scale) });
                }
            }
            var left = (int)Math.Ceiling(26 * scale);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(left, 0, Math.Max(0, ClientSize.Width - left), ClientSize.Height), Enabled ? ForeColor : SystemColors.GrayText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(0, 0, (int)Math.Ceiling(size + 2 * scale), ClientSize.Height));
        }
    }
}
