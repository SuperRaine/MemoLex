using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Tesseract;
using MemoLex.Core;

namespace MemoLex.Desktop;

internal static class ScreenTextReader
{
    internal static string ReadEnglish(byte[] png,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var input=new MemoryStream(png);using var original=new Bitmap(input);
        if((long)original.Width*original.Height>12_000_000)throw new ArgumentException("选区太大，请缩小后再试。");
        var scale=original.Width<1800&&original.Height<1200?2:1;
        using var image=new Bitmap(original.Width*scale,original.Height*scale,PixelFormat.Format24bppRgb);
        using(var graphics=Graphics.FromImage(image)){graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.DrawImage(original,new Rectangle(Point.Empty,image.Size));}
        using var buffer=new MemoryStream();image.Save(buffer,System.Drawing.Imaging.ImageFormat.Png);
        using var engine=new TesseractEngine(Path.Combine(Environment.GetEnvironmentVariable("MEMOLEX_DATA_DIR")??Path.Combine(ApplicationPaths.RuntimeDirectory,"data"),"ocr"),"eng",EngineMode.LstmOnly);
        using var pix=Pix.LoadFromMemory(buffer.ToArray());using var page=engine.Process(pix,PageSegMode.Auto);
        cancellation.ThrowIfCancellationRequested();var text=page.GetText().Replace("\r\n","\n").Trim();
        if(page.GetMeanConfidence()<0.5||text.Count(char.IsAsciiLetter)<3)return "";
        if(text.Length>5000)throw new ArgumentException("识别文字超过 5000 字符，请缩小截图范围。");
        return text;
    }
}
