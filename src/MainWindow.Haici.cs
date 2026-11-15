using System.Windows;
using System.Windows.Controls;
using MemoLex.Desktop;

namespace MemoLex;
public partial class MainWindow
{
    private HaiciDictionaryWindow? _haiciWindow;
    internal Action<string>? HaiciDictionaryRequested {get;set;}
    private void HaiciButton_Click(object sender,RoutedEventArgs e)=>OpenHaici(_word);
    private void OpenHaici(string word)
    {
        if(!ValidWord(word))return;
        try
        {
            if(HaiciDictionaryRequested!=null){HaiciDictionaryRequested(word);return;}
            if(_haiciWindow==null)
            {
                _haiciWindow=new(this,word);_haiciWindow.Window.Closed+=(_,_)=>_haiciWindow=null;_haiciWindow.Window.Show();
            }
            else{_haiciWindow.ShowWord(word);if(_haiciWindow.Window.WindowState==WindowState.Minimized)_haiciWindow.Window.WindowState=WindowState.Normal;_haiciWindow.Window.Activate();}
        }
        catch{StatusText.Text="无法打开海词词典，请稍后重试。";}
    }
    private ContextMenu HaiciWordMenu(string word)
    {
        var menu=new ContextMenu();
        var embedded=new MenuItem {Header="在海词词典中查看"};embedded.Click+=(_,_)=>OpenHaici(word);menu.Items.Add(embedded);
        return menu;
    }
}
