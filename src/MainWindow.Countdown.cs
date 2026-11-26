using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace MemoLex;
public partial class MainWindow
{
    private DispatcherTimer? _examCountdownTimer;
    private void StartExamCountdownClock()
    {
        if(_examCountdownTimer!=null)return;
        UpdateExamCountdown();
        _examCountdownTimer=new(){Interval=TimeSpan.FromSeconds(30)};
        _examCountdownTimer.Tick+=(_,_)=>UpdateExamCountdown();
        _examCountdownTimer.Start();
        Activated+=(_,_)=>UpdateExamCountdown();
    }
    internal void UpdateExamCountdown(DateTime? today=null)
    {
        if(!_preferences.ShowExamCountdown||_preferences.ExamDate is not {} date)
        {
            ExamCountdownBadge.Visibility=Visibility.Collapsed;ExamCountdownText.Text="";return;
        }
        var days=Math.Max(0,(date.Date-(today??DateTime.Today).Date).Days);
        var name=string.IsNullOrWhiteSpace(_preferences.ExamName)?"考试":_preferences.ExamName.Trim();
        ExamCountdownText.Text=$"距 {name} 还有{days.ToString(CultureInfo.InvariantCulture)}天";
        ExamCountdownBadge.ToolTip=ExamCountdownText.Text;
        ExamCountdownBadge.Visibility=Visibility.Visible;
    }
}
