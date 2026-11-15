using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;

namespace MemoLex;

internal sealed record TourSpot(Window Window,FrameworkElement Target);
internal sealed record TourStep(string Title,string Description,Func<TourSpot> Open);

internal sealed class OnboardingTour(IReadOnlyList<TourStep> steps,Action completed,bool showWindows=true):IDisposable
{
    private Panel? _root;private bool _finished;
    private readonly string[] _topics=steps.Select(step=>step.Title).ToArray();
    internal bool ChangingStep {get;private set;}
    internal int Index {get;private set;}
    internal Window? CurrentWindow {get;private set;}
    internal FrameworkElement? Target {get;private set;}
    internal SpotlightOverlay? Overlay {get;private set;}
    internal void Start()=>ShowStep(0);
    internal void Next(){if(Index==steps.Count-1)Skip();else ShowStep(Index+1);}
    internal void Previous(){if(Index>0)ShowStep(Index-1);}
    internal void Skip(){if(_finished)return;Dispose();completed();}
    internal bool HandleKey(KeyEventArgs e)
    {
        if(_finished)return false;
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        if(Overlay?.Topics.IsKeyboardFocusWithin==true)
        {
            if(key==Key.Escape&&!Overlay.Topics.IsDropDownOpen){e.Handled=true;Skip();}
            return true;
        }
        e.Handled=true;
        if(key==Key.Escape)Skip();else if(key is Key.Enter or Key.Right)Next();else if(key==Key.Left)Previous();
        return true;
    }
    private void PreviewKey(object sender,KeyEventArgs e)=>HandleKey(e);
    private void ShowStep(int index)
    {
        if(_finished)return;ChangingStep=true;
        try
        {
            Detach();Index=index;var step=steps[index];var spot=step.Open();CurrentWindow=spot.Window;Target=spot.Target;
            if(showWindows&&!CurrentWindow.IsVisible)CurrentWindow.Show();
            if(showWindows)CurrentWindow.Activate();
            _root=CurrentWindow.Content is Border {Child:Panel panel}?panel:CurrentWindow.Content as Panel;
            if(_root==null)throw new InvalidOperationException("当前页面无法显示引导。");
            if(!CurrentWindow.IsVisible||_root.ActualWidth<1){_root.Measure(new Size(CurrentWindow.Width,CurrentWindow.Height));_root.Arrange(new Rect(0,0,CurrentWindow.Width,CurrentWindow.Height));}
            _root.UpdateLayout();EnsureTargetVisible();_root.SizeChanged+=RootSizeChanged;
            Overlay=new(Target,step.Description,index,_topics,Previous,Next,Skip,ShowStep);
            Grid.SetRowSpan(Overlay,Math.Max(1,(_root as Grid)?.RowDefinitions.Count??1));Grid.SetColumnSpan(Overlay,Math.Max(1,(_root as Grid)?.ColumnDefinitions.Count??1));Panel.SetZIndex(Overlay,10000);
            _root.Children.Add(Overlay);CurrentWindow.PreviewKeyDown+=PreviewKey;
            FocusManager.SetFocusedElement(CurrentWindow,Target);if(showWindows)Keyboard.Focus(Target);
            _root.UpdateLayout();Overlay.Refresh();_root.UpdateLayout();
        }
        catch{Dispose();completed();throw;}
        finally{ChangingStep=false;}
    }
    private void Detach()
    {
        if(CurrentWindow!=null)CurrentWindow.PreviewKeyDown-=PreviewKey;
        if(_root!=null)_root.SizeChanged-=RootSizeChanged;
        if(Overlay!=null){Overlay.Dispose();_root?.Children.Remove(Overlay);}
        Overlay=null;_root=null;CurrentWindow=null;Target=null;
    }
    private void RootSizeChanged(object sender,SizeChangedEventArgs e)
    {
        var root=_root;var target=Target;
        root?.Dispatcher.BeginInvoke(()=>{if(!_finished&&_root==root&&Target==target)EnsureTargetVisible();},System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void EnsureTargetVisible()
    {
        if(_root==null||Target==null)return;
        Target.BringIntoView();_root.UpdateLayout();
        for(var parent=VisualTreeHelper.GetParent(Target);parent!=null;parent=VisualTreeHelper.GetParent(parent))
        {
            if(parent is not ScrollViewer scroll||scroll.ActualHeight<1)continue;
            var viewport=scroll.Template?.FindName("PART_ScrollContentPresenter",scroll) as FrameworkElement??scroll;
            var bounds=Target.TransformToAncestor(viewport).TransformBounds(new Rect(Target.RenderSize));
            if(bounds.Top<6)scroll.ScrollToVerticalOffset(Math.Max(0,scroll.VerticalOffset+bounds.Top-6));
            else if(bounds.Bottom>viewport.ActualHeight-6)scroll.ScrollToVerticalOffset(scroll.VerticalOffset+bounds.Bottom-viewport.ActualHeight+6);
            _root.UpdateLayout();
        }
        Overlay?.Refresh();
    }
    public void Dispose(){if(_finished)return;_finished=true;Detach();}
}

internal sealed class SpotlightOverlay:Canvas,IDisposable
{
    private readonly FrameworkElement _target;private readonly Border _card;
    private readonly TextBlock _description,_progress;
    internal Button Next {get;}internal Button Previous {get;}internal Button Skip {get;}
    internal ComboBox Topics {get;}
    internal Rect FocusBounds {get;private set;}
    internal Rect CardBounds {get;private set;}
    internal SpotlightOverlay(FrameworkElement target,string description,int index,IReadOnlyList<string> topics,Action previous,Action next,Action skip,Action<int> jump)
    {
        _target=target;HorizontalAlignment=HorizontalAlignment.Stretch;VerticalAlignment=VerticalAlignment.Stretch;ClipToBounds=true;Focusable=false;
        WindowChrome.SetIsHitTestVisibleInChrome(this,true);
        var body=new StackPanel();var heading=new Grid{Margin=new Thickness(0,0,0,14)};heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});heading.ColumnDefinitions.Add(new());body.Children.Add(heading);
        _progress=new TextBlock{Text=$"{index+1} / {topics.Count}",Foreground=new SolidColorBrush(Color.FromRgb(20,142,190)),FontSize=11,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)};heading.Children.Add(_progress);
        Topics=new ComboBox{ItemsSource=topics,SelectedIndex=index,MinHeight=32,FontSize=13,FontWeight=FontWeights.SemiBold,ToolTip="选择要了解的功能"};Grid.SetColumn(Topics,1);heading.Children.Add(Topics);
        Topics.SelectionChanged+=(_,e)=>{if(Topics.SelectedIndex<0||Topics.SelectedIndex==index)return;var selected=Topics.SelectedIndex;Topics.IsDropDownOpen=false;e.Handled=true;jump(selected);};
        _description=new TextBlock{Text=description,FontSize=13,LineHeight=23,TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(81,111,135))};body.Children.Add(_description);
        var buttons=new WrapPanel{Margin=new Thickness(0,18,0,0),HorizontalAlignment=HorizontalAlignment.Right};body.Children.Add(buttons);
        Skip=new Button{Content="跳过",Padding=new Thickness(8,6,8,6),Margin=new Thickness(0,0,7,0)};Previous=new Button{Content="上一步",IsEnabled=index>0,Padding=new Thickness(9,6,9,6),Margin=new Thickness(0,0,7,0)};
        Next=new Button{Content=index==topics.Count-1?"完成":"下一步",Padding=new Thickness(11,6,11,6),Style=(Style)Application.Current.FindResource("AccentButton")};buttons.Children.Add(Skip);buttons.Children.Add(Previous);buttons.Children.Add(Next);
        Previous.Click+=(_,_)=>previous();Next.Click+=(_,_)=>next();Skip.Click+=(_,_)=>skip();
        _card=new Border{Child=body,Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(113,205,249)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(18),Effect=new DropShadowEffect{BlurRadius=20,ShadowDepth=4,Opacity=0.22,Color=Color.FromRgb(16,42,62)}};Children.Add(_card);
        LayoutUpdated+=UpdateSpot;SizeChanged+=(_,_)=>UpdateSpot(this,EventArgs.Empty);
    }
    private void UpdateSpot(object? sender,EventArgs e)
    {
        if(Parent is not FrameworkElement root||root.ActualWidth<1||root.ActualHeight<1)return;
        try
        {
            var rect=_target.TransformToAncestor(root).TransformBounds(new Rect(_target.RenderSize));rect.Inflate(5,5);rect.Intersect(new Rect(0,0,root.ActualWidth,root.ActualHeight));
            if(rect!=FocusBounds){FocusBounds=rect;InvalidateMeasure();InvalidateVisual();}
        }
        catch(InvalidOperationException){}
    }
    internal void Refresh()=>UpdateSpot(this,EventArgs.Empty);
    protected override Size MeasureOverride(Size constraint)
    {
        var size=new Size(double.IsFinite(constraint.Width)?constraint.Width:760,double.IsFinite(constraint.Height)?constraint.Height:520);
        var left=FocusBounds.Left-28;var right=size.Width-FocusBounds.Right-28;
        var available=right>=240?right:left>=240?left:size.Width-32;
        _card.Measure(new Size(Math.Min(340,available),Math.Max(150,size.Height-32)));return size;
    }
    protected override Size ArrangeOverride(Size size)
    {
        var width=Math.Min(_card.DesiredSize.Width,size.Width-32);var height=Math.Min(_card.DesiredSize.Height,size.Height-32);
        var x=FocusBounds.Right+width+28<=size.Width?FocusBounds.Right+14:FocusBounds.Left-width-14>=16?FocusBounds.Left-width-14:size.Width-width-16;
        var y=Math.Clamp(FocusBounds.Top,16,Math.Max(16,size.Height-height-16));
        if(x<FocusBounds.Right&&x+width>FocusBounds.Left)
        {
            if(FocusBounds.Bottom+height+28<=size.Height)y=FocusBounds.Bottom+14;
            else if(FocusBounds.Top-height-14>=16)y=FocusBounds.Top-height-14;
        }
        CardBounds=new Rect(Math.Max(16,x),y,width,height);_card.Arrange(CardBounds);return size;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);var full=new RectangleGeometry(new Rect(RenderSize));var hole=new RectangleGeometry(FocusBounds,8,8);
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150,15,34,51)),null,new CombinedGeometry(GeometryCombineMode.Exclude,full,hole));
        dc.DrawRoundedRectangle(null,new Pen(new SolidColorBrush(Color.FromRgb(46,184,244)),2.5),FocusBounds,8,8);
    }
    protected override HitTestResult HitTestCore(PointHitTestParameters p)=>new PointHitTestResult(this,p.HitPoint);
    public void Dispose(){Topics.IsDropDownOpen=false;LayoutUpdated-=UpdateSpot;}
}
