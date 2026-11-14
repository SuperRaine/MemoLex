using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Forms=System.Windows.Forms;

namespace MemoLex.Desktop;

internal static class ScreenTranslationCapture
{
    internal static Bitmap? Select()
    {
        var prior=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            var bounds=Forms.SystemInformation.VirtualScreen;
            if((long)bounds.Width*bounds.Height>100_000_000)throw new InvalidOperationException("屏幕范围过大，暂时无法截图。");
            using var screen=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format24bppRgb);
            using(var graphics=Graphics.FromImage(screen))graphics.CopyFromScreen(bounds.Location,Point.Empty,screen.Size);
            using var selector=new SelectionForm(screen,bounds);
            return selector.ShowDialog()==Forms.DialogResult.OK?screen.Clone(selector.RegionPixels,PixelFormat.Format24bppRgb):null;
        }
        finally{if(prior!=IntPtr.Zero)SetThreadDpiAwarenessContext(prior);}
    }
    internal static Rectangle MapSelection(Point a,Point b,Size client,Size image)
    {
        if(client.Width<=0||client.Height<=0)return Rectangle.Empty;
        var left=Math.Clamp(Math.Min(a.X,b.X),0,client.Width);var top=Math.Clamp(Math.Min(a.Y,b.Y),0,client.Height);
        var right=Math.Clamp(Math.Max(a.X,b.X),0,client.Width);var bottom=Math.Clamp(Math.Max(a.Y,b.Y),0,client.Height);
        var x=(int)Math.Floor(left*(double)image.Width/client.Width);var y=(int)Math.Floor(top*(double)image.Height/client.Height);
        return Rectangle.FromLTRB(x,y,(int)Math.Ceiling(right*(double)image.Width/client.Width),(int)Math.Ceiling(bottom*(double)image.Height/client.Height));
    }
    private sealed class SelectionForm:Forms.Form
    {
        private readonly Bitmap _screen;private Point _start,_end;private bool _dragging;
        internal Rectangle RegionPixels {get;private set;}
        internal SelectionForm(Bitmap screen,Rectangle bounds)
        {
            _screen=screen;FormBorderStyle=Forms.FormBorderStyle.None;StartPosition=Forms.FormStartPosition.Manual;Bounds=bounds;TopMost=true;ShowInTaskbar=false;DoubleBuffered=true;KeyPreview=true;Cursor=Forms.Cursors.Cross;
            Text="截图翻译 · 拖动框选英文区域";
        }
        protected override void OnKeyDown(Forms.KeyEventArgs e){if(e.KeyCode==Forms.Keys.Escape){DialogResult=Forms.DialogResult.Cancel;Close();}base.OnKeyDown(e);}
        protected override void OnMouseDown(Forms.MouseEventArgs e)
        {
            if(e.Button==Forms.MouseButtons.Right){DialogResult=Forms.DialogResult.Cancel;Close();return;}
            if(e.Button==Forms.MouseButtons.Left){_start=_end=e.Location;_dragging=true;Capture=true;Invalidate();}
        }
        protected override void OnMouseMove(Forms.MouseEventArgs e){if(_dragging){_end=e.Location;Invalidate();}}
        protected override void OnMouseUp(Forms.MouseEventArgs e)
        {
            if(!_dragging||e.Button!=Forms.MouseButtons.Left)return;_end=e.Location;_dragging=false;Capture=false;
            RegionPixels=MapSelection(_start,_end,ClientSize,_screen.Size);
            if(RegionPixels.Width<12||RegionPixels.Height<8){Invalidate();return;}
            DialogResult=Forms.DialogResult.OK;Close();
        }
        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            var graphics=e.Graphics;graphics.DrawImage(_screen,ClientRectangle);
            using var dim=new SolidBrush(Color.FromArgb(110,0,0,0));graphics.FillRectangle(dim,ClientRectangle);
            if(_dragging)
            {
                var selection=Rectangle.FromLTRB(Math.Min(_start.X,_end.X),Math.Min(_start.Y,_end.Y),Math.Max(_start.X,_end.X),Math.Max(_start.Y,_end.Y));
                if(selection.Width>0&&selection.Height>0)
                {
                    var pixels=MapSelection(_start,_end,ClientSize,_screen.Size);graphics.DrawImage(_screen,selection,pixels,GraphicsUnit.Pixel);
                    using var line=new Pen(Color.FromArgb(48,184,244),2);graphics.DrawRectangle(line,selection);
                }
            }
            var monitor=Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds;var local=PointToClient(new Point(monitor.Left+monitor.Width/2,monitor.Top+30));
            var hint=new Rectangle(Math.Max(8,local.X-235),Math.Max(8,local.Y),470,38);using var fill=new SolidBrush(Color.FromArgb(240,28,48,64));graphics.FillRectangle(fill,hint);
            Forms.TextRenderer.DrawText(graphics,"拖动框选英文区域 · 松开后翻译 · Esc / 右键取消",Font,hint,Color.White,Forms.TextFormatFlags.HorizontalCenter|Forms.TextFormatFlags.VerticalCenter);
        }
    }
    [DllImport("user32.dll")]private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
