using System.Windows;
using MemoLex.Services;

namespace MemoLex;

public partial class MainWindow
{
    private sealed record StudyWordSnapshot(DateTime CheckedAt,TodayStudyStatus? Status,string? Error=null,bool Missing=false);
    private readonly Dictionary<string,StudyWordSnapshot> _todayStudyStates=new(StringComparer.OrdinalIgnoreCase);
    private DateOnly _studyDate;
    private string? _studyWordInFlight;
    private CancellationTokenSource? _studyCts;
    private CancellationTokenSource? _studyLoadCts;
    private int _studyReadVersion;
    private string? _studyLoadingWord;
    private System.Windows.Threading.DispatcherTimer? _studyDayTimer;
    internal Func<Window,string,bool> ConfirmTodayStudy {get;set;}=TodayStudyConfirmationDialog.Confirm;
    private bool LocallyConfirmedLearned(string word)=>_accountCredentialKey!=null&&_accountCredentialKey==_tokens.CredentialKey&&_preferences.IsConfirmedLearned(_tokens.ActiveAccount?.Id,word);

    private void RememberLearnedWord(string word)
    {
        if(_accountCredentialKey==null||_accountCredentialKey!=_tokens.CredentialKey||_tokens.ActiveAccount is not {} account
            ||_preferences.IsConfirmedLearned(account.Id,word))return;
        _preferences.ConfirmLearned(account.Id,word);
        _preferences.Save();
    }

    private void StartTodayStudyClock()
    {
        _studyDayTimer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(30)};
        _studyDayTimer.Tick+=(_,_)=>
        {
            var previous=_studyDate;UpdateTodayStudyButton();
            if((previous!=_studyDate||IsActive&&!FreshStudySnapshot(_word))&&_studyLoadingWord==null&&!_adding&&_ready&&_allowCloud&&_tokens.HasToken&&ValidWord(_word))
            {
                PrepareTodayStudyLookup(_word,_lookupCts?.Token??CancellationToken.None);
                _=RetryTodayStudyStatusAsync(_word);
            }
        };
        _studyDayTimer.Start();
    }

    private void UpdateTodayStudyButton()
    {
        // Today follows the API's Beijing timezone, rather than the PC's timezone.
        var today=DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);
        if(today!=_studyDate){_studyDate=today;_todayStudyStates.Clear();}
        _todayStudyStates.TryGetValue(_word,out var snapshot);var local=LocallyConfirmedLearned(_word);var state=local?TodayStudyStatus.AlreadyLearned:snapshot?.Status;
        var confirmed=state is TodayStudyStatus.Added or TodayStudyStatus.AlreadyToday or TodayStudyStatus.Planned;
        var learned=state==TodayStudyStatus.AlreadyLearned;var pending=state==TodayStudyStatus.PendingVerification;
        var syncRequired=state==TodayStudyStatus.SyncRequired;
        var loading=!local&&_studyLoadingWord==_word;var failed=!local&&snapshot?.Error!=null;var missing=!local&&snapshot?.Missing==true;
        AddTodayButton.Content=_studyWordInFlight==_word?"正在处理…":loading?"正在获取学习状态…":confirmed?"已计划新学":learned?"已学过":missing?"墨墨未收录":failed?"重新获取学习状态":syncRequired?"核对学习记录":pending?"核对今日新学":"＋ 今日新学";
        AddTodayButton.IsEnabled=_ready&&_allowCloud&&!_adding&&!_refreshing&&!loading&&ValidWord(_word)&&!confirmed&&!learned&&!missing;
        AddTodayButton.ToolTip=local?"已在当前账号确认学过，不会再次加入今日新学。":failed?snapshot!.Error+"\n点击可刷新学习状态，不会重复添加。":missing?"墨墨词库未收录此拼写。":learned?"该词已有实际学习记录，不能重新作为新学单词添加。":confirmed?"已加入新学计划，尚未学习；手机端完成自动同步后状态会刷新。":syncRequired?"暂时无法确认学习记录，点击可重新核对，不会重复添加。":pending?"学习状态还未同步，点击可刷新，不会重复添加。":"将单词加入墨墨今日新学。";
    }

    private bool FreshStudySnapshot(string word)=>LocallyConfirmedLearned(word)||_todayStudyStates.TryGetValue(word,out var cached)&&cached.Error==null&&DateTime.UtcNow-cached.CheckedAt<TimeSpan.FromSeconds(15);
    private void PrepareTodayStudyLookup(string word,CancellationToken cancellation)
    {
        _studyLoadCts?.Cancel();_studyLoadCts?.Dispose();++_studyReadVersion;
        _studyLoadCts=CancellationTokenSource.CreateLinkedTokenSource(cancellation,_accountCts.Token);
        UpdateTodayStudyButton();
        _studyLoadingWord=_allowCloud&&_tokens.HasToken&&!FreshStudySnapshot(word)&&_studyWordInFlight!=word?word:null;
        UpdateTodayStudyButton();
    }
    private void FailTodayStudyLookup(string word,Exception error)
    {
        if(word!=_word)return;
        _todayStudyStates[word]=new(DateTime.UtcNow,null,UserError(error));_studyLoadingWord=null;UpdateTodayStudyButton();
    }
    private async Task LoadTodayStudyStatusAsync(string word,Vocabulary? vocabulary)
    {
        if(word!=_word||!_allowCloud||!_tokens.HasToken)return;
        if(FreshStudySnapshot(word)||_studyWordInFlight==word){_studyLoadingWord=null;UpdateTodayStudyButton();return;}
        var version=_studyReadVersion;var account=_accountVersion;var cancellation=_studyLoadCts?.Token??_accountCts.Token;
        bool Current()=>word==_word&&version==_studyReadVersion&&account==_accountVersion&&!cancellation.IsCancellationRequested;
        _studyLoadingWord=word;UpdateTodayStudyButton();
        try
        {
            if(vocabulary==null){if(Current())_todayStudyStates[word]=new(DateTime.UtcNow,null,Missing:true);return;}
            var status=await _api.GetTodayStudyStatusAsync(vocabulary,cancellation);
            if(!Current())return;
            if(_todayStudyStates.TryGetValue(word,out var old))
            {
                if(old.Status==TodayStudyStatus.AlreadyLearned)status=TodayStudyStatus.AlreadyLearned;
                else if(status==TodayStudyStatus.NotAdded&&old.Status==TodayStudyStatus.PendingVerification)status=TodayStudyStatus.PendingVerification;
            }
            if(status==TodayStudyStatus.AlreadyLearned)RememberLearnedWord(word);
            _todayStudyStates[word]=new(DateTime.UtcNow,status);
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(Current()){_todayStudyStates[word]=new(DateTime.UtcNow,null,UserError(error));SetAccountError(error);}}
        finally{if(Current()){_studyLoadingWord=null;UpdateTodayStudyButton();}}
    }
    private async Task RetryTodayStudyStatusAsync(string word)
    {
        var account=_accountVersion;var version=_studyReadVersion;var cancellation=_studyLoadCts?.Token??_accountCts.Token;
        try
        {
            var vocabulary=_vocabulary?.Spelling.Equals(word,StringComparison.OrdinalIgnoreCase)==true?_vocabulary:await _api.ResolveWordAsync(word,cancellation);
            if(cancellation.IsCancellationRequested||account!=_accountVersion||version!=_studyReadVersion||word!=_word)return;
            _vocabulary=vocabulary;await LoadTodayStudyStatusAsync(word,vocabulary);
        }
        catch(OperationCanceledException){}
        catch(Exception error){if(account==_accountVersion&&version==_studyReadVersion&&word==_word){FailTodayStudyLookup(word,error);SetAccountError(error);}}
    }

    private async void AddTodayButton_Click(object sender,RoutedEventArgs e)
    {
        if(LocallyConfirmedLearned(_word)){UpdateTodayStudyButton();return;}
        if(!_ready||!_allowCloud||_adding||_refreshing||_studyLoadingWord==_word||!ValidWord(_word))return;
        UpdateTodayStudyButton();
        _todayStudyStates.TryGetValue(_word,out var snapshot);var saved=snapshot?.Status;
        if(saved is TodayStudyStatus.Added or TodayStudyStatus.AlreadyToday or TodayStudyStatus.Planned or TodayStudyStatus.AlreadyLearned||snapshot?.Missing==true)return;
        if(!_tokens.HasToken){AccountButton_Click(this,new RoutedEventArgs());return;}
        if(snapshot?.Error!=null){_todayStudyStates.Remove(_word);PrepareTodayStudyLookup(_word,_lookupCts?.Token??CancellationToken.None);await RetryTodayStudyStatusAsync(_word);return;}
        var word=_word;var date=_studyDate;var account=_accountVersion;var verifyOnly=saved is TodayStudyStatus.PendingVerification or TodayStudyStatus.SyncRequired;
        _adding=true;
        SetCloudWriteBusy(true);AddButton.IsEnabled=false;
        string? message=null;
        try
        {
            if(!verifyOnly&&!ConfirmTodayStudy(this,word))
            {
                message=$"已取消将「{word}」添加到今日新学。";
                return;
            }
            ++_studyReadVersion;_studyLoadCts?.Cancel();_studyLoadingWord=null;
            _studyWordInFlight=word;_studyCts?.Dispose();_studyCts=CancellationTokenSource.CreateLinkedTokenSource(_accountCts.Token);UpdateTodayStudyButton();
            StatusText.Text=verifyOnly?$"正在核对「{word}」的今日新学状态…":$"正在将「{word}」添加到今日新学…";
            var token=_studyCts.Token;
            var vocabulary=_vocabulary?.Spelling.Equals(word,StringComparison.OrdinalIgnoreCase)==true?_vocabulary:await _api.ResolveWordAsync(word,token);
            if(vocabulary==null)throw new MaiMemoException("墨墨未收录这个单词，请检查拼写。");
            var result=await _api.AddToTodayAsync(vocabulary,verifyOnly,token);
            if(date==_studyDate&&account==_accountVersion)
            {
                if(result.Status==TodayStudyStatus.AlreadyLearned)RememberLearnedWord(word);
                _todayStudyStates[word]=new(DateTime.UtcNow,result.Status);
            }
            message=result.Status switch
            {
                TodayStudyStatus.Added=>$"「{word}」已加入今日新学。",
                TodayStudyStatus.AlreadyToday=>$"「{word}」已计划新学，不会重复添加。",
                TodayStudyStatus.Planned=>$"「{word}」已计划新学，手机端同步后可查看今日安排。",
                TodayStudyStatus.SyncRequired=>$"「{word}」的学习记录还未同步。请先在墨墨 App 同步，再点击核对，不会重复添加。",
                TodayStudyStatus.AlreadyLearned=>$"「{word}」已学过或已安排今日复习，无需再加入新学。",
                TodayStudyStatus.NotAdded=>$"墨墨本次未添加「{word}」；该词可能已在计划中，或单词上限不足。",
                _=>result.AddedCount==1?$"「{word}」已加入学习计划。请在墨墨 App 同步后，点击核对今日安排。":$"「{word}」是否加入今日新学还未确认。请在墨墨 App 同步后，点击核对。"
            };
        }
        catch(OperationCanceledException){message="今日新学操作已取消，请在墨墨核对结果。";}
        catch(Exception error){message=UserError(error);SetAccountError(error);}
        finally
        {
            _adding=false;_studyWordInFlight=null;SetCloudWriteBusy(false);
            await UpdateMembershipAsync();UpdateTodayStudyButton();
            if(message!=null)StatusText.Text=message;
        }
    }
}
