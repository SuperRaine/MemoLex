using System.Windows;
using System.Windows.Controls;
using MemoLex.Core;

namespace MemoLex.Desktop;

internal static class HoverSettingsPanel
{
    internal static FrameworkElement Build(Window dialog,Preferences preferences,out Func<string?> apply)
    {
        var panel=new StackPanel();var options=new Grid();options.ColumnDefinitions.Add(new());options.ColumnDefinitions.Add(new());panel.Children.Add(options);
        CheckBox Option(string label,bool value,int row,int column,string name)
        {
            while(options.RowDefinitions.Count<=row)options.RowDefinitions.Add(new(){Height=GridLength.Auto});
            var input=new CheckBox{Content=new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap},IsChecked=value,Margin=new Thickness(0,0,14,8),MinHeight=28,VerticalAlignment=VerticalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center};Grid.SetRow(input,row);Grid.SetColumn(input,column);options.Children.Add(input);dialog.RegisterName(name,input);return input;
        }
        var enabled=Option("开启鼠标自动取词",preferences.HoverLookupEnabled,0,0,"HoverLookupEnabled");
        var translucent=Option("取词浮窗半透明",preferences.HoverTranslucent,0,1,"HoverTranslucent");
        var autoAdd=Option("找到单词后自动加入云词本",preferences.HoverAutoAdd,1,1,"HoverAutoAdd");
        var nearest=Option("未找到释义时显示相近单词",preferences.HoverNearest,1,0,"HoverNearest");
        nearest.ToolTip="推荐拼写相近的单词，例如 enviroment → environment。仅供参考，不会自动收藏。";
        var grid=new Grid{Margin=new Thickness(0,8,0,12)};grid.ColumnDefinitions.Add(new(){Width=new GridLength(105)});grid.ColumnDefinitions.Add(new());panel.Children.Add(grid);
        TextBlock InlineText(string text)=>new(){Text=text,VerticalAlignment=VerticalAlignment.Center,Foreground=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100,126,147))};
        void Label(string label,int row){while(grid.RowDefinitions.Count<=row)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});var text=InlineText(label);text.Margin=new Thickness(0,0,12,12);Grid.SetRow(text,row);grid.Children.Add(text);}
        Label("取词方式",0);var mode=new WrapPanel{Margin=new Thickness(0,0,0,12),ItemHeight=36};mode.Children.Add(InlineText("鼠标悬停  ＋  "));
        var ctrl=new CheckBox{Content="Ctrl",IsChecked=(preferences.HoverModifiers&1)!=0,Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};
        var alt=new CheckBox{Content="Alt",IsChecked=(preferences.HoverModifiers&2)!=0,Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};
        var shift=new CheckBox{Content="Shift",IsChecked=(preferences.HoverModifiers&4)!=0,VerticalAlignment=VerticalAlignment.Center};mode.Children.Add(ctrl);mode.Children.Add(alt);mode.Children.Add(shift);Grid.SetColumn(mode,1);grid.Children.Add(mode);
        Label("取词延迟",1);var delayRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12),Height=36};var delay=new TextBox{Text=Math.Clamp(preferences.HoverDelayMs,100,3000).ToString(),Width=100,Height=36,Padding=new Thickness(10,6,10,6),MaxLength=4};delayRow.Children.Add(delay);var unit=InlineText("毫秒（100–3000）");unit.Margin=new Thickness(10,0,0,0);delayRow.Children.Add(unit);Grid.SetRow(delayRow,1);Grid.SetColumn(delayRow,1);grid.Children.Add(delayRow);
        Label("浮窗字体大小",2);var sizes=new[]{new FontChoice("较小","small"),new FontChoice("标准","standard"),new FontChoice("较大","large")};var font=new ComboBox{ItemsSource=sizes,SelectedItem=sizes.FirstOrDefault(x=>x.Value==preferences.HoverFontSize)??sizes[1],MinWidth=180,Height=36,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,12),VerticalAlignment=VerticalAlignment.Center};Grid.SetRow(font,2);Grid.SetColumn(font,1);grid.Children.Add(font);
        panel.Children.Add(InputDialog.Text("将鼠标停在英文单词上即可取词，移开后浮窗自动收起。开启组合键后，取词时需同时按住。图片在本机识别。"));
        var hint=InputDialog.Text("自动收藏前，请先登录并选择云词本。仅收藏准确匹配的单词。" );hint.Margin=new Thickness(0,8,0,0);panel.Children.Add(hint);
        dialog.RegisterName("HoverDelayMs",delay);dialog.RegisterName("HoverFontSize",font);dialog.RegisterName("HoverCtrl",ctrl);dialog.RegisterName("HoverAlt",alt);dialog.RegisterName("HoverShift",shift);
        apply=()=>
        {
            preferences.HoverLookupEnabled=enabled.IsChecked==true;preferences.HoverTranslucent=translucent.IsChecked==true;preferences.HoverAutoAdd=autoAdd.IsChecked==true;preferences.HoverNearest=nearest.IsChecked==true;
            preferences.HoverFontSize=((FontChoice)font.SelectedItem).Value;preferences.HoverModifiers=(ctrl.IsChecked==true?1:0)|(alt.IsChecked==true?2:0)|(shift.IsChecked==true?4:0);
            if(!int.TryParse(delay.Text,out var milliseconds)||milliseconds is <100 or >3000)return "取词延迟需为 100–3000 毫秒。";
            preferences.HoverDelayMs=milliseconds;
            return null;
        };
        return panel;
    }
    private sealed record FontChoice(string Label,string Value){public override string ToString()=>Label;}
}
