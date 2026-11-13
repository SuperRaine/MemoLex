using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MemoLex;
using MemoLex.Core;
using MemoLex.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/MemoLex;component/Theme.xaml", UriKind.Relative) });
        if(args.FirstOrDefault()=="--icon-only")
        {
            var directory=Path.GetFullPath(args[2]);Directory.CreateDirectory(directory);
            new UiFixture(directory,null).VerifyNativeIcon(args[1]);Console.WriteLine("PASS Published EXE contains the owl icon");return 0;
        }
        var exitCode = 1;
        application.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await new UiFixture(args.FirstOrDefault() ?? Path.GetFullPath("work/MemoLex/ui-review"), args.ElementAtOrDefault(1)).RunAsync();
                exitCode = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { application.Shutdown(); }
        });
        application.Run();
        return exitCode;
    }
}

internal sealed class UiFixture
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MemoLex.UITests." + Guid.NewGuid().ToString("N"));
    private readonly string _previews;
    private readonly string? _nativeExecutable;
    private MainWindow _window = null!;
    private LocalDictionary? _reverseDictionary;
    private int _passed;
    public UiFixture(string previews, string? nativeExecutable) { _previews = previews; _nativeExecutable = nativeExecutable; }
    private T Control<T>(string name) where T : FrameworkElement => (T)_window.FindName(name);
    private string Word => Control<TextBlock>("WordText").Text;
    private ScrollViewer Scroll => Control<ScrollViewer>("ResultScroll");
    private ComboBox Filter => Control<ComboBox>("ExamplePaperCombo");
    private StackPanel Examples => Control<StackPanel>("ExamplesPanel");
    private void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private void Pass(string name) { _passed++; Console.WriteLine("PASS " + name); }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private void Click(string name) => Click(Control<Button>(name));
    private static async Task DrainAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(15);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
    private static async Task WaitAsync(Func<bool> ready, string message)
    {
        var timer = Stopwatch.StartNew();
        while (!ready())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(6)) throw new Exception("Timed out: " + message);
            await Task.Delay(15);
        }
        await DrainAsync();
    }
    private async Task ReadyAsync(string word) => await WaitAsync(() => Word == word && !Control<TextBlock>("ExamplesHintText").Text.StartsWith("正在") && !Control<TextBlock>("NotesHintText").Text.StartsWith("正在")&&!Control<TextBlock>("RelatedHintText").Text.StartsWith("正在"), "lookup " + word);
    private async Task SearchAsync(string word)
    {
        Control<TextBox>("SearchBox").Text = word;
        Click("SearchButton");
        await ReadyAsync(word);
    }
    private async Task BackAsync(string word)
    {
        Check(Control<Button>("BackButton").IsEnabled, "Back was disabled");
        Click("BackButton"); await ReadyAsync(word);
    }
    private async Task ForwardAsync(string word)
    {
        Check(Control<Button>("ForwardButton").IsEnabled, "Forward was disabled");
        Click("ForwardButton"); await ReadyAsync(word);
    }
    private static IEnumerable<TextBlock> Blocks(DependencyObject node)
    {
        if (node is TextBlock text) yield return text;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var block in Blocks(child)) yield return block;
    }
    private static IEnumerable<DependencyObject> LogicalNodes(DependencyObject node)
    {
        yield return node;
        foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())foreach(var item in LogicalNodes(child))yield return item;
    }
    private static IEnumerable<DependencyObject> VisualNodes(DependencyObject node)
    {
        yield return node;
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(node);index++)foreach(var item in VisualNodes(VisualTreeHelper.GetChild(node,index)))yield return item;
    }
    private void WithModifiers(ModifierKeys modifiers,Action action)
    {
        // This modifies only this test thread's key-state table; it sends no
        // keyboard input and does not affect another app or the physical keys.
        var original=new byte[256];Check(GetKeyboardState(original),"Could not snapshot test-thread keyboard state");
        var state=(byte[])original.Clone();foreach(var key in new[]{0x10,0x11,0x12,0xA0,0xA1,0xA2,0xA3,0xA4,0xA5,0x5B,0x5C})state[key]&=0x7f;
        if((modifiers&ModifierKeys.Control)!=0)state[0x11]=state[0xA2]=0x80;
        if((modifiers&ModifierKeys.Shift)!=0)state[0x10]=state[0xA0]=0x80;
        try{Check(SetKeyboardState(state),"Could not set test-thread modifiers");Check(Keyboard.Modifiers==modifiers,"WPF did not observe test-thread modifiers");action();}
        finally{SetKeyboardState(original);}
    }
    private void NativeSelect(ListBox list,object row,ModifierKeys modifiers)
    {
        Check(!list.IsVisible,"Selection fixture must stay hidden");
        var item=list.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem??throw new Exception("Selection item has no generated container");
        var click=typeof(ListBox).GetMethod("NotifyListItemClicked",BindingFlags.Instance|BindingFlags.NonPublic)??throw new Exception("WPF native item selection method missing");
        WithModifiers(modifiers,()=>click.Invoke(list,new object[]{item,MouseButton.Left}));
        Check(Mouse.Captured!=list,"Hidden selection test captured desktop input");
    }
    [DllImport("user32.dll")]private static extern bool GetKeyboardState([Out]byte[] state);
    [DllImport("user32.dll")]private static extern bool SetKeyboardState(byte[] state);
    private static Hyperlink Link(DependencyObject node, string word)
    {
        var links = Blocks(node).SelectMany(block => block.Inlines.OfType<Hyperlink>()).ToArray();
        return links.FirstOrDefault(link => Equals(link.ToolTip, "查询 " + word))
            ?? throw new Exception($"Missing link {word}; found: {string.Join(", ", links.Select(link=>link.ToolTip))}");
    }
    private static void ClickLink(Hyperlink link) => link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
    private static string InlineText(Inline inline)=>inline switch {Run run=>run.Text,Span span=>string.Concat(span.Inlines.Select(InlineText)),LineBreak=>"\n",_=>""};
    private string DisplayedText(string name)=>string.Concat(Control<TextBlock>(name).Inlines.Select(InlineText));
    private void Layout(double width = 1160, double height = 800)
    {
        var root = (FrameworkElement)_window.Content;
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
    }
    private double SectionOffset(string name) => Control<FrameworkElement>(name).TranslatePoint(new Point(), (UIElement)Scroll.Content).Y;
    private async Task ScrollToAsync(double offset)
    {
        Layout(); Scroll.ScrollToVerticalOffset(offset); await DrainAsync();
    }
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject value)
    {
        while ((value=VisualTreeHelper.GetParent(value)) != null) yield return value;
    }
    private static void Render(Window window, double width, double height, string path)
    {
        var root = (FrameworkElement)window.Content;
        // TextBox's text view requires a presentation source. This host is never
        // visible (no WS_VISIBLE), never activated and receives no desktop input.
        TextElement.SetFontFamily(root, window.FontFamily);
        TextElement.SetFontSize(root, window.FontSize);
        TextElement.SetForeground(root, window.Foreground);
        window.Content = null;
        try
        {
            using var host = new HwndSource(new HwndSourceParameters("MemoLex off-screen render") { WindowStyle=unchecked((int)0x80000000), Width=(int)width, Height=(int)height, PositionX=-32000, PositionY=-32000 });
            host.RootVisual = root;
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(path); encoder.Save(output);
            host.RootVisual = null;
        }
        finally { window.Content = root; }
    }
    public async Task RunAsync()
    {
        Directory.CreateDirectory(_root); Directory.CreateDirectory(_previews);
        try
        {
            var data = Path.Combine(_root, "data"); Directory.CreateDirectory(data); CreateDictionary(data);
            var prefs = Preferences.Load(Path.Combine(_root, "preferences.json")); prefs.RecordLookup("talkative");
            var tokens = new TokenStore(Path.Combine(_root, "token.dpapi")); tokens.SaveToken(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            var examples = new ExampleSentenceClient(new ExampleHandler(), TimeSpan.Zero);
            var privateHandler = new PrivateHandler {Content="# 我的单词\ntalkative\nreader"};
            File.WriteAllText(Path.Combine(_root,"seed-mnemonics.json"),JsonSerializer.Serialize(new Dictionary<string,StoredMnemonic[]>{{"talkative",[new("seed:talkative","词根词缀","本地初始助记 talk + -ative",Origin:"builtin")]}}));
            privateHandler.NotesBarrier=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var mnemonics=new MnemonicStore(Path.Combine(_root,"mnemonics.user.json"),Path.Combine(_root,"seed-mnemonics.json"));
            var localDictionary=new LocalDictionary(data);
            var relatedData=Environment.GetEnvironmentVariable("MEMOLEX_DATA_DIR")??new[]{"outputs/MemoLex/data","../MemoLex/data","data"}.FirstOrDefault(value=>File.Exists(Path.Combine(value,"related.data")))??throw new Exception("Set MEMOLEX_DATA_DIR to the runtime data folder.");
            _reverseDictionary=new LocalDictionary(relatedData);await _reverseDictionary.LoadAsync();
            _window = new MainWindow(prefs, tokens, localDictionary, examples, api: new MaiMemoClient(tokens, privateHandler, TimeSpan.Zero),
                related:new RelatedWords(localDictionary,relatedData),translation:new TranslationClient(new ParagraphHandler()),chinese:new ChineseReverseSearch(_reverseDictionary,relatedData),mnemonics:mnemonics);
            var initializing=_window.InitializeAsync();
            await WaitAsync(()=>Blocks(Control<StackPanel>("NotesPanel")).Any(block=>string.Concat(block.Inlines.Select(InlineText)).Contains("本地初始助记")),"immediate local mnemonic");
            privateHandler.NotesBarrier.SetResult();privateHandler.NotesBarrier=null;
            await initializing; Layout(); await DrainAsync();
            Check(!Blocks(Control<StackPanel>("NotesPanel")).Any(block=>string.Concat(block.Inlines.Select(InlineText)).Contains("本地初始助记"))&&mnemonics.Get("talkative").Single().Origin=="cloud","Cloud note did not override and cache the local note");
            Pass("Local mnemonics render before cloud requests finish and cloud notes override and cache them");
            Check(Word == "talkative" && Examples.Children.Count == 10 && Filter.Items.Count == 5, "Initial content missing");
            Check(Control<TextBlock>("CloudStatusText").Text == "已连接" && Control<ComboBox>("NotepadCombo").Items.Count == 1, "Account/notebook status missing");
            foreach (var name in new[] { "BackButton", "ForwardButton", "PinButton", "SettingsButton", "MaximizeButton" })
            {
                var button = Control<Button>(name); var position = button.TranslatePoint(new Point(), (UIElement)_window.Content);
                Check(position.Y >= 0 && position.Y + button.ActualHeight <= 57, name + " is outside title bar");
            }
            Pass("The real WPF view initializes with the owl resources, title controls, account and all five exam types");
            Layout(1000,680);
            Check(Control<Button>("SettingsButton").ActualWidth > 90 && Control<TextBox>("SearchBox").ActualWidth > 100, "Compact controls collapsed");
            Check(SectionOffset("NotesSection")>SectionOffset("DefinitionSection") && SectionOffset("ExamplesSection")>SectionOffset("NotesSection"),"Definitions, mnemonics and examples are out of order");
            foreach(var name in new[]{"DefinitionText","NotesPanel","ExamplesPanel"})
                Check(Ancestors(Control<FrameworkElement>(name)).OfType<ScrollViewer>().SequenceEqual(new[]{Scroll}),name+" has a separate scrolling view");
            Pass("One scrolling page presents definitions, mnemonics and examples in order at both window sizes");
            Layout(760,520);await DrainAsync();Layout(760,520);
            var compactRoot=(FrameworkElement)_window.Content;
            foreach(var name in new[]{"AddButton","ResultScroll","SettingsButton","DictionaryCountText"})
            {
                var item=Control<FrameworkElement>(name);var point=item.TranslatePoint(new Point(),compactRoot);
                Check(point.X>=0&&point.X+item.ActualWidth<=761&&point.Y+item.ActualHeight<=521,name+" clipped at minimum size");
            }
            Check(_window.FindName("EnglishText")==null&&!Blocks(compactRoot).Any(block=>block.Text=="英英释义"),"English definitions remain visible");
            foreach(var size in new[]{new Size(760,520),new Size(1000,680),new Size(1160,800)})
            {
                Layout(size.Width,size.Height);await DrainAsync();Layout(size.Width,size.Height);
                var relationHeader=Control<Grid>("RelationsHeader");var relationSection=Control<StackPanel>("RelatedSection");
                var toggles=new[]{Control<ToggleButton>("SynonymsToggle"),Control<ToggleButton>("SimilarToggle"),Control<ToggleButton>("DerivativesToggle"),Control<ToggleButton>("PhrasesToggle")};
                Check(relationSection.ActualHeight<=38&&toggles.All(toggle=>toggle.TranslatePoint(new Point(),relationHeader).Y<1),"Related-word buttons take multiple rows when collapsed");
                Check(toggles.Last().TranslatePoint(new Point(),relationHeader).X+toggles.Last().ActualWidth<=relationHeader.ActualWidth+1,"Compact related-word buttons are clipped");
            }
            Control<ToggleButton>("SynonymsToggle").IsChecked=true;Layout();await DrainAsync();Layout();
            Check(Ancestors(Control<StackPanel>("SynonymsPanel")).OfType<Border>().First().Visibility==Visibility.Visible&&Control<StackPanel>("SynonymsPanel").ActualWidth>Control<Border>("WordCard").ActualWidth*.7,"Related words do not expand below the shared header at full width");
            Control<ToggleButton>("SynonymsToggle").IsChecked=false;Layout(760,520);await DrainAsync();Layout(760,520);
            Check(Control<StackPanel>("RelatedSection").ActualHeight<=38,"Closing a related-word group leaves empty space");
            Pass("Related words use one compact row at all sizes and expand into full-width content without empty gaps");
            var sidebar=Control<FrameworkElement>("SidebarLayout");var cloudCard=Control<Border>("CloudNotepadCard");
            Check(Math.Abs(cloudCard.TranslatePoint(new Point(),sidebar).Y+cloudCard.ActualHeight-sidebar.ActualHeight)<2&&Control<ListBox>("HistoryList").ActualHeight>=20,"Cloud card is not anchored or history collapsed at minimum size");
            Check(Control<Button>("EditNotepadButton").TranslatePoint(new Point(),compactRoot).Y+Control<Button>("EditNotepadButton").ActualHeight<520,"Cloud controls are clipped in short windows");
            Pass("The 760x520 layout keeps the cloud card at the bottom, retains all controls and removes English definitions");
            foreach(var size in new[]{new Size(760,520),new Size(1000,680),new Size(1160,800),new Size(1160,1200)})
            {
                Layout(size.Width,size.Height);await DrainAsync();Layout(size.Width,size.Height);
                Check(Math.Abs(cloudCard.TranslatePoint(new Point(),sidebar).Y+cloudCard.ActualHeight-sidebar.ActualHeight)<2,"Cloud card moved away from the bottom");
                Check(Math.Abs(Control<ListBox>("SuggestionList").ActualHeight-Control<ListBox>("HistoryList").ActualHeight)<=1,"Suggestions and history do not split the available height equally");
            }
            Layout();await DrainAsync();
            Pass("Suggestions and history split the available height equally at minimum, compact, normal and tall sizes");
            var closePrompts=0;prefs.RememberCloseAction=false;
            _window.ChooseCloseAction=(_,_)=>{closePrompts++;return null;};
            Check(_window.ResolveCloseAction()==null&&!prefs.RememberCloseAction,"Cancelling the close prompt changed the preference");
            _window.ChooseCloseAction=(_,_)=>{closePrompts++;return new CloseChoice(false,false);};
            Check(_window.ResolveCloseAction()==false&&!prefs.RememberCloseAction,"A temporary exit choice was remembered");
            _window.ChooseCloseAction=(_,_)=>{closePrompts++;return new CloseChoice(true,true);};
            Check(_window.ResolveCloseAction()==true&&Preferences.Load(Path.Combine(_root,"preferences.json")).RememberCloseAction,"Remembered tray choice was not saved");
            _window.ChooseCloseAction=(_,_)=>throw new Exception("Remembered close action prompted again");
            Check(_window.ResolveCloseAction()==true&&closePrompts==3,"Remembered action did not bypass the prompt");
            prefs.RememberCloseAction=false;_window.ChooseCloseAction=CloseBehaviorDialog.Ask;
            Pass("Close prompting supports cancellation, one-time exit and a remembered tray choice");

            await SearchAsync("reader"); await SearchAsync("learning");
            await BackAsync("reader"); await BackAsync("talkative");
            await ForwardAsync("reader"); await ForwardAsync("learning");
            Pass("Search-button navigation traverses both directions across every query");

            await ScrollToAsync(SectionOffset("ExamplesSection")+200);
            var allScroll=Scroll.VerticalOffset;
            Filter.SelectedItem=PaperTypeChoice.All.First(x=>x.Value=="CET6"); await ReadyAsync("learning");
            Click("MoreExamplesButton"); await WaitAsync(()=>Examples.Children.Count==20,"second example page");
            await ScrollToAsync(SectionOffset("ExamplesSection")+400);
            var savedScroll=Scroll.VerticalOffset;
            Check(savedScroll > 0,"Example scroll setup failed");
            ClickLink(Link(Examples,"reader")); await ReadyAsync("reader");
            Check(Scroll.VerticalOffset<2,"Example word did not open the top of the dictionary");
            await BackAsync("learning"); Layout(); await DrainAsync();
            Check(Examples.Children.Count==20 && ((PaperTypeChoice)Filter.SelectedItem).Value=="CET6", "Example pagination/filter snapshot was lost");
            Check(Math.Abs(Scroll.VerticalOffset-savedScroll)<2,"Example scroll position was lost");
            await BackAsync("learning");
            Check(((PaperTypeChoice)Filter.SelectedItem).Value=="ALL" && Examples.Children.Count==10,"Previous exam scope was lost");
            Check(Math.Abs(Scroll.VerticalOffset-allScroll)<2,"Prior exam-filter reading position was lost");
            await ForwardAsync("learning");
            Check(((PaperTypeChoice)Filter.SelectedItem).Value=="CET6" && Examples.Children.Count==20,"Forward lost expanded examples");
            Pass("Example word links preserve the exam type, loaded pages and scroll in both directions");

            await SearchAsync("talkative");
            ClickLink(Link(Control<TextBlock>("DefinitionText"),"reader")); await ReadyAsync("reader"); await BackAsync("talkative");
            ClickLink(Link(Control<TextBlock>("ExchangeText"),"talking")); await ReadyAsync("talking"); await BackAsync("talkative");
            Check(!Control<TextBlock>("DefinitionText").Inlines.OfType<Hyperlink>().Any(x=>new TextRange(x.ContentStart,x.ContentEnd).Text=="adj"),"Part-of-speech label became a word link");
            Pass("Embedded English in Chinese definitions and word forms share the navigation journal");
            await ScrollToAsync(SectionOffset("NotesSection")); var notesScroll=Scroll.VerticalOffset;
            ClickLink(Link(Control<StackPanel>("NotesPanel"),"talk")); await ReadyAsync("talk"); await BackAsync("talkative");
            Check(Math.Abs(Scroll.VerticalOffset-notesScroll)<2,"Mnemonic reading position was lost");
            Pass("Mnemonic word links return to the same position on the combined page");

            await ScrollToAsync(0);
            var cet6=Control<WrapPanel>("TagsPanel").Children.OfType<Button>().First(b=>((TextBlock)b.Content).Text=="CET-6");
            Click(cet6); await ReadyAsync("talkative");
            Check(((PaperTypeChoice)Filter.SelectedItem).Value=="CET6" && Math.Abs(Scroll.VerticalOffset-(SectionOffset("ExamplesSection")-8))<2,"Exam tag did not scroll to matching examples");
            await BackAsync("talkative"); Check(Scroll.VerticalOffset<2,"Same-filter exam tag did not preserve its source position");
            await ForwardAsync("talkative"); Check(Scroll.VerticalOffset>notesScroll,"Forward did not return to the examples anchor");
            Pass("Even a same-filter exam tag scrolls to examples and preserves a back/forward destination");

            Control<TextBox>("SearchBox").Text="nov";
            Control<ListBox>("SuggestionList").SelectedItem="novel"; await ReadyAsync("novel");
            await BackAsync("talkative"); await ForwardAsync("novel");
            var history=Control<ListBox>("HistoryList"); history.SelectedItem=history.Items.OfType<SearchHistoryEntry>().First(x=>x.Word=="reader"); await ReadyAsync("reader");
            await BackAsync("novel"); await SearchAsync("talk");
            Check(!Control<Button>("ForwardButton").IsEnabled,"A new lookup did not replace the old forward branch");
            Pass("Suggestions and history use the same navigation, including new branches after Back");

            Control<TextBox>("SearchBox").Text="slow"; Click("SearchButton");
            await Task.Delay(30); await SearchAsync("fast"); await Task.Delay(250);
            Check(Word=="fast" && Blocks(Examples).Any(x=>new TextRange(x.ContentStart,x.ContentEnd).Text.Contains("fast")),"A canceled result overwrote the current word");
            await BackAsync("slow"); await ForwardAsync("fast");
            Pass("Rapid searches cancel stale results and retain their correct navigation destinations");
            await SearchAsync("empty"); Check(Examples.Children.Count==0 && Control<TextBlock>("ExamplesHintText").Text.Contains("没有找到"),"Empty state missing");
            await SearchAsync("retry"); Check(Control<Button>("RetryExamplesButton").Visibility==Visibility.Visible,"Retry missing after HTTP 429");
            Click("RetryExamplesButton"); await WaitAsync(()=>Examples.Children.Count==10,"retry results");
            Pass("Empty results and rate-limit retry remain usable with navigation");

            Click("PinButton"); Check(_window.Topmost && prefs.AlwaysOnTop,"Pin state was not saved");
            Click("PinButton"); Check(!_window.Topmost && !prefs.AlwaysOnTop,"Unpin state was not saved");
            Check(privateHandler.Writes==0,"The read-only UI checks attempted a cloud write");
            Pass("The title pin toggles and persists, and UI checks never write to cloud data");

            await SearchAsync("talkative");
            await WaitAsync(()=>Control<Button>("AddButton").IsEnabled&&Control<Button>("AddButton").Content.ToString()!.Contains("取消"),"word membership");
            Click("AddButton");await WaitAsync(()=>privateHandler.Writes==1&&Control<Button>("AddButton").IsEnabled,"remove cloud word");
            Check(!privateHandler.Content.Split('\n').Contains("talkative")&&Control<Button>("AddButton").Content.ToString()!.Contains("加入"),"Remove did not update the button and word list");
            Click("AddButton");await WaitAsync(()=>privateHandler.Writes==2&&Control<Button>("AddButton").IsEnabled,"add cloud word back");
            Check(privateHandler.Content.Split('\n').Contains("talkative")&&Control<Button>("AddButton").Content.ToString()!.Contains("取消"),"Add did not restore membership");
            Pass("The cloud button removes and re-adds a word using a simulated notepad and accurate membership state");

            Control<ToggleButton>("SynonymsToggle").IsChecked=true;Control<ToggleButton>("DerivativesToggle").IsChecked=true;
            await ScrollToAsync(SectionOffset("SynonymsPanel"));var relationScroll=Scroll.VerticalOffset;
            ClickLink(Link(Control<StackPanel>("SynonymsPanel"),"loquacious"));await ReadyAsync("loquacious");await BackAsync("talkative");
            Check(Control<ToggleButton>("SynonymsToggle").IsChecked==true&&Math.Abs(Scroll.VerticalOffset-relationScroll)<2,"Related-word navigation lost expansion/scroll");
            ClickLink(Link(Control<StackPanel>("DerivativesPanel"),"talkativeness"));await ReadyAsync("talkativeness");await BackAsync("talkative");
            await SearchAsync("form");Control<ToggleButton>("SimilarToggle").IsChecked=true;
            ClickLink(Link(Control<StackPanel>("SimilarPanel"),"from"));await ReadyAsync("from");await BackAsync("form");
            Pass("Synonym, derivative and similar-spelling links navigate with meanings and restore expanded groups");
            await SearchAsync("talk");
            await WaitAsync(()=>Blocks(Control<StackPanel>("PhrasesPanel")).Any(block=>string.Concat(block.Inlines.Select(InlineText)).Contains("谈论")),"related phrase meanings");
            Control<ToggleButton>("PhrasesToggle").IsChecked=true;await ScrollToAsync(SectionOffset("PhrasesPanel"));var phraseScroll=Scroll.VerticalOffset;
            ClickLink(Link(Control<StackPanel>("PhrasesPanel"),"talk about"));await ReadyAsync("talk about");
            Check(DisplayedText("DefinitionText").Contains("谈论"),"The full phrase did not open its Chinese definition");
            await BackAsync("talk");
            Check(Control<ToggleButton>("PhrasesToggle").IsChecked==true&&Math.Abs(Scroll.VerticalOffset-phraseScroll)<2,"Phrase navigation lost the expanded group or scroll position");
            Control<ToggleButton>("PhrasesToggle").IsChecked=false;
            Pass("Related phrases display Chinese meanings and navigate as whole phrases with Back restoring the group");
            Control<TextBox>("SearchBox").Text="rés";await DrainAsync();
            Check(Control<ListBox>("SuggestionList").Items.Cast<object>().Contains("resume"),"Accented input did not find the plain-spelling suggestion");
            Control<TextBox>("SearchBox").Text="résumé";Click("SearchButton");await ReadyAsync("resume");
            Check(DisplayedText("DefinitionText").Contains("简历"),"Accented search did not open its normalized dictionary spelling");
            Pass("Accented English input searches the corresponding plain spelling and suggestions");

            await SearchAsync("talkative");
            Check(Blocks(Examples).SelectMany(block=>block.Inlines.OfType<Hyperlink>()).Any(link=>Equals(link.ToolTip,"查询 talkative")&&link.Background is SolidColorBrush brush&&brush.Color.A>0),"English keyword highlight missing");
            Check(Blocks(Examples).SelectMany(block=>block.Inlines.OfType<Run>()).Any(run=>run.Text.Contains("健谈")&&run.Background is SolidColorBrush brush&&brush.Color.A>0),"Translated keyword highlight missing");
            Pass("English example keywords and matching Chinese meaning spans are highlighted");

            Click("TranslationModeButton");Control<TextBox>("TranslationInput").Text="A talkative reader enjoys learning.";Click("TranslateButton");
            Layout();await DrainAsync();
            await WaitAsync(()=>Control<Button>("TranslateButton").IsEnabled&&DisplayedText("TranslationOutputText").Contains("健谈"),"paragraph translation");
            ClickLink(Link(Control<TextBlock>("TranslationSourceText"),"reader"));await ReadyAsync("reader");
            Click("BackButton");await WaitAsync(()=>Control<FrameworkElement>("TranslationView").Visibility==Visibility.Visible,"return to paragraph");
            Check(Control<TextBox>("TranslationInput").Text=="A talkative reader enjoys learning."&&DisplayedText("TranslationOutputText").Contains("健谈"),"Paragraph snapshot was lost");
            Control<ComboBox>("TranslationDirectionCombo").SelectedIndex=1;Control<TextBox>("TranslationInput").Text="一位健谈的读者享受学习。";Click("TranslateButton");
            await WaitAsync(()=>Control<Button>("TranslateButton").IsEnabled&&DisplayedText("TranslationOutputText").Contains("reader"),"reverse paragraph translation");
            ClickLink(Link(Control<TextBlock>("TranslationOutputText"),"reader"));await ReadyAsync("reader");
            Click("BackButton");await WaitAsync(()=>Control<FrameworkElement>("TranslationView").Visibility==Visibility.Visible,"return to translated English");
            Check(Control<ComboBox>("TranslationDirectionCombo").SelectedIndex==1&&DisplayedText("TranslationOutputText").Contains("reader"),"Translated paragraph destination lost");
            Pass("Both original and translated paragraph words navigate and Back restores the complete translation view");
            await _window.ProcessClipboardTextAsync("talkative");await ReadyAsync("talkative");
            await _window.ProcessClipboardTextAsync("A talkative reader enjoys learning.");
            Check(Control<FrameworkElement>("TranslationView").Visibility==Visibility.Visible&&DisplayedText("TranslationOutputText").Contains("健谈"),"Clipboard paragraph did not translate");
            await _window.ProcessClipboardTextAsync("一位健谈的读者享受学习。");
            Check(Control<ComboBox>("TranslationDirectionCombo").SelectedIndex==1&&DisplayedText("TranslationOutputText").Contains("reader"),"Clipboard Chinese direction failed");
            await _window.ProcessClipboardTextAsync("快乐");
            Check(Control<FrameworkElement>("ChineseSearchView").Visibility==Visibility.Visible&&Control<StackPanel>("ChineseResultsPanel").Children.Count>0,"Clipboard Chinese word did not reverse search");
            Pass("Clipboard content routes single words, English and Chinese paragraphs and Chinese word queries without desktop input");

            prefs.Shortcuts["search"]="Ctrl+Shift+F";prefs.Save();
            Check(ShortcutGestures.Match(Preferences.Load(Path.Combine(_root,"preferences.json")),System.Windows.Input.Key.F,System.Windows.Input.ModifierKeys.Control|System.Windows.Input.ModifierKeys.Shift)=="search","Custom shortcut did not persist or match");
            var duplicates=new Dictionary<string,string>(prefs.Shortcuts){["history"]="Ctrl+Shift+F"};
            Check(!ShortcutGestures.Normalize(duplicates,out _,out var duplicateError)&&duplicateError!.Contains("重复"),"Duplicate shortcuts accepted");
            Pass("Custom shortcuts persist and route by their new gesture while duplicates are rejected");

            Control<TextBox>("SearchBox").Text="快乐";Click("SearchButton");
            await WaitAsync(()=>Control<FrameworkElement>("ChineseSearchView").Visibility==Visibility.Visible&&Control<StackPanel>("ChineseResultsPanel").Children.Count==40,"Chinese reverse search");
            Check(Link(Control<StackPanel>("ChineseResultsPanel"),"happy")!=null,"Chinese result meaning missing");
            Click("MoreChineseResultsButton");await WaitAsync(()=>Control<StackPanel>("ChineseResultsPanel").Children.Count==80,"more Chinese matches");
            Check(Link(Control<StackPanel>("ChineseResultsPanel"),"cheerful")!=null,"Chinese result synonyms missing after complete matches");
            Layout();Control<ScrollViewer>("ChineseSearchScroll").ScrollToVerticalOffset(430);await DrainAsync();var chineseOffset=Control<ScrollViewer>("ChineseSearchScroll").VerticalOffset;
            ClickLink(Link(Control<StackPanel>("ChineseResultsPanel"),"happy"));await ReadyAsync("happy");
            Click("BackButton");await WaitAsync(()=>Control<FrameworkElement>("ChineseSearchView").Visibility==Visibility.Visible&&Control<StackPanel>("ChineseResultsPanel").Children.Count==80,"back to Chinese matches");
            Check(Control<TextBox>("SearchBox").Text=="快乐"&&Math.Abs(Control<ScrollViewer>("ChineseSearchScroll").VerticalOffset-chineseOffset)<2,"Chinese query, pagination or scroll snapshot was lost");
            Click("ForwardButton");await ReadyAsync("happy");
            Pass("Chinese results include synonyms and restore the query, loaded results and position in both directions");

            Control<TextBox>("SearchBox").Text="管理";Control<TextBox>("SearchBox").Text="reader";await Task.Delay(320);await DrainAsync();
            Check(Control<ListBox>("SuggestionList").Items.Cast<object>().All(item=>item is string value&&value.StartsWith("reader",StringComparison.OrdinalIgnoreCase)),"A stale Chinese suggestion overwrote the English list");
            Control<TextBox>("SearchBox").Text="健谈";
            await WaitAsync(()=>Control<ListBox>("SuggestionList").Items.Cast<object>().OfType<ChineseSearchResult>().Any(item=>item.Word=="talkative"),"Chinese live suggestions");
            Control<ListBox>("SuggestionList").SelectedItem=Control<ListBox>("SuggestionList").Items.Cast<object>().OfType<ChineseSearchResult>().First(item=>item.Word=="talkative");await ReadyAsync("talkative");
            Check(prefs.SearchHistory.Any(item=>item.Word=="快乐"),"Chinese search was not saved in local history");
            Pass("Chinese suggestions show English alternatives, cancel stale input and share the search history");
            Control<TextBox>("SearchBox").Text="生产的";
            await WaitAsync(()=>Control<ListBox>("SuggestionList").Items.Cast<object>().OfType<ChineseSearchResult>().Any(item=>item.Word=="generative"),"exact Chinese suggestions");
            var productionSuggestions=Control<ListBox>("SuggestionList").Items.Cast<ChineseSearchResult>().ToArray();
            Check(productionSuggestions.Take(2).Any(item=>item.Word=="generative"&&item.IsExactMatch),"Complete-match word is not near the top of suggestions");
            Click("SearchButton");await WaitAsync(()=>Control<StackPanel>("ChineseResultsPanel").Children.Count>0&&Control<TextBlock>("ChineseQueryTitle").Text.Contains("生产的"),"exact Chinese result page");
            var productionCards=Control<StackPanel>("ChineseResultsPanel").Children.Cast<Border>().ToArray();
            Check(productionCards.Take(2).Any(card=>Blocks(card).SelectMany(block=>block.Inlines.OfType<Hyperlink>()).Any(link=>Equals(link.ToolTip,"查询 generative"))),"Complete match is not near the top of the result page");
            Check(Blocks(productionCards[0]).Last().Text=="完全匹配","Exact result label is missing");
            Pass("Chinese suggestions and result cards put complete production meanings ahead of partial matches");
            Layout();await DrainAsync();
            Check(productionCards.Take(6).All(card=>card.ActualHeight<=52),"Chinese result rows are still oversized");
            Pass("Chinese result rows retain meanings and match labels while fitting more than twice as densely");

            privateHandler.NoNotesFor.Add("localonly");mnemonics.Save("localonly",[new("local:test","词根词缀","本地独有 reader 助记")]);await SearchAsync("localonly");
            Check(Blocks(Control<StackPanel>("NotesPanel")).Any(block=>string.Concat(block.Inlines.Select(InlineText)).Contains("本地独有")),"Local fallback disappeared when cloud was empty");
            var mnemonicEditor=_window.CreateMnemonicEditor(mnemonics.Get("localonly").Single())!;
            Render(mnemonicEditor,650,510,Path.Combine(_previews,"13-mnemonic-editor.png"));
            ((TextBox)mnemonicEditor.FindName("MnemonicText")).Text="本地编辑后仍可查看 reader";Click((Button)mnemonicEditor.FindName("SaveMnemonicLocal"));
            await WaitAsync(()=>mnemonics.Get("localonly").Single().Text.Contains("本地编辑后"),"local mnemonic edit");Check(privateHandler.NoteWrites==0,"Local save wrote to cloud");
            var syncEditor=_window.CreateMnemonicEditor(mnemonics.Get("localonly").Single())!;
            ((TextBox)syncEditor.FindName("MnemonicText")).Text="同步后的 reader 助记";Click((Button)syncEditor.FindName("SaveMnemonicCloud"));
            await WaitAsync(()=>privateHandler.NoteWrites==1&&mnemonics.Get("localonly").Single().Origin=="cloud","explicit mnemonic sync");
            var reloadedNotes=new MnemonicStore(Path.Combine(_root,"mnemonics.user.json"),Path.Combine(_root,"seed-mnemonics.json"));await reloadedNotes.LoadAsync();
            Check(reloadedNotes.Get("localonly").Single().Text=="同步后的 reader 助记","Editor save was not durable");
            Pass("The mnemonic editor saves local changes without HTTP and explicitly syncs to the fake cloud");

            prefs.ClearHistory();
            foreach (var previewWord in new[] {"novel","learning","reader"}) prefs.RecordLookup(previewWord);
            await SearchAsync("talkative");
            Control<ToggleButton>("SimilarToggle").IsChecked=false;Control<ToggleButton>("SynonymsToggle").IsChecked=false;Control<ToggleButton>("DerivativesToggle").IsChecked=false;Control<ToggleButton>("PhrasesToggle").IsChecked=false;
            Render(_window,1160,1200,Path.Combine(_previews,"01-combined.png"));
            Render(_window,1000,680,Path.Combine(_previews,"02-compact.png"));
            Render(_window,760,520,Path.Combine(_previews,"03-minimum.png"));
            var startup=new FakeStartupRegistration();
            var settings=SettingsDialog.Build(null!,tokens,prefs,out _,startupRegistration:startup);
            Check(settings.FindName("RememberCloseAction") is CheckBox {IsChecked:false},"Settings cannot re-enable the close prompt");
            Check(settings.FindName("StartWithWindows") is CheckBox {IsChecked:false},"Startup should be optional and default off");
            ((CheckBox)settings.FindName("StartWithWindows")).IsChecked=true;
            settings.Close();Check(startup.Writes==0,"Canceling settings changed startup registration");
            settings=SettingsDialog.Build(null!,tokens,prefs,out _,startupRegistration:startup);
            var settingsNav=(ListBox)settings.FindName("SettingsNav");
            Check(settingsNav.Items.Cast<string>().SequenceEqual(new[]{"常规","快捷键","墨墨账号","反馈","赞助与关于"}),"Settings pages are not separated by purpose");
            var startupCheck=(CheckBox)settings.FindName("StartWithWindows");startupCheck.IsChecked=true;
            Render(settings,840,740,Path.Combine(_previews,"05-settings.png"));
            settingsNav.SelectedIndex=1;Render(settings,840,740,Path.Combine(_previews,"18-settings-shortcuts.png"));
            settingsNav.SelectedIndex=2;Render(settings,840,740,Path.Combine(_previews,"19-settings-account.png"));
            settingsNav.SelectedIndex=0;Check(startupCheck.IsChecked==true&&startup.Writes==0,"Switching settings pages loses or commits draft values");
            settingsNav.SelectedIndex=3;
            Check(Equals(((Button)settings.FindName("FeedbackEmailButton")).Tag,"1623255609@qq.com")&&Equals(((Button)settings.FindName("CopyFeedbackQq")).Tag,"1132371819"),"Feedback contact details do not match the provided email and group");
            Check(Equals(((Button)settings.FindName("FeedbackIssueButton")).Tag,SettingsDialog.RepositoryUrl+"/issues")&&SettingsDialog.IssueUrl=="https://github.com/SuperRaine/MemoLex/issues/new/choose","Feedback issue navigation points to the wrong repository");
            Render(settings,840,740,Path.Combine(_previews,"24-settings-feedback.png"));
            Pass("The feedback page exposes the supplied email, QQ group and new GitHub issue destination without sending messages");
            settingsNav.SelectedIndex=4;
            var paymentTabs=(TabControl)settings.FindName("SponsorPaymentTabs");
            Check(paymentTabs.Items.Cast<TabItem>().Select(item=>item.Header).SequenceEqual(new[]{"微信支付","支付宝"}),"Both sponsor methods must be available");
            foreach(var name in new[]{"WechatPaymentImage","AlipayPaymentImage"})
                Check(((BitmapSource)((Image)settings.FindName(name)).Source).PixelWidth>=1000,"Sponsor code has been reduced or replaced");
            Render(settings,840,740,Path.Combine(_previews,"20-settings-wechat.png"));paymentTabs.SelectedIndex=1;
            Render(settings,840,740,Path.Combine(_previews,"21-settings-alipay.png"));
            Render(settings,680,520,Path.Combine(_previews,"22-settings-compact.png"));
            var paymentDialog=SettingsDialog.BuildPaymentDialog(null!,"微信支付","sponsor-wechat.jpg");
            Render(paymentDialog,640,880,Path.Combine(_previews,"23-payment-enlarged.png"));paymentDialog.Close();settings.Close();
            Pass("Settings pages retain unsaved values, keep saving fixed and offer both original sponsor codes with enlargement");
            var startupKey=@"Software\MemoLex.Tests\"+Guid.NewGuid().ToString("N");
            try
            {
                var registration=new StartupRegistration(typeof(MainWindow).Assembly.Location,startupKey);
                Check(!registration.Enabled,"Fresh isolated registration starts enabled");registration.SetEnabled(true);
                using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(startupKey))
                    Check(Equals(key?.GetValue(StartupRegistration.ValueName),'"'+typeof(MainWindow).Assembly.Location+'"'),"Startup command does not quote the absolute application path");
                Check(registration.Enabled,"Startup registration was not durable");registration.SetEnabled(false);Check(!registration.Enabled,"Startup registration did not disable");
            }
            finally{Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(startupKey,false);}
            Pass("Startup is opt-in, cancel has no side effects and registration toggles in an isolated registry key");
            var failingStartup=new FakeStartupRegistration {Fail=true};
            var failedSettings=SettingsDialog.Build(null!,tokens,prefs,out _,startupRegistration:failingStartup);
            ((CheckBox)failedSettings.FindName("StartWithWindows")).IsChecked=true;Click((Button)failedSettings.FindName("SaveSettings"));
            Check(((TextBlock)failedSettings.FindName("SettingsStatus")).Text.Contains("自启动设置失败"),"Startup failure is hidden from the user");failedSettings.Close();
            Pass("Startup permission failures keep settings open with a specific error");
            CloseChoice? closeChoice=null;
            var closeDialog=CloseBehaviorDialog.Build(null!,prefs,value=>closeChoice=value);
            Render(closeDialog,520,330,Path.Combine(_previews,"17-close-choice.png"));
            ((CheckBox)closeDialog.FindName("RememberCloseChoice")).IsChecked=true;Click((Button)closeDialog.FindName("TrayChoice"));
            Check(closeChoice==new CloseChoice(true,true),"Tray button lost the remembered choice");
            closeDialog=CloseBehaviorDialog.Build(null!,prefs,value=>closeChoice=value);Click((Button)closeDialog.FindName("ExitApplicationChoice"));
            Check(closeChoice==new CloseChoice(false,false),"Exit button lost the one-time choice");
            closeChoice=null;closeDialog=CloseBehaviorDialog.Build(null!,prefs,value=>closeChoice=value);Click((Button)closeDialog.FindName("CancelCloseChoice"));
            Check(closeChoice==null,"Cancel selected a close action");
            Pass("The close dialog has exit, tray, cancel and remember-choice controls");
            var historyWindow=new HistoryWindow(null!,prefs);
            Render(historyWindow,660,600,Path.Combine(_previews,"06-history.png")); historyWindow.Close();
            var notebookHandler=new PrivateHandler();
            using var previewApi=new MaiMemoClient(tokens,notebookHandler,TimeSpan.Zero);
            var clickedNotebookWord="";CloudNotepad? savedNotebook=null;
            var notepadEditor=NotepadEditor.Build(null!,await previewApi.GetNotepadAsync("pad-test"),previewApi,pad=>savedNotebook=pad,localDictionary,word=>{clickedNotebookWord=word;return Task.CompletedTask;});
            var notebookTabs=(TabControl)notepadEditor.FindName("NotepadTabs");
            var notebookList=(ListBox)notepadEditor.FindName("NotepadWords");
            var notebookFilter=(TextBox)notepadEditor.FindName("NotepadFilter");
            var entryInput=(TextBox)notepadEditor.FindName("NotepadEntryInput");var entryPanel=(Expander)notepadEditor.FindName("NotepadEntryPanel");
            string NotebookDraft()=>entryInput.Text;
            void AppendWords(string words)=>entryInput.Text=entryInput.Text.TrimEnd('\r','\n')+"\n"+words;

            var wordPage=(DependencyObject)((TabItem)notebookTabs.Items[0]).Content;var infoPage=(DependencyObject)((TabItem)notebookTabs.Items[1]).Content;
            Check(notebookTabs.Items.Cast<TabItem>().Select(tab=>tab.Header).SequenceEqual(new[]{"单词与释义","编辑词本"}),"Notebook tabs do not match their intended roles");
            foreach(var name in new[]{"NotepadEntryInput","NotepadWords","NotepadDeleteSelected"})
                Check(LogicalNodes(wordPage).Contains((DependencyObject)notepadEditor.FindName(name))&&!LogicalNodes(infoPage).Contains((DependencyObject)notepadEditor.FindName(name)),name+" is not contained exclusively in the word page");
            Check(LogicalNodes(infoPage).Contains((DependencyObject)notepadEditor.FindName("NotepadTitle"))&&!LogicalNodes(wordPage).Contains((DependencyObject)notepadEditor.FindName("NotepadTitle")),"Metadata editing remains mixed with word editing");
            Pass("The word page contains all content and group editing while Edit Notepad contains only metadata");
            await WaitAsync(()=>notebookList.Items.Count>0,"notebook translations");
            Check(LogicalNodes(wordPage).OfType<Expander>().Count()==1,"Word editing still has multiple expandable entries");
            Check(!LogicalNodes(wordPage).OfType<ComboBox>().Any(),"Duplicate add/edit modes remain in the word page");
            Check(entryInput.Text=="# 我的单词\ntalkative","Unified editor does not start with the complete word list");
            Pass("One complete editor adds, removes and groups words without a mode selector");

            Check(notebookList.Items.Cast<NotepadDisplayRow>().Any(row=>row.Word=="talkative"&&row.Meaning.Contains("健谈")),"Notepad translation absent");
            notebookFilter.Text="健谈";Check(notebookList.Items.Count==1,"Chinese notepad filter failed");notebookFilter.Clear();
            AppendWords("reader, learning, READER");Click((Button)notepadEditor.FindName("NotepadEntryAction"));
            await WaitAsync(()=>notebookList.Items.Count==4,"batch add preview");Check(notebookHandler.Writes==0,"Draft word changes were sent before saving");
            Render(notepadEditor,800,730,Path.Combine(_previews,"09-notepad-translations.png"));
            Check(!VisualNodes(notebookList).OfType<CheckBox>().Any(),"Selection checkboxes remain in word rows");
            var selectable=notebookList.Items.Cast<NotepadDisplayRow>().Where(row=>!row.IsChapter).ToArray();
            NativeSelect(notebookList,selectable[0],ModifierKeys.None);NativeSelect(notebookList,selectable[2],ModifierKeys.Control);
            Check(notebookList.SelectedItems.Count==2&&!notebookList.SelectedItems.Contains(selectable[1]),"Ctrl selection did not keep nonadjacent words");
            NativeSelect(notebookList,selectable[0],ModifierKeys.Control);Check(notebookList.SelectedItems.Count==1,"Ctrl selection did not toggle a word off");
            NativeSelect(notebookList,selectable[0],ModifierKeys.None);NativeSelect(notebookList,selectable[2],ModifierKeys.Shift);
            Check(notebookList.SelectedItems.Count==3,"Shift did not select the continuous range");
            NativeSelect(notebookList,selectable[1],ModifierKeys.Control);
            Check(notebookList.SelectedItems.Count==2&&clickedNotebookWord=="","Selecting words unexpectedly looked one up");
            using(var keySource=new HwndSource(new HwndSourceParameters("MemoLex isolated key-event source"){WindowStyle=unchecked((int)0x80000000),Width=1,Height=1,PositionX=-32000,PositionY=-32000}))
                WithModifiers(ModifierKeys.Control,()=>notebookList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,keySource,Environment.TickCount,Key.Delete){RoutedEvent=Keyboard.PreviewKeyDownEvent}));
            await WaitAsync(()=>notebookList.Items.Count==2,"Delete key on nonadjacent selected words");
            Check(NotebookDraft()=="# 我的单词\nreader\n","Delete did not respect the selected words");
            Click((Button)notepadEditor.FindName("NotepadUndo"));await WaitAsync(()=>notebookList.Items.Count==2&&notebookList.Items.Cast<NotepadDisplayRow>().Any(row=>row.Word=="talkative"),"undo selection edit");
            AppendWords("reader,learning");Click((Button)notepadEditor.FindName("NotepadEntryAction"));await WaitAsync(()=>notebookList.Items.Count==4,"restore selection test words");
            Pass("Native Ctrl and Shift row selection works without checkboxes and Delete removes only selected words");
            notebookFilter.Text="供界面";Click((Button)notepadEditor.FindName("NotepadSelectAll"));
            Check(notebookList.SelectedItems.Count==2,"Select filtered results included a chapter or missed a word");
            Click((Button)notepadEditor.FindName("NotepadDeleteSelected"));notebookFilter.Clear();
            await WaitAsync(()=>notebookList.Items.Count==2,"batch delete preview");
            Check(NotebookDraft()=="# 我的单词\ntalkative\n","Batch deletion changed the chapter or retained a deleted word");
            AppendWords("reader, learning");Click((Button)notepadEditor.FindName("NotepadEntryAction"));
            await WaitAsync(()=>notebookList.Items.Count==4,"second batch add");
            var readerRow=notebookList.Items.Cast<NotepadDisplayRow>().First(row=>row.Word=="reader");
            notebookList.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,new Button {DataContext=readerRow,Tag="remove"}));
            await WaitAsync(()=>notebookList.Items.Count==3,"single row deletion");
            entryInput.Text="# 重新分组\ntalkative\nlearning\nnovel";
            await WaitAsync(()=>notebookList.Items.Count==4&&notebookList.Items.Cast<NotepadDisplayRow>().First().Word=="重新分组","integrated full content edit");
            notebookList.SelectedItems.Add(notebookList.Items.Cast<NotepadDisplayRow>().First(row=>row.Word=="novel"));Click((Button)notepadEditor.FindName("NotepadDeleteSelected"));
            await WaitAsync(()=>notebookList.Items.Count==3,"delete a word after full content edit");
            notebookTabs.SelectedIndex=1;((TextBox)notepadEditor.FindName("NotepadTitle")).Text="信息编辑后的词本";((TextBox)notepadEditor.FindName("NotepadBrief")).Text="词本信息独立编辑";
            Click((Button)notepadEditor.FindName("NotepadSave"));
            await WaitAsync(()=>savedNotebook!=null&&!((Button)notepadEditor.FindName("NotepadSave")).IsEnabled,"save merged notepad");
            Check(notebookHandler.Writes==1&&savedNotebook!.Title=="信息编辑后的词本"&&savedNotebook.Content.StartsWith("# 重新分组")&&savedNotebook.Content.Contains("learning")&&!savedNotebook.Words.Contains("reader"),"Separated edits did not save correctly");
            Pass("Word edits and independent metadata edits retain one consistent cloud snapshot");
            Render(notepadEditor,800,730,Path.Combine(_previews,"09-notepad-editor.png"));notebookTabs.SelectedIndex=0;
            Render(notepadEditor,800,730,Path.Combine(_previews,"09-notepad-translations.png"));
            entryPanel.IsExpanded=true;
            Render(notepadEditor,800,730,Path.Combine(_previews,"15-unified-word-editor.png"));
            Render(notepadEditor,620,520,Path.Combine(_previews,"14-notepad-compact.png"));await DrainAsync();
            Render(notepadEditor,620,520,Path.Combine(_previews,"14-notepad-compact.png"));
            Check(notebookList.ActualHeight>=100,"Expanded editing fields hide the word list in a short window");
            entryPanel.IsExpanded=false;
            var notebookWord=notebookList.Items.Cast<NotepadDisplayRow>().First(row=>!row.IsChapter);
            var doubleClickedRow=(ListBoxItem)notebookList.ItemContainerGenerator.ContainerFromItem(notebookWord);
            WithModifiers(ModifierKeys.None,()=>notebookList.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=System.Windows.Controls.Control.MouseDoubleClickEvent,Source=doubleClickedRow}));await DrainAsync();
            Check(clickedNotebookWord=="talkative","Notepad word did not navigate");
            Pass("Cloud word lists show Chinese meanings, preserve chapters, filter and keep point-word navigation after saving");
            var deletionHandler=new PrivateHandler();using var deletionApi=new MaiMemoClient(tokens,deletionHandler,TimeSpan.Zero);
            var allowed=false;var confirmations=0;var removedBook="";
            var deletionEditor=NotepadEditor.Build(null!,await deletionApi.GetNotepadAsync("pad-test"),deletionApi,_=>{},localDictionary,deleted:id=>removedBook=id,confirmDeletion:(_,_,count)=>{confirmations++;Check(count==1,"Confirmation does not describe the whole book");return allowed;});
            var deletionList=(ListBox)deletionEditor.FindName("NotepadWords");await WaitAsync(()=>deletionList.Items.Count==2,"deletion fixture");
            Click((Button)deletionEditor.FindName("NotepadSelectAll"));Click((Button)deletionEditor.FindName("NotepadDeleteSelected"));await DrainAsync();
            Check(confirmations==1&&deletionHandler.Deletes==0&&deletionHandler.Writes==0&&deletionList.Items.Count==2,"Canceling all-word deletion changed the book");
            allowed=true;Click((Button)deletionEditor.FindName("NotepadDeleteSelected"));await WaitAsync(()=>removedBook=="pad-test","confirmed whole-book deletion");
            Check(deletionHandler.Deletes==1&&deletionHandler.Writes==0&&confirmations==2,"All-word deletion saved an empty list instead of deleting the book");
            Pass("Deleting the final selected word confirms whole-book deletion and Cancel preserves the entire book");
            var emptyHandler=new PrivateHandler();using var emptyApi=new MaiMemoClient(tokens,emptyHandler,TimeSpan.Zero);var emptyPrompts=0;
            var emptyEditor=NotepadEditor.Build(null!,await emptyApi.GetNotepadAsync("pad-test"),emptyApi,_=>{},localDictionary,confirmDeletion:(_,_,_)=>{emptyPrompts++;return false;});
            ((TextBox)emptyEditor.FindName("NotepadEntryInput")).Clear();
            Click((Button)emptyEditor.FindName("NotepadSave"));await WaitAsync(()=>((TextBox)emptyEditor.FindName("NotepadEntryInput")).Text.Contains("talkative"),"restore nonempty draft after canceled clear");
            Check(emptyPrompts==1&&emptyHandler.Writes==0&&emptyHandler.Deletes==0,"Empty raw edit bypassed deletion confirmation");emptyEditor.Close();
            Pass("Clearing the complete editor requires confirmation and restores a nonempty draft on Cancel");
            var confirmation=NotepadDeletionDialog.Build(null!,await emptyApi.GetNotepadAsync("pad-test"),1);
            Check(((Button)confirmation.FindName("CancelNotepadDeletion")).IsDefault&&!((Button)confirmation.FindName("ConfirmNotepadDeletion")).IsDefault,"Deletion confirmation defaults to the destructive action");
            Render(confirmation,510,320,Path.Combine(_previews,"16-delete-confirmation.png"));confirmation.Close();
            Pass("Whole-book deletion uses a separate warning with Cancel as the default");
            await SearchAsync("talkative");privateHandler.Content="# 我的单词\ntalkative";var beforeDeleteWrites=privateHandler.Writes;
            _window.ConfirmNotepadDeletion=(_,_,_)=>false;Click("AddButton");await WaitAsync(()=>Control<Button>("AddButton").IsEnabled,"cancel last-word toggle");
            Check(privateHandler.Deletes==0&&privateHandler.Writes==beforeDeleteWrites,"The main add/remove button bypassed final-word protection");
            _window.ConfirmNotepadDeletion=(_,_,_)=>true;Click("AddButton");await WaitAsync(()=>Control<ComboBox>("NotepadCombo").Items.Count==0,"deleted book removed from selector");
            Check(privateHandler.Deletes==1&&Control<Button>("AddButton").Content.ToString()!.Contains("新建")&&Control<TextBlock>("StatusText").Text=="云词本已删除。","A successful deletion left stale state or an unconfirmed-result error");
            Pass("The main word button protects the final word and removes a confirmed deleted book from selection");
            Pass("Settings and history use the same themed custom title bars and render off screen");
            // A nonactivating window outside the desktop lets WPF realize text
            // editing visuals, which intentionally skip drawing in hidden trees.
            _window.WindowStartupLocation=WindowStartupLocation.Manual;
            _window.Left=-32000; _window.Top=-32000; _window.ShowInTaskbar=false; _window.ShowActivated=false;
            _window.Show(); await DrainAsync();
            await VerifyNativeResizeAsync();
            await ScrollToAsync(0);
            Control<TextBox>("SearchBox").Select(0,0);
            var editor=Control<TextBox>("SearchBox");
            var character=editor.GetRectFromCharacterIndex(0);
            Check(editor.IsVisible && editor.Text=="talkative" && !character.IsEmpty && character.Bottom<=editor.ActualHeight-editor.Padding.Bottom, "The search editor text is clipped by its template");
            Pass("The realized search editor displays its text inside the padded content area");
            var visibleRoot=(FrameworkElement)_window.Content;
            visibleRoot.UpdateLayout();
            var realized=new RenderTargetBitmap((int)visibleRoot.ActualWidth,(int)visibleRoot.ActualHeight,96,96,PixelFormats.Pbgra32);
            realized.Render(visibleRoot);
            var realizedEncoder=new PngBitmapEncoder(); realizedEncoder.Frames.Add(BitmapFrame.Create(realized));
            using(var realizedOutput=File.Create(Path.Combine(_previews,"07-realized-view.png"))) realizedEncoder.Save(realizedOutput);
            Control<ToggleButton>("SynonymsToggle").IsChecked=true;Control<ToggleButton>("DerivativesToggle").IsChecked=true;
            await ScrollToAsync(SectionOffset("SynonymsToggle")-10);
            visibleRoot.UpdateLayout();
            var relatedShot=new RenderTargetBitmap((int)visibleRoot.ActualWidth,(int)visibleRoot.ActualHeight,96,96,PixelFormats.Pbgra32);relatedShot.Render(visibleRoot);
            var relatedEncoder=new PngBitmapEncoder();relatedEncoder.Frames.Add(BitmapFrame.Create(relatedShot));
            using(var relatedOutput=File.Create(Path.Combine(_previews,"10-related-words.png")))relatedEncoder.Save(relatedOutput);
            Control<ToggleButton>("SynonymsToggle").IsChecked=false;Control<ToggleButton>("DerivativesToggle").IsChecked=false;Control<ToggleButton>("PhrasesToggle").IsChecked=true;
            await ScrollToAsync(SectionOffset("PhrasesToggle")-10);visibleRoot.UpdateLayout();
            var phrasesShot=new RenderTargetBitmap((int)visibleRoot.ActualWidth,(int)visibleRoot.ActualHeight,96,96,PixelFormats.Pbgra32);phrasesShot.Render(visibleRoot);
            var phrasesEncoder=new PngBitmapEncoder();phrasesEncoder.Frames.Add(BitmapFrame.Create(phrasesShot));
            using(var phrasesOutput=File.Create(Path.Combine(_previews,"18-related-phrases.png")))phrasesEncoder.Save(phrasesOutput);
            Click("TranslationModeButton");Control<TextBox>("TranslationInput").Text="A talkative reader enjoys learning.";Control<ComboBox>("TranslationDirectionCombo").SelectedIndex=0;Click("TranslateButton");
            await WaitAsync(()=>Control<Button>("TranslateButton").IsEnabled&&DisplayedText("TranslationOutputText").Contains("健谈"),"paragraph preview");
            visibleRoot.UpdateLayout();
            var paragraphShot=new RenderTargetBitmap((int)visibleRoot.ActualWidth,(int)visibleRoot.ActualHeight,96,96,PixelFormats.Pbgra32);paragraphShot.Render(visibleRoot);
            var paragraphEncoder=new PngBitmapEncoder();paragraphEncoder.Frames.Add(BitmapFrame.Create(paragraphShot));
            using(var paragraphOutput=File.Create(Path.Combine(_previews,"11-paragraph.png")))paragraphEncoder.Save(paragraphOutput);
            Control<TextBox>("SearchBox").Text="聪明";Click("SearchButton");
            await WaitAsync(()=>Control<FrameworkElement>("ChineseSearchView").Visibility==Visibility.Visible&&Control<StackPanel>("ChineseResultsPanel").Children.Count>0,"Chinese preview");
            visibleRoot.UpdateLayout();
            var chineseShot=new RenderTargetBitmap((int)visibleRoot.ActualWidth,(int)visibleRoot.ActualHeight,96,96,PixelFormats.Pbgra32);chineseShot.Render(visibleRoot);
            var chineseEncoder=new PngBitmapEncoder();chineseEncoder.Frames.Add(BitmapFrame.Create(chineseShot));
            using(var chineseOutput=File.Create(Path.Combine(_previews,"12-chinese-search.png")))chineseEncoder.Save(chineseOutput);
            _window.Hide();
            if(_nativeExecutable != null)
            {
                VerifyNativeIcon(_nativeExecutable);
                Pass("The EXE contains the owl icon in its native Windows icon resources");
            }
            Console.WriteLine($"{_passed} WPF component checks passed. Rendering used a nonactivating off-screen window; no desktop input was sent. Preview: {_previews}");
        }
        finally
        {
            _window?.Close();
            _reverseDictionary?.Dispose();
            var resolved=Path.GetFullPath(_root);
            if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("MemoLex.UITests.")) Directory.Delete(resolved,true);
        }
    }
    private async Task VerifyNativeResizeAsync()
    {
        var handle=new WindowInteropHelper(_window).Handle;var pointer=Marshal.AllocHGlobal(40);
        try
        {
            Marshal.Copy(new int[10],0,pointer,10);SendMessage(handle,0x24,IntPtr.Zero,pointer);
            var limits=new int[10];Marshal.Copy(pointer,limits,0,10);var dpi=VisualTreeHelper.GetDpi(_window);
            Check(limits[6]==Math.Ceiling(760*dpi.DpiScaleX)&&limits[7]==Math.Ceiling(520*dpi.DpiScaleY),"Native min track size ignores WPF minimum or DPI");
            foreach(var size in new[]{new Size(760,520),new Size(900,600),new Size(1160,800)})
            {
                _window.Width=size.Width;_window.Height=size.Height;await DrainAsync();_window.UpdateLayout();
                GetClientRect(handle,out var client);var root=(FrameworkElement)_window.Content;
                Check(Math.Abs(root.ActualWidth*dpi.DpiScaleX-client.Right)<3&&Math.Abs(root.ActualHeight*dpi.DpiScaleY-client.Bottom)<3,"Native client bounds and WPF layout differ");
            }
            var body=Control<Grid>("BodyGrid");var bounds=Marshal.AllocHGlobal(16);
            var definition=Control<TextBlock>("DefinitionText");var previousInlines=definition.Inlines.ToArray();
            definition.Inlines.Clear();definition.Inlines.Add(new Run(string.Join(" ",Enumerable.Repeat("A talkative reader enjoys learning new words and reading longer paragraphs.",20))));
            var liveResizeTimer=Stopwatch.StartNew();double narrowTextHeight=0;
            try
            {
                GetWindowRect(handle,out var rect);Marshal.StructureToPtr(rect,bounds,false);
                SendMessage(handle,0x231,IntPtr.Zero,IntPtr.Zero);SendMessage(handle,0x214,(IntPtr)2,bounds);
                for(var index=0;index<60;index++)
                {
                    var size=new Size(760+index*7,520+index*4);
                    _window.Width=size.Width;_window.Height=size.Height;await DrainAsync();_window.UpdateLayout();
                    GetClientRect(handle,out var client);
                    Check(Math.Abs(body.ActualWidth*dpi.DpiScaleX-(client.Right-2*dpi.DpiScaleX))<3&&body.CacheMode==null&&body.RenderTransform.Value.IsIdentity,"Native resizing froze or scaled the view before the sizing loop ended");
                    Check(Control<TextBlock>("WordText").FontSize==(size.Width-2<950?32:42),"Native resizing postponed responsive updates");
                    Check(Math.Abs(Control<ListBox>("SuggestionList").ActualHeight-Control<ListBox>("HistoryList").ActualHeight)<=1,"List heights stopped splitting equally during native resize");
                    if(index==0)narrowTextHeight=definition.ActualHeight;
                }
                Check(definition.ActualHeight<narrowTextHeight,"Text wrapping waited until the sizing loop ended");
                Console.WriteLine($"Real native window: 60 live layout updates in {liveResizeTimer.ElapsedMilliseconds} ms including frame waits");
                Pass("The real window reflows text and responsive controls over 60 native sizes before resizing ends, with equal lists and no cached preview");
            }
            finally{SendMessage(handle,0x232,IntPtr.Zero,IntPtr.Zero);Marshal.FreeHGlobal(bounds);definition.Inlines.Clear();definition.Inlines.AddRange(previousInlines);}
            await DrainAsync();
            var region=CreateRectRgn(0,0,0,0);
            try
            {
                Check(System.Windows.Shell.WindowChrome.GetWindowChrome(_window).CornerRadius.TopLeft==RoundedWindow.Radius,"The native chrome has square corners");
                var regionKind=GetWindowRgn(handle,region);
                var dwmResult=DwmGetWindowAttribute(handle,33,out var preference,sizeof(int));
                Check(regionKind>1?!PtInRegion(region,0,0)&&PtInRegion(region,100,100):dwmResult==0&&preference==2,"The native window is not configured with rounded corners");
                Console.WriteLine($"Native rounded window: region={regionKind}, cornerIncluded={PtInRegion(region,0,0)}, DWM={dwmResult}, preference={preference}");
            }
            finally{DeleteObject(region);}
            Pass("The actual native window has rounded chrome and a rounded region or system corner preference");
            Check(ShortcutDefinitions.All.First(item=>item.Id=="search").Global&&MainWindow.GlobalShortcuts.Any(item=>item.Id==0x4D4D&&item.Command=="search"),"Quick search is not globally registered");
            _window.Hide();_window.WindowState=WindowState.Minimized;
            SendMessage(handle,0x312,(IntPtr)0x4D4D,IntPtr.Zero);await DrainAsync();
            Check(_window.IsVisible&&_window.WindowState==WindowState.Normal&&Control<TextBox>("SearchBox").SelectedText==Control<TextBox>("SearchBox").Text,"Global quick search did not restore the hidden minimized window and select the query");
            Pass("The global quick-search message restores a hidden minimized window and selects its search text without desktop input");
            using var copyMonitor=new DoubleCopyMonitor(_=>{});
            Pass("Native resize minimums follow DPI, client bounds match WPF and the passive copy monitor installs and disposes");
        }
        finally{Marshal.FreeHGlobal(pointer);}
    }
    [StructLayout(LayoutKind.Sequential)]private struct ClientRect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr window,out ClientRect rect);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out ClientRect rect);
    [DllImport("user32.dll")]private static extern int GetWindowRgn(IntPtr window,IntPtr region);
    [DllImport("gdi32.dll")]private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")]private static extern bool PtInRegion(IntPtr region,int x,int y);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr value);
    [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
    internal void VerifyNativeIcon(string path)
    {
        var handles=new IntPtr[1];
        Check(ExtractIconEx(Path.GetFullPath(path),0,handles,null,1)==1 && handles[0]!=IntPtr.Zero,"EXE icon extraction failed");
        try
        {
            var native=Imaging.CreateBitmapSourceFromHIcon(handles[0],Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
            var reference=new IconBitmapDecoder(new Uri("pack://application:,,,/MemoLex;component/app.ico"),BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames.First(frame=>frame.PixelWidth==native.PixelWidth && frame.PixelHeight==native.PixelHeight);
            byte[] Pixels(BitmapSource bitmap)
            {
                var converted=new FormatConvertedBitmap(bitmap,PixelFormats.Pbgra32,null,0);
                var output=new byte[converted.PixelWidth*converted.PixelHeight*4]; converted.CopyPixels(output,converted.PixelWidth*4,0); return output;
            }
            var actual=Pixels(native); var expected=Pixels(reference);
            var difference=actual.Zip(expected,(a,b)=>Math.Abs((int)a-b)).Average();
            Check(difference<2,$"Native icon differs from the owl asset (mean pixel delta {difference:F2})");
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(native));
            using var output=File.Create(Path.Combine(_previews,"08-exe-icon.png"));encoder.Save(output);
        }
        finally { DestroyIcon(handles[0]); }
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,EntryPoint="ExtractIconExW")] private static extern uint ExtractIconEx(string file,int index,IntPtr[]? large,IntPtr[]? small,uint count);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr handle);
    private static void CreateDictionary(string directory)
    {
        using var data=File.Create(Path.Combine(directory,"dictionary.data"));
        using var index=new StreamWriter(Path.Combine(directory,"dictionary.index.tsv"),false,new UTF8Encoding(false));
        foreach(var word in new[]{"talkative","reader","learning","talk","talking","talked","readers","novel","slow","fast","empty","retry","loquacious","talkativeness","form","from","happy","localonly","talk about","small talk","resume"})
        {
            var entry=new DictionaryEntry { word=word, phonetic="tɔːkətɪv", translation=word switch {"talkative"=>"adj. 健谈的，爱说话的；可用来形容 reader。","talk about"=>"谈论，讨论","small talk"=>"闲谈，寒暄","resume"=>"n. 简历；vt. 重新开始",_=>"n. 供界面测试使用的自编释义。"}, definition=word=="talkative"?"adj. Inclined to talk a great deal; fond of talking.":"A reader enjoys learning.",tag="cet4 cet6 ky",exchange=word=="talkative"?"0:talk/i:talking":"",collins="3",oxford="1" };
            var bytes=JsonSerializer.SerializeToUtf8Bytes(entry);
            index.WriteLine($"{word}\t{data.Position}\t{bytes.Length}"); data.Write(bytes);
        }
    }
}

internal sealed class ExampleHandler : HttpMessageHandler
{
    private bool _retryFailed;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        var query=Query(request.RequestUri!); var word=query["keyword"]; var paper=query["paper_type"]; var offset=int.Parse(query["offset"]);
        if(word=="slow") await Task.Delay(180,cancellationToken);
        if(word=="retry"&&!_retryFailed) { _retryFailed=true;return new HttpResponseMessage(HttpStatusCode.TooManyRequests); }
        var total=word=="empty"?0:20;
        var phrases=Enumerable.Range(offset,Math.Min(10,Math.Max(0,total-offset))).Select(i=>new { phrase_id=$"{word}-{paper}-{i}",phrase_en=$"A {word} reader enjoys learning.",phrase_zh="一位健谈的读者享受学习的过程。（自编测试句）",source=$"{paper} · 示例 {i+1}",highlight=new {context=new {phrase=$"A <span>{word}</span> reader enjoys learning."}} });
        return JsonResponse(new {success=true,data=new {total=new {value=total},phrases}});
    }
    internal static Dictionary<string,string> Query(Uri uri)=>uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(pair=>pair.Split('=',2)).ToDictionary(pair=>pair[0],pair=>Uri.UnescapeDataString(pair[1]));
    internal static HttpResponseMessage JsonResponse(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
}
internal sealed class PrivateHandler : HttpMessageHandler
{
    internal int Writes;
    internal int Deletes;private bool _deleted;
    internal int NoteWrites;
    internal TaskCompletionSource? NotesBarrier;
    internal HashSet<string> NoNotesFor=[];
    internal string NoteText="talk 讲话 + -ative 表形容词，有…倾向的，有…性质的 → talkative 爱讲话的。\n联想：a talkative reader（健谈的读者）。";
    internal string Content="# 我的单词\ntalkative";
    private object Pad()=>new{id="pad-test",title="我的生词本",brief="测试词本",content=Content,tags=new[]{"学习"},status=_deleted?"DELETED":"UNPUBLISHED",type="NOTEPAD"};
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        var path=request.RequestUri!.AbsolutePath;
        object data;
        if(request.Method==HttpMethod.Delete&&path.EndsWith("/notepads/pad-test")){Deletes++;var beforeDelete=Pad();_deleted=true;return ExampleHandler.JsonResponse(new {success=true,data=new {notepad=beforeDelete}});}
        if(request.Method==HttpMethod.Post&&path.EndsWith("/notepads/pad-test"))
        {
            Writes++;using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));Content=body.RootElement.GetProperty("notepad").GetProperty("content").GetString()!;
            return ExampleHandler.JsonResponse(new {success=true,data=new {notepad=new {id="pad-test",list=Array.Empty<object>()}}});
        }
        if(request.Method==HttpMethod.Post&&(path.EndsWith("/notes")||path.Contains("/notes/")))
        {
            NoteWrites++;using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));var note=body.RootElement.GetProperty("note");NoteText=note.GetProperty("note").GetString()!;
            if(note.TryGetProperty("voc_id",out var voc))NoNotesFor.Remove(voc.GetString()![4..]);
            return ExampleHandler.JsonResponse(new {success=true,data=new {note=new {id=path.EndsWith("/notes")?"note-new":path.Split('/')[^1],note_type=note.GetProperty("note_type").GetString(),note=NoteText,status="PUBLISHED"}}});
        }
        if(request.Method!=HttpMethod.Get)throw new Exception("Unexpected simulated write");
        if(path.EndsWith("/vocabulary"))
        {
            var word=ExampleHandler.Query(request.RequestUri)["spelling"];
            data=new {voc=new{id="voc-"+word,spelling=word}};
        }
        else if(path.EndsWith("/notes"))
        {
            if(NotesBarrier is { } barrier)await barrier.Task.WaitAsync(cancellationToken);
            var word=ExampleHandler.Query(request.RequestUri)["voc_id"][4..];
            data=NoNotesFor.Contains(word)?new {notes=Array.Empty<object>()}:(object)new {notes=new[]{new{id="note-1",note_type="词根词缀",note=NoteText,status="PUBLISHED"}}};
        }
        else if(path.EndsWith("/notepads")) data=new {notepads=new[]{Pad()}};
        else if(path.EndsWith("/notepads/pad-test"))data=new {notepad=Pad()};
        else throw new Exception("Unexpected fixture endpoint: "+path);
        return ExampleHandler.JsonResponse(new {success=true,data});
    }
}
internal sealed class ParagraphHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        if(request.Headers.Authorization!=null)throw new Exception("Translation received an account token");
        var pair=ExampleHandler.Query(request.RequestUri!)["langpair"];
        return Task.FromResult(ExampleHandler.JsonResponse(new {responseStatus=200,responseData=new {translatedText=pair=="zh-CN|en"?"A talkative reader enjoys learning.":"一位健谈的读者享受学习。"}}));
    }
}

internal sealed class FakeStartupRegistration : IStartupRegistration
{
    public bool Enabled {get;private set;}
    public int Writes {get;private set;}
    public bool Fail {get;init;}
    public void SetEnabled(bool enabled)
    {
        if(Fail)throw new UnauthorizedAccessException("Simulated registry permission failure");
        Enabled=enabled;Writes++;
    }
}

