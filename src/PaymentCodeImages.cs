using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MemoLex;

internal static class PaymentCodeImages
{
    internal static BitmapSource Load(string asset)
    {
        var source=new BitmapImage();source.BeginInit();source.CacheOption=BitmapCacheOption.OnLoad;
        source.UriSource=new Uri("pack://application:,,,/MemoLex;component/Assets/"+asset);source.EndInit();source.Freeze();
        var bounds=asset switch
        {
            "sponsor-wechat.jpg"=>new Int32Rect(354,438,568,568),
            "sponsor-alipay.jpg"=>new Int32Rect(216,643,742,742),
            _=>throw new ArgumentException("未知的收款码资源。",nameof(asset))
        };
        var cropped=new CroppedBitmap(source,bounds);cropped.Freeze();
        // Add a clean quiet zone around the unchanged QR pixels. The surrounding
        // poster and account-name caption are never included in the displayed code.
        var padding=(int)Math.Ceiling(bounds.Width*0.1);var size=bounds.Width+padding*2;
        var drawing=new DrawingVisual();RenderOptions.SetBitmapScalingMode(drawing,BitmapScalingMode.NearestNeighbor);
        using(var context=drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.White,null,new Rect(0,0,size,size));
            context.DrawImage(cropped,new Rect(padding,padding,bounds.Width,bounds.Height));
        }
        var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);bitmap.Freeze();return bitmap;
    }
}
