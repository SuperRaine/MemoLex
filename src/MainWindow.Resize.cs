using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MemoLex;

public partial class MainWindow
{
    private HwndSource? _resizeSurface;
    private Rectangle? _resizeFace;
    private NativeRect _resizeMonitor;
    private bool _useNativeResize;

    private IntPtr ResizeHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmSizing = 0x0214, wmExitSizeMove = 0x0232;
        const int wmDpiChanged = 0x02E0, wmDisplayChange = 0x007E, wmShowWindow = 0x0018, wmActivateApp = 0x001C;
        if (message == wmExitSizeMove)
        {
            EndResizeSurface();
            _useNativeResize = false;
        }
        else if (message is wmDpiChanged or wmDisplayChange ||
                 (message is wmShowWindow or wmActivateApp && wParam == IntPtr.Zero))
        {
            if (_resizeSurface != null) FallBackToNativeResize();
        }
        else if (message == wmSizing && !_useNativeResize && IsVisible && WindowState == WindowState.Normal)
        {
            var proposed = Marshal.PtrToStructure<NativeRect>(lParam);
            if (_resizeSurface == null)
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return IntPtr.Zero;
                _resizeMonitor = info.Monitor;
            }
            if (proposed.Left < _resizeMonitor.Left || proposed.Top < _resizeMonitor.Top ||
                proposed.Right > _resizeMonitor.Right || proposed.Bottom > _resizeMonitor.Bottom)
                FallBackToNativeResize();
            else if (_resizeSurface == null)
                StartResizeSurface(hwnd);
        }
        return IntPtr.Zero;
    }

    private void StartResizeSurface(IntPtr hwnd)
    {
        // Draw the moving edge and the freshly laid-out client in one composition surface.
        // The real HWND continues to receive the system's normal sizing and layout messages.
        const int wsPopup = unchecked((int)0x80000000);
        const int wsExNoActivate = 0x08000000, wsExTransparent = 0x20, wsExToolWindow = 0x80, wsExTopmost = 0x8;
        var parameters = new HwndSourceParameters("MemoLex live resize")
        {
            WindowStyle = wsPopup,
            ExtendedWindowStyle = wsExNoActivate | wsExTransparent | wsExToolWindow | (Topmost ? wsExTopmost : 0),
            UsesPerPixelOpacity = true,
            PositionX = _resizeMonitor.Left, PositionY = _resizeMonitor.Top,
            Width = _resizeMonitor.Right - _resizeMonitor.Left,
            Height = _resizeMonitor.Bottom - _resizeMonitor.Top
        };
        try
        {
            _resizeSurface = new HwndSource(parameters);
            if (GetDpiForWindow(_resizeSurface.Handle) != GetDpiForWindow(hwnd))
            {
                FallBackToNativeResize();
                return;
            }
            var brush = new VisualBrush(RootFrame)
            {
                ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top
            };
            var radius = RootFrame.CornerRadius.TopLeft;
            _resizeFace = new Rectangle { Fill = brush, RadiusX = radius, RadiusY = radius };
            var canvas = new Canvas { IsHitTestVisible = false };
            canvas.Children.Add(_resizeFace);
            _resizeSurface.ContentRendered += ResizeSurfaceReady;
            _resizeSurface.RootVisual = canvas;
            RootFrame.SizeChanged += DrawResizeSurface;
            DrawResizeSurface();
            const uint showWithoutActivation = 0x0040 | 0x0010 | 0x0002 | 0x0001;
            if (!SetWindowPos(_resizeSurface.Handle, Topmost ? new IntPtr(-1) : IntPtr.Zero, 0, 0, 0, 0, showWithoutActivation))
                FallBackToNativeResize();
        }
        catch (Exception ex) when (ex is Win32Exception or COMException)
        {
            FallBackToNativeResize();
        }
    }

    private void DrawResizeSurface(object? sender, SizeChangedEventArgs args) => DrawResizeSurface();

    private void DrawResizeSurface()
    {
        if (_resizeSurface?.CompositionTarget is not { } target || _resizeFace?.Fill is not VisualBrush brush) return;
        var origin = RootFrame.PointToScreen(new Point());
        var position = target.TransformFromDevice.Transform(new Point(origin.X - _resizeMonitor.Left, origin.Y - _resizeMonitor.Top));
        var size = RootFrame.RenderSize;
        brush.Viewbox = new Rect(size);
        Canvas.SetLeft(_resizeFace, position.X);
        Canvas.SetTop(_resizeFace, position.Y);
        _resizeFace.Width = size.Width;
        _resizeFace.Height = size.Height;
    }

    private void ResizeSurfaceReady(object? sender, EventArgs args)
    {
        if (_resizeSurface == null || _source == null) return;
        _resizeSurface.ContentRendered -= ResizeSurfaceReady;
        int cloak = 1;
        if (DwmSetWindowAttribute(_source.Handle, DwmwaCloak, ref cloak, sizeof(int)) != 0)
            FallBackToNativeResize();
    }

    private void FallBackToNativeResize()
    {
        _useNativeResize = true;
        EndResizeSurface();
    }

    private void EndResizeSurface()
    {
        if (_resizeSurface == null) return;
        if (_source is { IsDisposed: false })
        {
            int cloak = 0;
            DwmSetWindowAttribute(_source.Handle, DwmwaCloak, ref cloak, sizeof(int));
        }
        RootFrame.SizeChanged -= DrawResizeSurface;
        _resizeSurface.ContentRendered -= ResizeSurfaceReady;
        _resizeSurface.Dispose();
        _resizeSurface = null;
        _resizeFace = null;
    }

    private const int DwmwaCloak = 13;
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
