using System.Windows;
using System.Windows.Controls;
using MemoLex.Core;

namespace MemoLex;

internal readonly record struct CloseChoice(bool CloseToTray,bool Remember);
internal static class CloseBehaviorDialog
{
    internal static CloseChoice? Ask(Window owner,Preferences preferences)
    {
        CloseChoice? choice=null;
        Build(owner,preferences,value=>choice=value).ShowDialog();
        return choice;
    }
    internal static Window Build(Window owner,Preferences preferences,Action<CloseChoice> choose)
    {
        var dialog=InputDialog.Shell(owner,"关闭窗口",out var panel);dialog.Width=520;
        panel.Children.Add(new TextBlock {Text="你希望如何关闭墨墨搜词？",FontSize=21,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,14)});
        panel.Children.Add(InputDialog.Text("最小化到托盘后，软件会继续运行。点击托盘图标或使用快速查词快捷键，可再次打开窗口。"));
        var remember=new CheckBox {Content="记住选择，下次不再询问",Margin=new Thickness(0,20,0,9),IsChecked=false};
        panel.Children.Add(remember);
        panel.Children.Add(InputDialog.Text("之后可以在标题栏的「账号与设置」中更改。"));
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,22,0,0)};
        var cancel=new Button {Content="取消",IsCancel=true,Margin=new Thickness(0,0,9,0)};
        cancel.Click+=(_,_)=>dialog.Close();buttons.Children.Add(cancel);
        var exit=new Button {Content="退出程序",IsDefault=!preferences.CloseToTray,Margin=new Thickness(0,0,9,0)};
        var tray=new Button {Content="最小化到托盘",IsDefault=preferences.CloseToTray,Style=(Style)Application.Current.FindResource("AccentButton")};
        void Select(bool closeToTray){choose(new CloseChoice(closeToTray,remember.IsChecked==true));dialog.Close();}
        exit.Click+=(_,_)=>Select(false);tray.Click+=(_,_)=>Select(true);
        buttons.Children.Add(exit);buttons.Children.Add(tray);panel.Children.Add(buttons);
        dialog.RegisterName("RememberCloseChoice",remember);dialog.RegisterName("ExitApplicationChoice",exit);dialog.RegisterName("TrayChoice",tray);dialog.RegisterName("CancelCloseChoice",cancel);
        return dialog;
    }
}
