using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MemoLex.Core;

namespace MemoLex;

public partial class MainWindow
{
    private bool _editingSettings;
    internal static IReadOnlyList<(int Id,string Command)> GlobalShortcuts {get;} = [(0x4D4C,"clipboard"),(0x4D4D,"search"),(0x4D4E,"screenshot")];
    private readonly HashSet<int> _registeredHotkeys=[];
    private static uint NativeModifiers(ModifierKeys modifiers) => 0x4000u | ((modifiers&ModifierKeys.Alt)!=0?1u:0) | ((modifiers&ModifierKeys.Control)!=0?2u:0)
        | ((modifiers&ModifierKeys.Shift)!=0?4u:0) | ((modifiers&ModifierKeys.Windows)!=0?8u:0);
    private void ReleaseGlobalShortcut()
    {
        var handle=new WindowInteropHelper(this).Handle;
        foreach(var id in _registeredHotkeys)UnregisterHotKey(handle,id);
        _registeredHotkeys.Clear();
    }
    private void RefreshGlobalShortcut()
    {
        if(_source==null||!_desktopIntegration)return;
        ReleaseGlobalShortcut();
        foreach(var (id,command) in GlobalShortcuts)
        {
            var gesture=ShortcutGestures.Get(_preferences,command);
            if(gesture==null)continue;
            if(RegisterHotKey(new WindowInteropHelper(this).Handle,id,NativeModifiers(gesture.Modifiers),(uint)KeyInterop.VirtualKeyFromKey(gesture.Key)))_registeredHotkeys.Add(id);
            else StatusText.Text=$"{ShortcutDefinitions.All.First(item=>item.Id==command).Label}已被占用，请在「设置 → 快捷键」中更换。";
        }
    }
    private string? ValidateGlobalShortcut(Dictionary<string,string> values)
    {
        if(_source==null||!_desktopIntegration)return null;
        var handle=new WindowInteropHelper(this).Handle;
        var registered=new List<int>();
        try
        {
            foreach(var (id,command) in GlobalShortcuts)
            {
                var gesture=ShortcutGestures.Parse(values[command]);
                if(gesture==null)continue;
                if(!RegisterHotKey(handle,id,NativeModifiers(gesture.Modifiers),(uint)KeyInterop.VirtualKeyFromKey(gesture.Key)))
                    return ShortcutDefinitions.All.First(item=>item.Id==command).Label+"已被占用，请换一个组合键。";
                registered.Add(id);
            }
            return null;
        }
        finally{foreach(var id in registered)UnregisterHotKey(handle,id);}
    }
    private string ShortcutLabel(string id) => ShortcutGestures.Get(_preferences,id) is { } gesture?ShortcutGestures.Text(gesture):"未设置";
    private void UpdateShortcutHints()
    {
        SearchShortcutHint.Text=ShortcutLabel("search");
        HistoryButton.ToolTip="搜词历史（"+ShortcutLabel("history")+"）";
        TranslationModeButton.ToolTip="段落翻译（"+ShortcutLabel("translation")+"）";
        ScreenshotButton.ToolTip="框选英文截图并翻译（"+ShortcutLabel("screenshot")+"）";
        TranslateButton.ToolTip="开始翻译（"+ShortcutLabel("translate")+"）";
        AddButton.ToolTip="加入 / 移除当前云词本（"+ShortcutLabel("toggleWord")+"）";
        UpdateNavigationButtons();
    }
    private async Task ExecuteShortcutAsync(string command)
    {
        switch(command)
        {
            case "back":await MoveNavigationAsync(-1);break;
            case "forward":await MoveNavigationAsync(1);break;
            case "search":OpenQuickLookupBar();break;
            case "history":HistoryButton_Click(this,new RoutedEventArgs());break;
            case "toggleWord":if(_viewTarget is ViewTarget.Word or ViewTarget.Examples)await AddCurrentWordAsync();break;
            case "translation":TranslationModeButton_Click(this,new RoutedEventArgs());break;
            case "screenshot":await StartScreenshotTranslationAsync();break;
            case "translate":if(_viewTarget==ViewTarget.Translation)await TranslateParagraphAsync();else TranslationModeButton_Click(this,new RoutedEventArgs());break;
        }
    }
}
