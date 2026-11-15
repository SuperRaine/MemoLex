using System.Windows;
using System.Windows.Threading;
using MemoLex.Desktop;
using MemoLex.Services;

namespace MemoLex;
public partial class MainWindow
{
    private bool _updateChecksStarted;
    private Window? _updateNotice;
    private UpdateRelease? _pendingUpdateNotice;
    internal Action<Window> PresentUpdateNotice {get;set;}=dialog=>dialog.ShowDialog();
    private void StartUpdateChecks()
    {
        if(_updateChecksStarted||(!_desktopIntegration&&_updates==null))return;
        _updateChecksStarted=true;_updates??=new(_preferences,ExitApplication);
        _updates.Changed+=()=>{if(_updates.CanInstall)StatusText.Text=_updates.Status+" 设置 → 软件更新";};
        _updates.NewVersionAvailable+=release=>{_pendingUpdateNotice=release;QueuePendingUpdateNotice();};
        _=_updates.Start();
    }
    private void QueuePendingUpdateNotice()
    {
        if(_pendingUpdateNotice!=null&&!Dispatcher.HasShutdownStarted)
            _=Dispatcher.BeginInvoke(new Action(ShowPendingUpdateNotice),DispatcherPriority.Background);
    }
    private void ShowPendingUpdateNotice()
    {
        if(_pendingUpdateNotice==null||_updates==null||_updateNotice!=null||_exitRequested||_editingSettings
            ||_onboardingTour!=null||!IsVisible||WindowState==WindowState.Minimized)return;
        var release=_pendingUpdateNotice;_pendingUpdateNotice=null;
        _updateNotice=UpdateNotificationDialog.Build(this,_updates,release);
        _editingSettings=true;ReleaseGlobalShortcut();if(_hover!=null)_hover.Enabled=false;
        try{PresentUpdateNotice(_updateNotice);}
        finally
        {
            _updateNotice=null;_editingSettings=false;
            if(!_exitRequested&&IsVisible)
            {
                RefreshGlobalShortcut();RefreshDoubleCopy();RefreshHoverLookup();
                if(_desktopIntegration&&_ready&&!_preferences.OnboardingCompleted)StartOnboarding();
                QueuePendingUpdateNotice();
            }
        }
    }
}
