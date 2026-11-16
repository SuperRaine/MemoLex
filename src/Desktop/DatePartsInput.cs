using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MemoLex.Desktop;

internal sealed class DatePartsInput:Grid
{
    internal ComboBox Year {get;}=Part("年份");
    internal ComboBox Month {get;}=Part("月份");
    internal ComboBox Day {get;}=Part("日期");
    private bool _updating;
    private int _dayCount=31;
    internal DatePartsInput(DateTime? date=null)
    {
        Margin=new Thickness(0,4,0,4);MaxWidth=480;HorizontalAlignment=HorizontalAlignment.Left;
        foreach(var width in new[]{new GridLength(1.4,GridUnitType.Star),GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})ColumnDefinitions.Add(new(){Width=width});
        Year.MinWidth=100;Month.MinWidth=72;Day.MinWidth=72;
        Year.ItemsSource=Enumerable.Range(Math.Max(1,DateTime.Today.Year-1),22).Concat(date.HasValue?[date.Value.Year]:Array.Empty<int>()).Distinct().Order().ToArray();
        Month.ItemsSource=Enumerable.Range(1,12).ToArray();Day.ItemsSource=Enumerable.Range(1,31).ToArray();
        var index=0;
        foreach(var (part,label) in new[]{(Year,"年"),(Month,"月"),(Day,"日")})
        {
            Grid.SetColumn(part,index++);Children.Add(part);
            var suffix=new TextBlock {Text=label,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,0,label=="日"?0:12,0)};
            Grid.SetColumn(suffix,index++);Children.Add(suffix);
        }
        foreach(var part in new[]{Year,Month})
        {
            part.SelectionChanged+=(_,_)=>RefreshDays();
            part.AddHandler(TextBoxBase.TextChangedEvent,new TextChangedEventHandler((_,_)=>RefreshDays()));
        }
        Day.DropDownOpened+=(_,_)=>RefreshDays();
        SetDate(date);
    }
    private static ComboBox Part(string name)
    {
        var combo=new ComboBox {IsEditable=true,IsTextSearchEnabled=false,StaysOpenOnEdit=true,MinHeight=36,MaxDropDownHeight=260,ToolTip="选择或输入"+name};
        AutomationProperties.SetName(combo,name);return combo;
    }
    internal void SetDate(DateTime? date)
    {
        _updating=true;
        Year.Text=date?.Year.ToString(CultureInfo.InvariantCulture)??"";Month.Text=date?.Month.ToString(CultureInfo.InvariantCulture)??"";
        _updating=false;RefreshDays();Day.Text=date?.Day.ToString(CultureInfo.InvariantCulture)??"";
    }
    private static bool Number(ComboBox part,out int value)=>int.TryParse(part.Text.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out value);
    private void RefreshDays()
    {
        if(_updating)return;
        var count=Number(Year,out var year)&&year is >=1 and <=9999&&Number(Month,out var month)&&month is >=1 and <=12?DateTime.DaysInMonth(year,month):31;
        if(count==_dayCount)return;
        _updating=true;var text=Day.Text;Day.ItemsSource=Enumerable.Range(1,count).ToArray();Day.Text=text;_dayCount=count;_updating=false;
    }
    internal bool TryGetDate(out DateTime? date,out string? error)
    {
        date=null;error=null;RefreshDays();
        if(new[]{Year,Month,Day}.All(part=>string.IsNullOrWhiteSpace(part.Text)))return true;
        if(!Number(Year,out var year)||year is <1 or >9999){error="请选择或输入有效年份（1–9999）。";return false;}
        if(!Number(Month,out var month)||month is <1 or >12){error="请选择或输入有效月份（1–12）。";return false;}
        var days=DateTime.DaysInMonth(year,month);
        if(!Number(Day,out var day)||day<1||day>days){error=$"{year}年{month}月的日期应为 1–{days}。";return false;}
        date=new(year,month,day);return true;
    }
}
