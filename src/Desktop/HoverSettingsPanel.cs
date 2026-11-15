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
            var input=new CheckBox{Content=new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap},IsChecked=value,Margin=new Thickness(0,0,14,15),VerticalContentAlignment=VerticalAlignment.Top};Grid.SetRow(input,row);Grid.SetColumn(input,column);options.Children.Add(input);dialog.RegisterName(name,input);return input;
        }
        var enabled=Option("开启鼠标自动取词",preferences.HoverLookupEnabled,0,0,"HoverLookupEnabled");
        var translucent=Option("取词浮窗半透明",preferences.HoverTranslucent,0,1,"HoverTranslucent");
        var autoAdd=Option("找到单词后自动加入云词本",preferences.HoverAutoAdd,1,1,"HoverAutoAdd");
        var nearest=Option("未找到释义时显示相近单词",preferences.HoverNearest,1,0,"HoverNearest");
        nearest.ToolTip="推荐拼写相近的单词，例如 enviroment → environment。仅供参考，不会自动收藏。";
        var grid=new Grid{Margin=new Thickness(0,8,0,12)};grid.ColumnDefinitions.Add(new(){Width=new GridLength(105)});grid.ColumnDefinitions.Add(new());panel.Children.Add(grid);
        void Label(string label,int row){while(grid.RowDefinitions.Count<=row)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});var text=InputDialog.Text(label);text.Margin=new Thickness(0,9,12,18);text.VerticalAlignment=VerticalAlignment.Center;Grid.SetRow(text,row);grid.Children.Add(text);}
        Label("取词方式",0);var mode=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,5,0,16)};mode.Children.Add(InputDialog.Text("鼠标悬停  ＋  "));
        var ctrl=new CheckBox{Content="Ctrl",IsChecked=(preferences.HoverModifiers&1)!=0,Margin=new Thickness(0,0,12,0)};
        var alt=new CheckBox{Content="Alt",IsChecked=(preferences.HoverModifiers&2)!=0,Margin=new Thickness(0,0,12,0)};
        var shift=new CheckBox{Content="Shift",IsChecked=(preferences.HoverModifiers&4)!=0};mode.Children.Add(ctrl);mode.Children.Add(alt);mode.Children.Add(shift);Grid.SetColumn(mode,1);grid.Children.Add(mode);
        Label("取词延迟",1);var delayRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};var delay=new TextBox{Text=Math.Clamp(preferences.HoverDelayMs,100,3000).ToString(),Width=100,MaxLength=4};delayRow.Children.Add(delay);var unit=InputDialog.Text("毫秒（100–3000）");unit.Margin=new Thickness(10,0,0,0);unit.VerticalAlignment=VerticalAlignment.Center;delayRow.Children.Add(unit);Grid.SetRow(delayRow,1);Grid.SetColumn(delayRow,1);grid.Children.Add(delayRow);
        Label("浮窗字体大小",2);var sizes=new[]{new FontChoice("较小","small"),new FontChoice("标准","standard"),new FontChoice("较大","large")};var font=new ComboBox{ItemsSource=sizes,SelectedItem=sizes.FirstOrDefault(x=>x.Value==preferences.HoverFontSize)??sizes[1],MinWidth=180,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,12)};Grid.SetRow(font,2);Grid.SetColumn(font,1);grid.Children.Add(font);
        panel.Children.Add(InputDialog.Text("移动鼠标后悬停取词；勾选组合键后需按住所选键。每次停留只识别一次，画面变化时自动收起。只识别英文，图片在本机处理；模糊或不确定的字符会跳过。"));
        var hint=InputDialog.Text("自动加入仅作用于准确匹配的英文词，需先登录并选择云词本；近似词不会自动加入。" );hint.Margin=new Thickness(0,14,0,0);panel.Children.Add(hint);
        dialog.RegisterName("HoverDelayMs",delay);dialog.RegisterName("HoverFontSize",font);dialog.RegisterName("HoverCtrl",ctrl);dialog.RegisterName("HoverAlt",alt);dialog.RegisterName("HoverShift",shift);
        apply=()=>
        {
            if(!int.TryParse(delay.Text,out var milliseconds)||milliseconds is <100 or >3000)return "取词延迟需为 100–3000 毫秒。";
            preferences.HoverLookupEnabled=enabled.IsChecked==true;preferences.HoverTranslucent=translucent.IsChecked==true;preferences.HoverAutoAdd=autoAdd.IsChecked==true;preferences.HoverNearest=nearest.IsChecked==true;
            preferences.HoverDelayMs=milliseconds;preferences.HoverFontSize=((FontChoice)font.SelectedItem).Value;preferences.HoverModifiers=(ctrl.IsChecked==true?1:0)|(alt.IsChecked==true?2:0)|(shift.IsChecked==true?4:0);return null;
        };
        return panel;
    }
    private sealed record FontChoice(string Label,string Value){public override string ToString()=>Label;}
}
