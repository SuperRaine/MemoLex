using System.IO;
using System.Windows;
using MemoLex.Desktop;
using MemoLex.Core;

namespace MemoLex;
public partial class MainWindow
{
    private bool _screenTranslating;
    internal Func<Task<string?>>? CaptureTranslationText {get;set;}
    private async void ScreenshotButton_Click(object sender,RoutedEventArgs e)=>await StartScreenshotTranslationAsync();
    internal async Task StartScreenshotTranslationAsync()
    {
        if(_screenTranslating||_editingSettings||!_ready||OwnedWindows.Cast<Window>().Any(window=>window.IsVisible))return;
        _screenTranslating=true;var wasActive=IsVisible&&IsActive;var originalHover=_preferences.HoverLookupEnabled;
        if(_hover!=null)_hover.Enabled=false;
        try
        {
            string? text;
            if(CaptureTranslationText!=null)text=await CaptureTranslationText();
            else
            {
                if(wasActive){Hide();await Task.Delay(100);}
                using var image=ScreenTranslationCapture.Select();
                if(image==null){if(wasActive)RestoreWindow();return;}
                RestoreWindow();StatusText.Text="正在识别截图中的英文…";
                using var buffer=new MemoryStream();image.Save(buffer,System.Drawing.Imaging.ImageFormat.Png);var png=buffer.ToArray();
                text=await Task.Run(()=>ScreenTextReader.ReadEnglish(png));
            }
            if(text==null)return;
            if(_desktopIntegration)RestoreWindow();
            if(string.IsNullOrWhiteSpace(text)){StatusText.Text="没有识别到清晰英文，请重新框选文字区域。";return;}
            if(HoverWords.Normalize(text) is {} word){await LoadWordAsync(word);return;}
            await TranslateClipboardAsync(text,"en|zh-CN","截图翻译");
        }
        catch(Exception error){if(_desktopIntegration)RestoreWindow();StatusText.Text=error is ArgumentException or InvalidOperationException?error.Message:"截图翻译未完成，请重新框选。";}
        finally{_screenTranslating=false;if(_hover!=null)_hover.Enabled=originalHover;}
    }
}
