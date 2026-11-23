using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MemoLex;

internal static class TodayStudyConfirmationDialog
{
    internal const string SyncSettingsPath="墨墨背单词 → 我的 → 学习数据同步 → 学习数据自动同步";

    internal static bool Confirm(Window owner,string word) => ConfirmPrompt(owner,$"将「{word}」添加到今日新学？");
    internal static bool ConfirmBatch(Window owner, int count) => ConfirmPrompt(owner, $"将选中的 {count} 个单词添加到今日新学？");

    private static bool ConfirmPrompt(Window owner,string prompt)
    {
        var confirmed=false;
        BuildPrompt(owner,prompt,()=>confirmed=true).ShowDialog();
        return confirmed;
    }

    internal static Window Build(Window owner,string word,Action confirmed) => BuildPrompt(owner,$"将「{word}」添加到今日新学？",confirmed);

    private static Window BuildPrompt(Window owner,string prompt,Action confirmed)
    {
        var dialog=InputDialog.Shell(owner,"添加到今日新学",out var panel);dialog.Width=560;
        panel.Children.Add(new TextBlock {Text=prompt,FontSize=20,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        panel.Children.Add(InputDialog.Text("学习状态依赖墨墨的数据同步。开启手机端自动同步，可及时更新学习记录。"));
        var path=new TextBlock {Text=SyncSettingsPath,FontSize=14,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,LineHeight=26,Foreground=new SolidColorBrush(Color.FromRgb(8,127,175))};
        panel.Children.Add(new Border {Child=path,Padding=new Thickness(14,11,14,11),Margin=new Thickness(0,14,0,12),CornerRadius=new CornerRadius(9),Background=new SolidColorBrush(Color.FromRgb(234,247,255))});
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,22,0,0)};
        var cancel=new Button {Content="取消",IsDefault=true,IsCancel=true,Margin=new Thickness(0,0,10,0)};
        cancel.Click+=(_,_)=>dialog.Close();
        var confirm=new Button {Content="添加",Style=(Style)Application.Current.FindResource("AccentButton")};
        confirm.Click+=(_,_)=>{confirmed();dialog.Close();};
        buttons.Children.Add(cancel);buttons.Children.Add(confirm);panel.Children.Add(buttons);
        dialog.RegisterName("TodayStudySyncPath",path);dialog.RegisterName("CancelTodayStudy",cancel);dialog.RegisterName("ConfirmTodayStudy",confirm);
        return dialog;
    }

}
