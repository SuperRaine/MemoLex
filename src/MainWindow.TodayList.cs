using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MemoLex.Services;

namespace MemoLex;

public partial class MainWindow
{
    private void TodayListButton_Click(object sender,RoutedEventArgs e)
    {
        if(!_tokens.HasToken){SettingsButton_Click(sender,e);return;}
        var dialog=InputDialog.Shell(this,"今日学习单词",out _);dialog.Width=700;dialog.Height=650;dialog.SizeToContent=SizeToContent.Manual;
        var body=new Grid{Margin=new Thickness(20)};body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new());
        var head=new DockPanel{Margin=new Thickness(0,0,0,12)};var refresh=new Button{Content="刷新"};DockPanel.SetDock(refresh,Dock.Right);head.Children.Add(refresh);
        var status=InputDialog.Text("正在读取…");status.VerticalAlignment=VerticalAlignment.Center;head.Children.Add(status);body.Children.Add(head);
        var list=new ListView{HorizontalContentAlignment=HorizontalAlignment.Stretch,SelectionMode=SelectionMode.Single};
        var view=new GridView();view.Columns.Add(new(){Header="单词",DisplayMemberBinding=new System.Windows.Data.Binding("Word"),Width=150});view.Columns.Add(new(){Header="中文释义",DisplayMemberBinding=new System.Windows.Data.Binding("Meaning"),Width=300});view.Columns.Add(new(){Header="学习状态",DisplayMemberBinding=new System.Windows.Data.Binding("Status"),Width=130});list.View=view;Grid.SetRow(list,1);body.Children.Add(list);InputDialog.SetBody(dialog,body);
        var account=_accountVersion;using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(_accountCts.Token);
        dialog.Closed+=(_,_)=>cancellation.Cancel();
        async Task Load()
        {
            if(!refresh.IsEnabled)return;refresh.IsEnabled=false;status.Text="正在读取…";
            try
            {
                var items=await _api.GetTodayItemsAsync(cancellation.Token);if(account!=_accountVersion||cancellation.IsCancellationRequested)return;
                list.ItemsSource=items.Select(item=>new TodayListRow(item.Spelling,_dictionary.Lookup(item.Spelling)?.translation.Replace('\n','；')??"",item.IsFinished?"今日已完成":item.Responded?"学习中":LocallyConfirmedLearned(item.Spelling)?"已学过的单词":item.IsNew?"已计划新学":"今日复习")).ToArray();
                status.Text=$"共 {items.Count} 词 · 已完成 {items.Count(i=>i.IsFinished)} · 待新学 {items.Count(i=>i.IsNew&&!i.IsFinished&&!i.Responded)}"+(items.Count==1000?"（接口最多返回前 1000 词）":"");
                _todayStudyStates.Remove(_word);PrepareTodayStudyLookup(_word,_lookupCts?.Token??CancellationToken.None);await RetryTodayStudyStatusAsync(_word);
            }
            catch(OperationCanceledException){}
            catch(Exception error){status.Text=UserError(error);SetAccountError(error);}
            finally{refresh.IsEnabled=true;}
        }
        list.PreviewMouseLeftButtonUp+=async(_,args)=>
        {
            var element=args.OriginalSource as DependencyObject;
            while(element!=null&&element is not ListViewItem)element=System.Windows.Media.VisualTreeHelper.GetParent(element);
            if(element is ListViewItem {Content:TodayListRow row}){dialog.Close();RestoreWindow();await LoadWordAsync(row.Word);}
        };
        refresh.Click+=async(_,_)=>await Load();dialog.Loaded+=async(_,_)=>await Load();dialog.ShowDialog();
    }
    private sealed record TodayListRow(string Word,string Meaning,string Status);
}
