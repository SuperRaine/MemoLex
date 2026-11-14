using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Automation.Text;
using System.Windows.Interop;
using MemoLex.Core;
using MemoLex.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var directory=Path.GetFullPath(args.FirstOrDefault()??"work/MemoLex/hover-review-1.1.4");Directory.CreateDirectory(directory);
        var results=new List<object>();var passed=0;using var reader=new ScreenWordReader();
        foreach(var size in new[]{10d,12d,14d,18d})
        foreach(var family in new[]{"Arial","Times New Roman"})
        {
            var prefix="A reader gains ";const string target="insight";var text=prefix+target+" from careful reading.";
            FormattedText Format(string value)=>new(value,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(family),size,Brushes.Black,1);
            var visual=new DrawingVisual();using(var draw=visual.RenderOpen())
            {
                draw.DrawRectangle(Brushes.White,null,new Rect(0,0,620,170));
                for(var row=0;row<6;row++)draw.DrawText(Format(text),new Point(20,15+row*(size+7)));
            }
            var raw=new RenderTargetBitmap(620,170,96,96,PixelFormats.Pbgra32);raw.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(raw));using var buffer=new MemoryStream();encoder.Save(buffer);var png=buffer.ToArray();
            var bounds=Format(target).BuildGeometry(new Point(20+Format(prefix).WidthIncludingTrailingWhitespace,15+2*(size+7))).Bounds;
            var points=new[]{new ScreenPoint(bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2),new ScreenPoint(bounds.X+bounds.Width/2,bounds.Bottom+2)};
            for(var position=0;position<points.Length;position++)
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();var hit=reader.Recognize(png,points[position],new(0,0),1);
                var ok=hit?.Word==target&&hit.Bounds.Contains(points[position]);
                results.Add(new {size,family,position,word=hit?.Word,confidence=hit?.Confidence,ok,milliseconds=watch.ElapsedMilliseconds});
                if(!ok)throw new Exception($"Static English sample missed: {family} {size}px position {position}");
                passed++;
            }
            File.WriteAllBytes(Path.Combine(directory,$"sample-{family.Replace(' ','-')}-{size}.png"),png);
        }
        foreach(var text in new[]{"中文","洞察力","川口","学习数据自动同步","请把单词加入我的词本"})
        {
            var visual=new DrawingVisual();using(var draw=visual.RenderOpen())
            {
                draw.DrawRectangle(Brushes.White,null,new Rect(0,0,620,170));
                draw.DrawText(new FormattedText(text,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei"),24,Brushes.Black,1),new Point(20,40));
            }
            var raster=new RenderTargetBitmap(620,170,96,96,PixelFormats.Pbgra32);raster.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(raster));using var buffer=new MemoryStream();encoder.Save(buffer);
            if(reader.Recognize(buffer.ToArray(),new(38,58),new(0,0),1)!=null)throw new Exception("Chinese sample was returned as English.");passed++;
        }
        foreach(var target in new[]{"talkative","environment","through","to","in","of","computer"})
        foreach(var size in new[]{12d,16d})
        foreach(var dark in new[]{false,true})
        {
            const string prefix="Readers find ";var foreground=dark?Brushes.White:Brushes.Black;
            FormattedText Format(string text)=>new(text,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Arial"),size,foreground,1);
            var visual=new DrawingVisual();using(var draw=visual.RenderOpen())
            {
                draw.DrawRectangle(dark?new SolidColorBrush(Color.FromRgb(32,32,32)):Brushes.White,null,new Rect(0,0,620,170));
                for(var row=0;row<5;row++)draw.DrawText(Format(prefix+target+" while reading these examples."),new Point(20,18+row*(size+7)));
            }
            var raster=new RenderTargetBitmap(620,170,96,96,PixelFormats.Pbgra32);raster.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(raster));using var buffer=new MemoryStream();encoder.Save(buffer);
            var box=Format(target).BuildGeometry(new Point(20+Format(prefix).WidthIncludingTrailingWhitespace,18+2*(size+7))).Bounds;var point=new ScreenPoint(box.X+box.Width/2,box.Y+box.Height/2);
            var hit=reader.Recognize(buffer.ToArray(),point,new(0,0),1);var ok=hit?.Word==target;
            results.Add(new{target,size,dark,word=hit?.Word,confidence=hit?.Confidence,ok});
            if(!ok&&!args.Contains("--diagnose"))throw new Exception($"English contextual sample missed {target} {size}px dark={dark}");
            if(ok)passed++;
        }
        foreach(var field in new[]{false,true})
        foreach(var size in new[]{12d,16d})
        {
            const string target="environment";const string prefix="Find ";
            FormattedText Format(string text)=>new(text,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Arial"),size,field?Brushes.Black:Brushes.Blue,1);
            var label=Format(prefix+target+" while reading");if(!field)label.SetTextDecorations(TextDecorations.Underline);
            var visual=new DrawingVisual();using(var draw=visual.RenderOpen())
            {
                draw.DrawRectangle(Brushes.White,null,new Rect(0,0,620,100));
                if(field)draw.DrawRoundedRectangle(Brushes.White,new Pen(Brushes.LightBlue,1),new Rect(10,15,540,48),5,5);
                draw.DrawText(label,new Point(24,28));
                if(field)draw.DrawLine(new Pen(Brushes.Black,1),new Point(24+label.Width,29),new Point(24+label.Width,29+size));
            }
            var raster=new RenderTargetBitmap(620,100,96,96,PixelFormats.Pbgra32);raster.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(raster));using var buffer=new MemoryStream();encoder.Save(buffer);var png=buffer.ToArray();
            var box=Format(target).BuildGeometry(new Point(24+Format(prefix).WidthIncludingTrailingWhitespace,28)).Bounds;
            var hit=reader.Recognize(png,new(box.X+box.Width/2,box.Y+box.Height/2),new(0,0),1);
            if(hit?.Word!=target)throw new Exception($"Underlined link / field OCR failed: field={field}, size={size}, word={hit?.Word}");
            passed++;results.Add(new{kind=field?"text-field":"underlined-link",size,word=hit.Word,ok=true});File.WriteAllBytes(Path.Combine(directory,$"sample-{(field?"field":"link")}-{size}.png"),png);
        }
        var accessibility=VerifyControlText();
        File.WriteAllText(Path.Combine(directory,"results.json"),JsonSerializer.Serialize(new{passed,accessibility,samples=results},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"PASS {passed} static OCR samples and {accessibility} accessibility checks, including underlined links and editable fields.");return 0;
    }
    private static int VerifyControlText()
    {
        var checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        var input=new TextBox{Text="Readers find environment and insight.",FontFamily=new FontFamily("Arial"),FontSize=16,Padding=new Thickness(12),VerticalContentAlignment=VerticalAlignment.Center,Height=54};
        input.Select(2,4);var link=new Hyperlink(new Run("Find environment and insight"));var clicks=0;link.Click+=(_,_)=>clicks++;
        var rich=new RichTextBox{Document=new FlowDocument(new Paragraph(link)),FontSize=16,Height=70,IsReadOnly=true};
        var root=new StackPanel();root.Children.Add(input);root.Children.Add(rich);
        using var host=new HwndSource(new HwndSourceParameters("MemoLex provider fixture"){WindowStyle=unchecked((int)0x80000000),Width=620,Height=170,PositionX=-32000,PositionY=-32000});host.RootVisual=root;root.Measure(new Size(620,170));root.Arrange(new Rect(0,0,620,170));root.UpdateLayout();
        void Verify(ITextProvider provider,string method)
        {
            var range=provider.DocumentRange;var text=range.GetText(2048);var start=text.IndexOf("environment",StringComparison.Ordinal);
            (string Text,ScreenBox[] Bounds) Locate(int index,int length)
            {
                var fragment=range.Clone();fragment.MoveEndpointByRange(TextPatternRangeEndpoint.End,range,TextPatternRangeEndpoint.Start);
                fragment.MoveEndpointByUnit(TextPatternRangeEndpoint.End,TextUnit.Character,index+length);if(index>0)fragment.MoveEndpointByUnit(TextPatternRangeEndpoint.Start,TextUnit.Character,index);
                var values=fragment.GetBoundingRectangles();return(fragment.GetText(100),Enumerable.Range(0,values.Length/4).Select(i=>new ScreenBox(values[4*i],values[4*i+1],values[4*i+2],values[4*i+3])).ToArray());
            }
            var fragment=Locate(start,"environment".Length);Check(fragment.Bounds.Length>0,"Own WPF provider returned no character bounds");var b=fragment.Bounds[0];var point=new ScreenPoint(b.X+b.Width/2,b.Y+b.Height/2);
            var result=ScreenWordReader.LocateText(text,Locate,point,method);
            Check(result.Hit?.Word=="environment"&&result.Hit.Bounds.Contains(point),"Multiword link / field did not locate the word actually under the pointer");
            Check(ScreenWordReader.LocateText(text,Locate,new(point.X+800,point.Y),method).Hit==null,"Field padding resolved to a nearby word");
            var surface=new HoverSurface(b,[]){Text="environment",TextPoint=point};
            Check(ScreenWordReader.SameAccessibleWord(surface,result.Hit),"Unchanged accessible text was rejected by caret or link styling validation");
            Check(!ScreenWordReader.SameAccessibleWord(surface,result.Hit! with{Word="insight"}),"Changed field text reused an old hover result");
        }
        Verify((ITextProvider)new TextBoxAutomationPeer(input).GetPattern(PatternInterface.Text),"文本框");
        Verify((ITextProvider)new RichTextBoxAutomationPeer(rich).GetPattern(PatternInterface.Text),"链接文字");
        Check(input.Text=="Readers find environment and insight."&&input.SelectionStart==2&&input.SelectionLength==4&&clicks==0,"Reading changed text, selection or invoked a link");
        var chinese=ScreenWordReader.LocateText("中文",(start,length)=>("中文",new[]{new ScreenBox(0,0,20,20)}),new(10,10),"文本框");
        Check(chinese.Hit==null&&chinese.Suppress,"Accessible Chinese reached English OCR fallback");
        host.RootVisual=null;return checks;
    }
}
