using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MemoLex.Services;

namespace MemoLex.Desktop;
internal static class UpdateNotificationDialog
{
    internal static Window Build(Window owner,UpdateManager updates,UpdateRelease release)
    {
        var dialog=InputDialog.Shell(owner,"软件更新",out var panel);
        dialog.Width=540;dialog.MaxHeight=Math.Max(380,SystemParameters.WorkArea.Height-60);
        var heading=InputDialog.Text("发现新版本 "+release.Version);
        heading.FontSize=22;heading.LineHeight=30;heading.FontWeight=FontWeights.Bold;heading.Foreground=new SolidColorBrush(Color.FromRgb(11,34,52));heading.Margin=new Thickness(0,0,0,6);
        panel.Children.Add(heading);
        var versionRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};
        versionRow.Children.Add(new Border
        {
            Background=new SolidColorBrush(Color.FromRgb(233,246,255)),BorderBrush=new SolidColorBrush(Color.FromRgb(198,228,247)),
            BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(9,2,9,2),
            Child=new TextBlock{Text=$"v{updates.CurrentVersion}  →  v{release.Version}",FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(11,132,189))}
        });
        panel.Children.Add(versionRow);
        if(!string.IsNullOrWhiteSpace(release.Notes))
        {
            var notesScroll=new ScrollViewer{Content=InputDialog.Text(release.Notes.Trim()),MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            panel.Children.Add(new Border
            {
                Child=notesScroll,Padding=new Thickness(13,10,13,10),Margin=new Thickness(0,0,0,8),
                CornerRadius=new CornerRadius(10),Background=new SolidColorBrush(Color.FromRgb(246,251,255)),
                BorderBrush=new SolidColorBrush(Color.FromRgb(220,236,247)),BorderThickness=new Thickness(1)
            });
        }
        var state=InputDialog.Text(updates.Status, status: true);state.Margin=new Thickness(0,8,0,14);panel.Children.Add(state);
        var automatic=new CheckBox{Content="以后发现新版时自动下载",Style=(Style)Application.Current.FindResource("SwitchCheckBox"),IsChecked=updates.AutomaticallyDownload,Margin=new Thickness(0,0,0,16)};
        automatic.Checked+=(_,_)=>updates.AutomaticallyDownload=true;automatic.Unchecked+=(_,_)=>updates.AutomaticallyDownload=false;panel.Children.Add(automatic);
        var buttons=new DockPanel();
        var page=new Button{Content="前往下载页面",HorizontalAlignment=HorizontalAlignment.Left,Style=(Style)Application.Current.FindResource("PillButton")};
        page.Click+=(_,_)=>{try{Process.Start(new ProcessStartInfo(release.Page.AbsoluteUri){UseShellExecute=true});}catch{state.Text="无法打开浏览器，请在「设置 → 软件更新」中重试。";}};
        DockPanel.SetDock(page,Dock.Left);buttons.Children.Add(page);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var later=new Button{Content="稍后",Margin=new Thickness(8,0,0,0),IsCancel=true};later.Click+=(_,_)=>dialog.Close();
        var primary=new Button{Margin=new Thickness(8,0,0,0),IsDefault=true,Style=(Style)Application.Current.FindResource("AccentButton")};
        primary.Click+=async(_,_)=>{if(updates.CanInstall)await updates.InstallAsync(dialog);else if(updates.CanDownload)await updates.DownloadAsync();};
        actions.Children.Add(later);actions.Children.Add(primary);buttons.Children.Add(actions);panel.Children.Add(buttons);
        void Render()
        {
            state.Text=updates.Status;automatic.IsChecked=updates.AutomaticallyDownload;primary.Content=updates.CanInstall?"更新并重启":updates.Busy?"正在准备…":"下载更新";
            primary.IsEnabled=updates.CanInstall||updates.CanDownload;
        }
        updates.Changed+=Render;dialog.Closed+=(_,_)=>updates.Changed-=Render;Render();
        dialog.RegisterName("UpdateNoticeVersion",heading);dialog.RegisterName("UpdateNoticeStatus",state);
        dialog.RegisterName("UpdateNoticePrimary",primary);dialog.RegisterName("UpdateNoticeLater",later);
        dialog.RegisterName("UpdateNoticeAutomaticDownload",automatic);
        return dialog;
    }
}
