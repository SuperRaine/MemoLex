using System.Windows;
using System.Windows.Controls;
using MemoLex.Services;

namespace MemoLex.Desktop;

internal static class AccountSettingsPanel
{
    internal static FrameworkElement Build(Window owner,TokenStore store,Func<Task> changed)
    {
        var panel=new StackPanel();
        var headline=new TextBlock {FontSize=15,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,0,0,8)};panel.Children.Add(headline);
        var login=new Button {Content="登录或添加账号",Style=(Style)Application.Current.FindResource("AccentButton"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,4,0,12)};
        panel.Children.Add(login);
        panel.Children.Add(new TextBlock {Text="选择账号即可切换",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});
        var accounts=new ComboBox {MinHeight=34,Margin=new Thickness(0,0,0,8)};panel.Children.Add(accounts);
        var actions=new WrapPanel();panel.Children.Add(actions);
        Button Add(string text){var b=new Button {Content=text,Margin=new Thickness(0,0,8,10)};actions.Children.Add(b);return b;}
        var relogin=Add("重新登录");var logout=Add("退出登录");var remove=Add("移除账号");
        var nameRow=new DockPanel {Margin=new Thickness(0,2,0,6)};var rename=new Button {Content="保存账号名称",Margin=new Thickness(10,0,0,0),Height=36,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(rename,Dock.Right);nameRow.Children.Add(rename);
        var name=new TextBox {MaxLength=80,Padding=new Thickness(9,6,9,6),Height=36,VerticalAlignment=VerticalAlignment.Center,ToolTip="给账号起一个方便区分的名称"};nameRow.Children.Add(name);panel.Children.Add(nameRow);
        var status=InputDialog.Text("", status: true);status.Margin=new Thickness(0,4,0,4);panel.Children.Add(status);
        panel.Children.Add(InputDialog.Text("登录信息加密保存在本机，过期后请重新登录。"));
        var importPanel=new StackPanel();
        importPanel.Children.Add(InputDialog.Text("可从墨墨 App 的「开放 API」复制授权，在你自己的电脑上分别导入同一份有效授权。"));
        var secret=new PasswordBox {MaxLength=512,Padding=new Thickness(9),Margin=new Thickness(0,10,0,10)};importPanel.Children.Add(secret);
        var import=new Button {Content="验证并导入",HorizontalAlignment=HorizontalAlignment.Left};importPanel.Children.Add(import);
        panel.Children.Add(new Expander {Header="导入已有的 API 授权",Content=importPanel,Margin=new Thickness(0,10,0,0)});
        var busy=false;var refreshing=false;string state="";
        SavedAccount? Selected()=>accounts.SelectedItem as SavedAccount;
        void Enable()
        {
            var selected=Selected();login.IsEnabled=!busy;accounts.IsEnabled=!busy;
            relogin.IsEnabled=!busy&&selected!=null;
            logout.IsEnabled=!busy&&store.HasToken;remove.IsEnabled=!busy&&selected!=null;rename.IsEnabled=!busy&&selected!=null;import.IsEnabled=!busy;
        }
        void Refresh(bool force=false)
        {
            try
            {
                var saved=store.GetAccounts();var key=string.Join(";",saved.Select(a=>$"{a.Id}:{a.Name}:{a.Active}:{a.NeedsLogin}"));
                var active=saved.FirstOrDefault(a=>a.Active);
                if(force||state!=key)
                {
                    var prior=Selected()?.Id;state=key;refreshing=true;
                    try
                    {
                        accounts.ItemsSource=saved;
                        // Leave a signed-out account available to choose, even when it is the only one.
                        accounts.SelectedItem=active??saved.FirstOrDefault(a=>a.Id==prior&&a.NeedsLogin);
                    }
                    finally{refreshing=false;}
                }
                headline.Text=active!=null?"当前账号："+active.Name:store.HasExpiredSession?"登录已失效，请重新登录":"还未登录墨墨账号";
                Enable();
            }
            catch{status.Text="无法加载已保存的账号，请稍后重试。";relogin.IsEnabled=remove.IsEnabled=rename.IsEnabled=false;}
        }
        accounts.SelectionChanged+=async(_,_)=>
        {
            name.Text=Selected()?.Name??"";Enable();
            if(!refreshing&&!busy&&Selected() is {NeedsLogin:false,Active:false} selected)await Change(()=>store.SwitchAccount(selected.Id));
        };
        async Task Change(Action mutation)
        {
            if(busy)return;busy=true;Enable();
            try{mutation();Refresh(true);await changed();status.Text="账号已更新。";}
            catch(Exception error){status.Text=error is ArgumentException or InvalidOperationException?error.Message:"未能更新账号信息，请检查网络后重试。";}
            finally{busy=false;Refresh(true);}
        }
        async Task Login(SavedAccount? target)
        {
            if(busy)return;busy=true;Enable();
            try
            {
                var dialog=new OfficialLoginWindow(owner,store,target);
                if(dialog.ShowDialog()==true){Refresh(true);await changed();status.Text="登录成功，已记住此账号。";}
            }
            catch{status.Text="登录未完成，请稍后重试。";}
            finally{busy=false;Refresh(true);}
        }
        login.Click+=async(_,_)=>await Login(null);
        relogin.Click+=async(_,_)=>await Login(Selected());
        logout.Click+=async(_,_)=>await Change(store.LogoutActive);
        rename.Click+=async(_,_)=>{if(Selected() is {} account)await Change(()=>store.RenameAccount(account.Id,name.Text));};
        remove.Click+=async(_,_)=>
        {
            if(Selected() is {} account&&MessageBox.Show(owner,"移除账号“"+account.Name+"”的登录信息？本地助记和查词历史会保留。","移除账号",MessageBoxButton.OKCancel,MessageBoxImage.Question,MessageBoxResult.Cancel)==MessageBoxResult.OK)
                await Change(()=>store.RemoveAccount(account.Id));
        };
        import.Click+=async(_,_)=>
        {
            if(busy||string.IsNullOrWhiteSpace(secret.Password))return;busy=true;Enable();status.Text="正在验证墨墨授权…";
            try
            {
                var token=secret.Password.Trim();using var api=new MaiMemoClient(TokenStore.InMemory(token));
                if(await api.ResolveWordAsync("hello")==null)throw new MaiMemoException("未能确认授权。");
                store.SaveToken(token);secret.Clear();await changed();status.Text="账号已导入并保存。";
            }
            catch{status.Text="授权未通过验证，请检查 Token、权限和网络。";}
            finally{busy=false;Refresh(true);}
        };
        var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(2)};timer.Tick+=(_,_)=>{if(!busy)Refresh();};
        panel.Loaded+=(_,_)=>timer.Start();panel.Unloaded+=(_,_)=>timer.Stop();Refresh(true);
        foreach(var (key,element) in new (string,FrameworkElement)[]{("AccountList",accounts),("OfficialLoginButton",login),("ReloginButton",relogin),("LogoutAccountButton",logout),("AccountStatus",headline)})owner.RegisterName(key,element);
        return panel;
    }
}
