using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MemoLex.Services;

namespace MemoLex.Desktop;
internal static class UpdateNotificationDialog
{
    internal static Window Build(Window owner,UpdateManager updates,UpdateRelease release)
    {
        var dialog=InputDialog.Shell(owner,"软件更新",out var panel);
        dialog.Width=520;dialog.MaxHeight=Math.Max(360,SystemParameters.WorkArea.Height-60);
        var heading=InputDialog.Text("发现新版本 "+release.Version);
        heading.FontSize=23;heading.LineHeight=32;heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,0,0,10);
        panel.Children.Add(heading);panel.Children.Add(InputDialog.Text("当前版本 "+updates.CurrentVersion));
        if(!string.IsNullOrWhiteSpace(release.Notes))
            panel.Children.Add(new ScrollViewer{Content=InputDialog.Text(release.Notes.Trim()),MaxHeight=170,
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,16,0,6)});
        var state=InputDialog.Text(updates.Status, status: true);state.Margin=new Thickness(0,14,0,16);panel.Children.Add(state);
        var automatic=new CheckBox{Content="以后发现新版时自动下载",Style=(Style)Application.Current.FindResource("SwitchCheckBox"),IsChecked=updates.AutomaticallyDownload,Margin=new Thickness(0,0,0,16)};
        automatic.Checked+=(_,_)=>updates.AutomaticallyDownload=true;automatic.Unchecked+=(_,_)=>updates.AutomaticallyDownload=false;panel.Children.Add(automatic);
        var buttons=new DockPanel();
        var page=new Button{Content="前往下载页面",HorizontalAlignment=HorizontalAlignment.Left};
        page.Click+=(_,_)=>{try{Process.Start(new ProcessStartInfo(release.Page.AbsoluteUri){UseShellExecute=true});}catch{state.Text="无法打开浏览器，请在「设置 → 软件更新」中重试。";}};
        DockPanel.SetDock(page,Dock.Left);buttons.Children.Add(page);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var later=new Button{Content="稍后",Margin=new Thickness(8,0,0,0),IsCancel=true};later.Click+=(_,_)=>dialog.Close();
        var primary=new Button{Margin=new Thickness(8,0,0,0),IsDefault=true};
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
