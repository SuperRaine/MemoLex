using System.Net;
using System.IO;
using MemoLex.Services;

namespace MemoLex;

public partial class MainWindow
{
    private int _accountVersion;
    private string? _accountCredentialKey;
    private bool _accountAuthenticated,_accountRejected;
    private CancellationTokenSource _accountCts=new();
    private System.Windows.Threading.DispatcherTimer? _accountTimer;
    private FileSystemWatcher? _accountWatcher;
    private DateTimeOffset? _accountRetryAt;
    private bool _checkingAccount;
    internal Action<string>? AccountExpiryNotification {get;set;}
    private void StartAccountClock()
    {
        CheckAccountExpiry();
        _accountTimer=new(){Interval=TimeSpan.FromSeconds(30)};
        _accountTimer.Tick+=(_,_)=>CheckAccountExpiry();_accountTimer.Start();
        if(_desktopIntegration)
        {
            try
            {
                var directory=Path.GetDirectoryName(TokenStore.TokenPath)!;Directory.CreateDirectory(directory);
                _accountWatcher=new(directory,Path.GetFileName(TokenStore.TokenPath)){NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size};
                void Updated()=>Dispatcher.InvokeAsync(CheckAccountExpiry);
                _accountWatcher.Changed+=(_,_)=>Updated();_accountWatcher.Created+=(_,_)=>Updated();_accountWatcher.Renamed+=(_,_)=>Updated();_accountWatcher.EnableRaisingEvents=true;
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    private async void CheckAccountExpiry()
    {
        if(_checkingAccount)return;_checkingAccount=true;
        try
        {
            if(_tokens.ExpireIfDue())HandleExpiredAccount();
            else if(_accountCredentialKey!=_tokens.CredentialKey)await ReloadAccountAsync();
            else if(_tokens.HasExpiredSession)SetAccountStatus("登录已过期");
            else if(_allowCloud&&_ready&&_tokens.HasToken&&_accountRetryAt<=DateTimeOffset.UtcNow)
            {
                _accountRetryAt=null;
                await RefreshNotepadsAsync();
                if(_accountAuthenticated&&_viewTarget==ViewTarget.Word&&ValidWord(_word))
                    await LoadLocalAndCloudNotesAsync(_word,_accountCts.Token);
            }
        }
        catch{}
        finally{_checkingAccount=false;}
    }
    private async void HandleExpiredAccount()
    {
        await ReloadAccountAsync();SetAccountStatus("登录已过期");
        const string message="墨墨登录已过期，已退出当前账号。请在设置中重新登录。";
        StatusText.Text=message;
        if(AccountExpiryNotification!=null)AccountExpiryNotification(message);
        else if(_desktopIntegration)System.Windows.MessageBox.Show(this,message,"需要重新登录",System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Information);
    }

    private void OnApiAuthenticationResult(string key,bool accepted)
    {
        void Apply()
        {
            if(key!=_accountCredentialKey||key!=_tokens.CredentialKey)return;
            if(!accepted)
            {
                try{if(_tokens.ExpireActive(key)){HandleExpiredAccount();return;}}catch{}
            }
            _accountAuthenticated=accepted;_accountRejected=!accepted;
            SetAccountStatus(accepted?"已连接":"登录失效");
        }
        if(Dispatcher.HasShutdownStarted)return;
        if(Dispatcher.CheckAccess())Apply();else Dispatcher.InvokeAsync(Apply);
    }

    private void SetAccountError(Exception error)
    {
        if(!_allowCloud){SetAccountStatus("离线模式");return;}
        if(!_tokens.HasToken){SetAccountStatus("未连接");return;}
        if(_accountRejected){SetAccountStatus("登录失效");return;}
        if(error is not MaiMemoException {StatusCode:HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity})
        {
            var due=DateTimeOffset.UtcNow.AddSeconds(error is MaiMemoException {StatusCode:HttpStatusCode.TooManyRequests}?60:30);
            if(_accountRetryAt==null||error is MaiMemoException {StatusCode:HttpStatusCode.TooManyRequests})_accountRetryAt=due;
        }
        if(_accountAuthenticated)
        {
            SetAccountStatus("已连接");
            AccountButton.ToolTip=$"墨墨账号：{_tokens.ActiveAccount?.Name??"未选择账号"} · 已连接\n部分服务暂不可用：{UserError(error)}\n点击登录或切换账号";
            return;
        }
        SetAccountStatus(error is MaiMemoException {StatusCode:HttpStatusCode.TooManyRequests}?"连接受限":error is MaiMemoException {StatusCode:HttpStatusCode.Forbidden}?"权限待确认":"暂时离线");
    }

    internal async Task ReloadAccountAsync()
    {
        ++_accountVersion;_accountCredentialKey=_tokens.CredentialKey;_api.ExpectedCredentialKey=_accountCredentialKey;_accountAuthenticated=_accountRejected=false;_accountRetryAt=null;
        _accountCts.Cancel();_accountCts.Dispose();_accountCts=new CancellationTokenSource();
        ResetStubbornWords();
        if(_ready)_=RefreshStubbornWordsAsync();
        _lookupCts?.Cancel();_notesCts?.Cancel();_studyLoadCts?.Cancel();++_studyReadVersion;
        _cloudCache.Clear();_vocabulary=null;_todayStudyStates.Clear();_studyLoadingWord=null;
        _notepadCache.Clear();_notepadLoads.Clear();_deletedNotepads.Clear();++_membershipVersion;
        _membershipWord=_membershipPad="";_refreshing=false;
        _changingLists=true;NotepadCombo.ItemsSource=Array.Empty<CloudNotepad>();_changingLists=false;
        SetAccountStatus(_tokens.HasToken?"连接中":"未连接");UpdateTodayStudyButton();
        var view=CaptureView();
        if(view.Target==ViewTarget.Translation){await RefreshNotepadsAsync();RestoreTranslationView(view);}
        else if(view.Target==ViewTarget.ChineseSearch&&view.Chinese is {} chinese)await Task.WhenAll(RefreshNotepadsAsync(),SearchChineseAsync(chinese.Query,false,false,chinese));
        else await Task.WhenAll(RefreshNotepadsAsync(),LoadWordAsync(_word,recordHistory:false,addNavigationEntry:false,restore:view));
    }
}
