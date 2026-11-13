using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MemoLex.Services;

namespace MemoLex;
internal static class NotepadDeletionDialog
{
    internal static bool Confirm(Window owner,CloudNotepad pad,int words)=>Build(owner,pad,words).ShowDialog()==true;
    internal static Window Build(Window owner,CloudNotepad pad,int words)
    {
        var dialog=InputDialog.Shell(owner,"确认删除整个云词本",out var panel);dialog.Width=510;
        panel.Children.Add(new TextBlock {Text=$"删除「{pad.Title}」？",FontSize=20,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
        panel.Children.Add(InputDialog.Text($"当前操作会移除全部 {words} 个单词。这会删除整个云词本及其中的词条和章节，未保存的修改也会丢弃。此操作无法撤销。"));
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,22,0,0)};
        var cancel=new Button {Content="取消",IsDefault=true,IsCancel=true,Margin=new Thickness(0,0,10,0)};cancel.Click+=(_,_)=>dialog.Close();
        var confirm=new Button {Content="删除整个云词本",Background=new SolidColorBrush(Color.FromRgb(207,69,69)),Foreground=Brushes.White,BorderThickness=new Thickness(0)};
        confirm.Click+=(_,_)=>dialog.DialogResult=true;buttons.Children.Add(cancel);buttons.Children.Add(confirm);panel.Children.Add(buttons);
        dialog.RegisterName("CancelNotepadDeletion",cancel);dialog.RegisterName("ConfirmNotepadDeletion",confirm);return dialog;
    }
}
