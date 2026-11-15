using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MemoLex;

internal static class MnemonicEditor
{
    internal static readonly string[] Types=["词根词缀","联想","谐音","派生","词根","词源","固搭","语法","对比","近义","反义","扩展","串记","口诀","合成","吐槽","其他","固定搭配","辨析","近反义词","图例"];
    internal static Window Build(Window owner,string word,string noteType,string initial,bool canSync,Func<string,string,bool,Task> save)
    {
        var dialog=InputDialog.Shell(owner,"编辑助记 · "+word,out _);
        dialog.Width=650;dialog.Height=510;dialog.MinWidth=520;dialog.MinHeight=430;dialog.SizeToContent=SizeToContent.Manual;
        var body=new Grid {Margin=new Thickness(24,18,24,22)};
        foreach(var height in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})body.RowDefinitions.Add(new RowDefinition {Height=height});
        var type=new ComboBox {ItemsSource=Types,SelectedItem=Types.Contains(noteType)?noteType:"其他",Width=140,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,12)};
        body.Children.Add(type);
        var text=new TextBox {Text=initial,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalContentAlignment=VerticalAlignment.Top,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxLength=20000,FontSize=15,FontFamily=new FontFamily("Microsoft YaHei UI")};
        Grid.SetRow(text,1);body.Children.Add(text);dialog.RegisterName("MnemonicText",text);dialog.RegisterName("MnemonicType",type);
        var status=InputDialog.Text("保存到本地后可离线查看。联网查词时优先显示云端助记；同步到墨墨也会保留本地副本。");status.Margin=new Thickness(0,12,0,13);Grid.SetRow(status,2);body.Children.Add(status);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetRow(buttons,3);body.Children.Add(buttons);
        var cancel=new Button {Content="取消",Margin=new Thickness(0,0,8,0)};cancel.Click+=(_,_)=>dialog.Close();buttons.Children.Add(cancel);
        var local=new Button {Content="保存到本地",Margin=new Thickness(0,0,8,0),IsDefault=true};buttons.Children.Add(local);dialog.RegisterName("SaveMnemonicLocal",local);
        var cloud=new Button {Content="同步到墨墨",Style=(Style)Application.Current.FindResource("AccentButton"),Visibility=canSync?Visibility.Visible:Visibility.Collapsed};buttons.Children.Add(cloud);dialog.RegisterName("SaveMnemonicCloud",cloud);
        InputDialog.SetBody(dialog,body);var busy=false;
        dialog.Closing+=(_,e)=>{if(busy)e.Cancel=true;};
        async Task Save(bool sync)
        {
            if(string.IsNullOrWhiteSpace(text.Text)){status.Text="请先填写助记内容。";return;}
            busy=true;body.IsEnabled=false;status.Text=sync?"正在保存并同步到墨墨…":"正在保存本地助记…";
            try{await save((string)type.SelectedItem,text.Text.Trim(),sync);busy=false;dialog.Close();}
            catch(Exception error){status.Text=error is Services.MaiMemoException or ArgumentException or System.IO.IOException?error.Message:"未能保存助记，请稍后重试。";}
            finally{busy=false;body.IsEnabled=true;}
        }
        local.Click+=async(_,_)=>await Save(false);cloud.Click+=async(_,_)=>await Save(true);
        return dialog;
    }
}
