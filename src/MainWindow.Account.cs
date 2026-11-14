using System.Net;
using MemoLex.Services;

namespace MemoLex;

public partial class MainWindow
{
    private int _accountVersion;
    private string? _accountCredentialKey;
    private bool _accountAuthenticated,_accountRejected;
    private CancellationTokenSource _accountCts=new();

    private void OnApiAuthenticationResult(string key,bool accepted)
    {
        void Apply()
        {
            if(key!=_accountCredentialKey||key!=_tokens.CredentialKey)return;
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
        if(_accountAuthenticated)
        {
            SetAccountStatus("已连接");
            SettingsButton.ToolTip=$"墨墨账号：已连接\n部分服务暂不可用：{UserError(error)}\n点击打开账号连接与应用设置";
            return;
        }
        SetAccountStatus(error is MaiMemoException {StatusCode:HttpStatusCode.TooManyRequests}?"连接受限":error is MaiMemoException {StatusCode:HttpStatusCode.Forbidden}?"权限待确认":"暂时离线");
    }

    internal async Task ReloadAccountAsync()
    {
        ++_accountVersion;_accountCredentialKey=_tokens.CredentialKey;_accountAuthenticated=_accountRejected=false;
        _accountCts.Cancel();_accountCts.Dispose();_accountCts=new CancellationTokenSource();
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
