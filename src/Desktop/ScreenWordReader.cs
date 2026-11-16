using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using MemoLex.Core;
using Tesseract;

namespace MemoLex.Desktop;

internal sealed class ScreenWordReader : IDisposable
{
    private sealed record TextProbe(HoverWord? Hit,bool SuppressOcr=false,ScreenBox? Clip=null,bool ValidateText=false);
    private Task<TextProbe>? _textProbe;
    private ScreenPoint _textPoint;
    private long _textStarted;
    private readonly SemaphoreSlim _ocrGate=new(1,1);
    private TesseractEngine? _engine;
    private bool _disposed;
    internal string? LastError {get;private set;}
    internal async Task<HoverWord?> ReadAsync(ScreenPoint point,CancellationToken cancellation)
    {
        if(_disposed)return null;
        var probe=await ProbeTextAsync(point,cancellation);
        if(probe?.SuppressOcr==true)return null;
        if(probe?.Hit is {} textHit&&textHit.Bounds.Contains(point))
        {
            if(probe.ValidateText)
            {
                var surface=new HoverSurface(textHit.Bounds,[]){TextPoint=point,Text=textHit.Word};
                await Task.Delay(100,cancellation);
                if(await IsSurfaceCurrentAsync(surface,cancellation))return textHit with{Surface=surface};
            }
            else
            {
                var surface=await Task.Run(()=>CaptureSurface(textHit.Bounds),cancellation);
                await Task.Delay(160,cancellation);
                return surface!=null&&await IsSurfaceCurrentAsync(surface,cancellation)?textHit with{Surface=surface}:null;
            }
        }
        cancellation.ThrowIfCancellationRequested();
        return await Task.Run(async()=>
        {
            await _ocrGate.WaitAsync(cancellation);
            try
            {
                if(_disposed)return null;
                using var capture=Capture(point,out var origin,probe?.Clip);
                using var buffer=new MemoryStream();capture.Save(buffer,System.Drawing.Imaging.ImageFormat.Png);
                var hit=Recognize(buffer.ToArray(),point,origin,1,cancellation);
                if(hit==null)return null;
                var surface=SurfaceFromImage(capture,origin,1,hit.Bounds);
                await Task.Delay(160,cancellation);
                return await IsSurfaceCurrentAsync(surface,cancellation)?hit with{Surface=surface}:null;
            }
            catch(Exception error)when(error is not OperationCanceledException){LastError="图片取词暂不可用，请检查 OCR 数据和组件是否完整。";return null;}
            finally{if(_disposed){_engine?.Dispose();_engine=null;}_ocrGate.Release();}
        },cancellation);
    }
    private async Task<TextProbe?> ProbeTextAsync(ScreenPoint point,CancellationToken cancellation)
    {
        // Keep at most one accessibility worker, including when a provider stops responding.
        if(_textProbe==null||_textProbe.IsCompleted){_textPoint=point;_textStarted=System.Diagnostics.Stopwatch.GetTimestamp();_textProbe=Task.Run(()=>ReadAccessibleText(point));}
        try
        {
            var probe=await _textProbe.WaitAsync(TimeSpan.FromMilliseconds(280),cancellation);
            return _textPoint==point&&System.Diagnostics.Stopwatch.GetElapsedTime(_textStarted)<TimeSpan.FromMilliseconds(750)?probe:null;
        }
        catch(TimeoutException){return null;}
        catch(Exception error)when(error is not OperationCanceledException){return null;}
    }
    private static bool ProviderFailure(Exception error)=>error is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException or NotSupportedException or NotImplementedException;
    private static TextProbe ReadAccessibleText(ScreenPoint point)
    {
        ScreenBox? clip=null;var semantic="文本";
        try
        {
            var element=AutomationElement.FromPoint(new System.Windows.Point(point.X,point.Y));
            var child=element;var parents=new List<AutomationElement>();
            for(var depth=0;element!=null&&depth<8;depth++,element=TreeWalker.ControlViewWalker.GetParent(element))
            {
                if(element.Current.IsPassword)return new(null,true);
                parents.Add(element);
                var kind=element.Current.ControlType;
                if(clip==null&&(kind==ControlType.Edit||kind==ControlType.Hyperlink))
                {
                    var r=element.Current.BoundingRectangle;var box=new ScreenBox(r.X,r.Y,r.Width,r.Height);
                    if(box.Contains(point)){clip=box;semantic=kind==ControlType.Edit?"文本框":"链接文字";}
                }
            }
            foreach(var parent in parents)
            {
                try
                {
                    if(!parent.TryGetCurrentPattern(TextPattern.Pattern,out var value))continue;
                    var pattern=(TextPattern)value;var ranges=new List<TextPatternRange>();
                    try{var range=pattern.RangeFromPoint(new System.Windows.Point(point.X,point.Y));range.ExpandToEnclosingUnit(TextUnit.Word);ranges.Add(range);}catch(Exception e)when(ProviderFailure(e)){}
                    if(ranges.Count==0&&child!=null&&parent!=child)
                        try{ranges.Add(pattern.RangeFromChild(child));}catch(Exception e)when(ProviderFailure(e)){}
                    if(ranges.Count==0)
                        try{ranges.AddRange(pattern.GetVisibleRanges().Take(20));}catch(Exception e)when(ProviderFailure(e)){}
                    foreach(var range in ranges)
                    {
                        try
                        {
                            var located=LocateText(range.GetText(2048),(start,length)=>
                            {
                                var fragment=range.Clone();fragment.MoveEndpointByRange(TextPatternRangeEndpoint.End,range,TextPatternRangeEndpoint.Start);
                                if(fragment.MoveEndpointByUnit(TextPatternRangeEndpoint.End,TextUnit.Character,start+length)!=start+length)return ("",[]);
                                if(start>0&&fragment.MoveEndpointByUnit(TextPatternRangeEndpoint.Start,TextUnit.Character,start)!=start)return ("",[]);
                                return(fragment.GetText(100),fragment.GetBoundingRectangles().Select(r=>new ScreenBox(r.X,r.Y,r.Width,r.Height)).ToArray());
                            },point,semantic,clip);
                            if(located.Suppress)return new(null,true,clip);
                            if(located.Hit!=null)return new(located.Hit,false,clip,semantic!="文本");
                        }
                        catch(Exception e)when(ProviderFailure(e)){}
                    }
                }
                catch(Exception e)when(ProviderFailure(e)){}
            }
        }
        catch(Exception e)when(ProviderFailure(e)){}
        return new(null,false,clip);
    }
    internal static (HoverWord? Hit,bool Suppress) LocateText(string text,Func<int,int,(string Text,ScreenBox[] Bounds)> locate,ScreenPoint point,string method,ScreenBox? clip=null)
    {
        foreach(var span in HoverWords.AccessibleSpans(text))
        {
            var fragment=locate(span.Start,span.Length);
            if(fragment.Text.Trim()!=text.Substring(span.Start,span.Length))continue;
            foreach(var box in fragment.Bounds)
            {
                if(!box.Contains(point)||clip is {} visible&&!visible.Contains(point))continue;
                if(span.IsChinese)return(null,true);
                if(span.Word!=null)return(new(span.Word,box,method),false);
            }
        }
        return(null,false);
    }
    internal static bool SameAccessibleWord(HoverSurface original,HoverWord? current)=>current!=null&&current.Word==original.Text&&current.Bounds==original.Bounds&&original.TextPoint is {} point&&current.Bounds.Contains(point);
    internal async Task<bool> IsSurfaceCurrentAsync(HoverSurface original,CancellationToken cancellation=default)
    {
        try
        {
            if(_disposed)return false;
            if(original.TextPoint is {} point&&original.Text!=null)
            {
                var current=await ProbeTextAsync(point,cancellation);
                return current is {SuppressOcr:false,ValidateText:true}&&SameAccessibleWord(original,current.Hit);
            }
            return HoverSurfaces.Same(original,await Task.Run(()=>CaptureSurface(original.Bounds,padded:false),cancellation));
        }
        catch(OperationCanceledException){throw;}
        catch{return false;}
    }
    private static HoverSurface? CaptureSurface(ScreenBox bounds,bool padded=true)
    {
        if(!double.IsFinite(bounds.X)||!double.IsFinite(bounds.Y)||bounds.Width is <=0 or >1800||bounds.Height is <=0 or >400)return null;
        var prior=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            var screen=System.Windows.Forms.Screen.FromPoint(new Point((int)(bounds.X+bounds.Width/2),(int)(bounds.Y+bounds.Height/2))).Bounds;
            var padding=padded?3:0;
            var left=Math.Max(screen.Left,(int)Math.Floor(bounds.X)-padding);var top=Math.Max(screen.Top,(int)Math.Floor(bounds.Y)-padding);
            var right=Math.Min(screen.Right,(int)Math.Ceiling(bounds.X+bounds.Width)+padding);var bottom=Math.Min(screen.Bottom,(int)Math.Ceiling(bounds.Y+bounds.Height)+padding);
            if(right<=left||bottom<=top)return null;
            using var image=new Bitmap(right-left,bottom-top,PixelFormat.Format24bppRgb);
            using(var graphics=Graphics.FromImage(image))graphics.CopyFromScreen(left,top,0,0,image.Size,CopyPixelOperation.SourceCopy);
            return new(new(left,top,image.Width,image.Height),Samples(image,new RectangleF(0,0,image.Width,image.Height)));
        }
        finally{if(prior!=IntPtr.Zero)SetThreadDpiAwarenessContext(prior);}
    }
    internal static HoverSurface SurfaceFromImage(Bitmap image,ScreenPoint origin,double scale,ScreenBox bounds)
    {
        var left=Math.Max(origin.X,Math.Floor(bounds.X)-3);var top=Math.Max(origin.Y,Math.Floor(bounds.Y)-3);
        var right=Math.Min(origin.X+image.Width/scale,Math.Ceiling(bounds.X+bounds.Width)+3);var bottom=Math.Min(origin.Y+image.Height/scale,Math.Ceiling(bounds.Y+bounds.Height)+3);
        var rectangle=new RectangleF((float)((left-origin.X)*scale),(float)((top-origin.Y)*scale),(float)((right-left)*scale),(float)((bottom-top)*scale));
        return new(new(left,top,right-left,bottom-top),Samples(image,rectangle));
    }
    internal static HoverSurface SurfaceFromPng(byte[] png,ScreenPoint origin,double scale,ScreenBox bounds)
    {
        using var input=new MemoryStream(png);using var image=new Bitmap(input);return SurfaceFromImage(image,origin,scale,bounds);
    }
    private static byte[] Samples(Bitmap image,RectangleF rectangle)
    {
        using var sample=new Bitmap(24,12,PixelFormat.Format24bppRgb);
        using(var graphics=Graphics.FromImage(sample)){graphics.InterpolationMode=InterpolationMode.HighQualityBilinear;graphics.DrawImage(image,new RectangleF(0,0,24,12),rectangle,GraphicsUnit.Pixel);}
        var values=new byte[24*12];
        for(var y=0;y<12;y++)for(var x=0;x<24;x++){var pixel=sample.GetPixel(x,y);values[y*24+x]=(byte)((pixel.R*77+pixel.G*150+pixel.B*29)>>8);}
        return values;
    }
    private static Bitmap Capture(ScreenPoint point,out ScreenPoint origin,ScreenBox? clip=null)
    {
        var prior=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            var screen=System.Windows.Forms.Screen.FromPoint(new Point((int)point.X,(int)point.Y)).Bounds;
            var width=Math.Min(620,screen.Width);var height=Math.Min(170,screen.Height);
            var x=Math.Clamp((int)point.X-width/2,screen.Left,screen.Right-width);var y=Math.Clamp((int)point.Y-height/2,screen.Top,screen.Bottom-height);
            if(clip is {} field&&field.Contains(point))
            {
                var right=Math.Min(x+width,(int)Math.Ceiling(field.X+field.Width));var bottom=Math.Min(y+height,(int)Math.Ceiling(field.Y+field.Height));
                x=Math.Max(x,(int)Math.Floor(field.X));y=Math.Max(y,(int)Math.Floor(field.Y));width=Math.Max(1,right-x);height=Math.Max(1,bottom-y);
            }
            origin=new(x,y);
            var raw=new Bitmap(width,height,PixelFormat.Format24bppRgb);
            try
            {
                using(var graphics=Graphics.FromImage(raw))graphics.CopyFromScreen(x,y,0,0,new Size(width,height),CopyPixelOperation.SourceCopy);
                return raw;
            }
            catch{raw.Dispose();throw;}
        }
        finally{if(prior!=IntPtr.Zero)SetThreadDpiAwarenessContext(prior);}
    }
    internal HoverWord? Recognize(byte[] png,ScreenPoint point,ScreenPoint origin,double scale,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        _engine??=new TesseractEngine(Path.Combine(Environment.GetEnvironmentVariable("MEMOLEX_DATA_DIR")??Path.Combine(ApplicationPaths.RuntimeDirectory,"data"),"ocr"),"eng",EngineMode.LstmOnly);
        using var input=new MemoryStream(png);using var original=new Bitmap(input);
        HoverWord? Read(Pix image,double imageScale,PageSegMode mode)
        {
            cancellation.ThrowIfCancellationRequested();
            using var page=_engine.Process(image,mode);using var iterator=page.GetIterator();iterator.Begin();var words=new List<HoverWord>();
            do
            {
                cancellation.ThrowIfCancellationRequested();
                if(!iterator.TryGetBoundingBox(PageIteratorLevel.Word,out var rectangle))continue;
                var box=new ScreenBox(origin.X+rectangle.X1/imageScale,origin.Y+rectangle.Y1/imageScale,rectangle.Width/imageScale,rectangle.Height/imageScale);
                var word=HoverWords.Normalize(iterator.GetText(PageIteratorLevel.Word)??"");
                if(word!=null)words.Add(new(word,box,"图片识别",iterator.GetConfidence(PageIteratorLevel.Word)));
            }while(iterator.Next(PageIteratorLevel.Word));
            return HoverWords.AtPointer(words,point);
        }
        Pix Prepare(double imageScale)
        {
            using var image=new Bitmap(Math.Max(1,(int)Math.Round(original.Width*imageScale/scale)),Math.Max(1,(int)Math.Round(original.Height*imageScale/scale)),PixelFormat.Format24bppRgb);
            using(var graphics=Graphics.FromImage(image)){graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.DrawImage(original,new Rectangle(0,0,image.Width,image.Height));}
            using var buffer=new MemoryStream();image.Save(buffer,System.Drawing.Imaging.ImageFormat.Png);return Pix.LoadFromMemory(buffer.ToArray());
        }
        // All fallback passes inspect the same frozen capture; moving video never causes a later retry.
        using(var image=Prepare(Math.Max(2,scale)))
        {
            var hit=Read(image,Math.Max(2,scale),PageSegMode.SparseText)??Read(image,Math.Max(2,scale),PageSegMode.Auto);
            if(hit!=null){LastError=null;return hit;}
        }
        if(scale<3)
        {
            using var image=Prepare(3);var hit=Read(image,3,PageSegMode.Auto);
            if(hit!=null){LastError=null;return hit;}
        }
        return null;
    }
    public void Dispose()
    {
        _disposed=true;
        // The OCR task owns the engine until it releases this gate.
        if(_ocrGate.Wait(0)){try{_engine?.Dispose();_engine=null;}finally{_ocrGate.Release();}}
    }
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
