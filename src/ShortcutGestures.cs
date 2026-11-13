using System.Globalization;
using System.Windows.Input;
using MemoLex.Core;

namespace MemoLex;

internal static class ShortcutGestures
{
    internal static KeyGesture? Parse(string? text) => string.IsNullOrWhiteSpace(text)?null:(KeyGesture?)new KeyGestureConverter().ConvertFromInvariantString(text);
    internal static string Text(KeyGesture gesture)=>(string)new KeyGestureConverter().ConvertTo(null,CultureInfo.InvariantCulture,gesture,typeof(string))!;
    internal static KeyGesture? Get(Preferences preferences,string id)
    {
        var definition=ShortcutDefinitions.All.First(item=>item.Id==id);
        try{return Parse(preferences.Shortcuts.GetValueOrDefault(id,definition.Default));}
        catch{return Parse(definition.Default);}
    }
    internal static string? Match(Preferences preferences,Key key,ModifierKeys modifiers) => ShortcutDefinitions.All.Where(item=>item.Id!="clipboard")
        .FirstOrDefault(item=>Get(preferences,item.Id) is { } gesture&&gesture.Key==key&&gesture.Modifiers==modifiers)?.Id;
    internal static bool Normalize(IReadOnlyDictionary<string,string> values,out Dictionary<string,string> normalized,out string? error)
    {
        normalized=[];error=null;var used=new HashSet<string>();
        foreach(var item in ShortcutDefinitions.All)
        {
            try
            {
                var gesture=Parse(values.GetValueOrDefault(item.Id,item.Default));
                if(gesture==null){normalized[item.Id]="";continue;}
                var text=Text(gesture);
                if(!used.Add(text)){error="快捷键重复："+text;return false;}
                normalized[item.Id]=text;
            }
            catch{error=item.Label+"的快捷键无效。";return false;}
        }
        return true;
    }
    internal static void Capture(object sender,KeyEventArgs e)
    {
        if(sender is not System.Windows.Controls.TextBox input)return;
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)return;
        e.Handled=true;
        if(key is Key.Delete or Key.Back && Keyboard.Modifiers==ModifierKeys.None){input.Text="";return;}
        try{input.Text=Text(new KeyGesture(key,Keyboard.Modifiers));}
        catch{input.ToolTip="请使用 Ctrl、Alt、Shift 等组合键，或功能键。";}
    }
}
