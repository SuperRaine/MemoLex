using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex;
public partial class MainWindow
{
    private readonly MnemonicStore _mnemonics;
    private CancellationTokenSource? _notesCts;
    private int _notesVersion;
    private StoredMnemonic[] _displayedNotes=[];
    private string? MnemonicAccountKey=>_accountCredentialKey!=null&&_accountCredentialKey==_tokens.CredentialKey&&_tokens.ActiveAccount is {} account?account.Id+":"+_accountCredentialKey:null;
    private void RenderNotes(string word,IReadOnlyList<StoredMnemonic> notes,bool cloud=false)
    {
        if(word!=_word)return;
        _displayedNotes=notes.Select(note=>note with {NoteType=MnemonicTypes.Normalize(note.NoteType)}).ToArray();NotesPanel.Children.Clear();EditNotesButton.IsEnabled=ValidWord(word);
        foreach(var note in _displayedNotes)
        {
            var stack=new StackPanel();
            var analysis=MnemonicParts.Analyze(word,note.NoteType,note.Text,(form,meaning,current)=>_mnemonics.FindMorphemeExamples(form,meaning,current));
            var hasBreakdowns=analysis.Breakdowns.Count>0;

            var header=new Grid{Margin=new Thickness(0,0,0,hasBreakdowns?6:4)};
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var origin=note.Origin=="cloud"?(cloud?"云端助记":"云端助记（离线）"):"本地助记";
            header.Children.Add(new TextBlock
            {
                Text=hasBreakdowns?$"词根拆解 · {origin}":origin,
                FontSize=11,FontWeight=FontWeights.SemiBold,
                Foreground=Brush("#5B7A91"),VerticalAlignment=VerticalAlignment.Center
            });

            var rightHead=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            var typeTag=new Border
            {
                Background=Brush("#DDF2FF"),CornerRadius=new CornerRadius(9),
                Padding=new Thickness(7,1.5,7,1.5),Margin=new Thickness(0,0,5,0),
                VerticalAlignment=VerticalAlignment.Center,
                Child=new TextBlock{Text=note.NoteType,FontSize=10.5,FontWeight=FontWeights.SemiBold,Foreground=Brush("#0B84BD")}
            };
            rightHead.Children.Add(typeTag);
            var edit=new Button{Content="编辑",FontSize=11,Padding=new Thickness(6,2,6,2),Background=Brushes.Transparent,BorderThickness=new Thickness(0),Foreground=Brush("#5B7A91")};
            edit.Click+=(_,_)=>OpenMnemonicEditor(note);
            rightHead.Children.Add(edit);
            Grid.SetColumn(rightHead,1);header.Children.Add(rightHead);
            stack.Children.Add(header);

            for(var i=0;i<analysis.Breakdowns.Count;i++)
            {
                var builder=BuildMnemonicParts(word,analysis.Breakdowns[i]);
                if(i>0)builder.Margin=new Thickness(0,6,0,0);
                stack.Children.Add(builder);
            }

            if(!string.IsNullOrWhiteSpace(analysis.RemainingText))
            {
                var text=BuildSentenceText(analysis.RemainingText,EmptyMatches,word,hasBreakdowns?13:14.5,accentLinks:true);
                text.LineHeight=hasBreakdowns?21:24;
                text.FontFamily=FontFamily;
                if(hasBreakdowns)
                {
                    var remBorder=new Border
                    {
                        Margin=new Thickness(0,7,0,0),Padding=new Thickness(0,6,0,0),
                        BorderThickness=new Thickness(0,1,0,0),
                        Child=text
                    };
                    remBorder.SetResourceReference(Border.BorderBrushProperty,"RuleBrush");
                    stack.Children.Add(remBorder);
                }
                else
                {
                    text.Margin=new Thickness(0,2,0,0);
                    stack.Children.Add(text);
                }
            }
            var cardBorder=new Border
            {
                Child=stack,Padding=new Thickness(13,10,13,10),Margin=new Thickness(0,6,0,0),
                BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(11)
            };
            cardBorder.SetResourceReference(Border.BackgroundProperty,"CardSoftBrush");
            cardBorder.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
            NotesPanel.Children.Add(cardBorder);
        }
        if(_displayedNotes.Length>0)Desktop.UiMotion.Reveal(NotesPanel,distance:4);
    }
    private FrameworkElement BuildMnemonicParts(string currentWord,MnemonicBreakdown breakdown)
    {
        static DropShadowEffect LitShadow(double opacity)=>new()
        {
            BlurRadius=14,ShadowDepth=4,Direction=270,Opacity=opacity,Color=Color.FromRgb(18,134,196)
        };
        var chipStyle=Application.Current?.TryFindResource("MorphemeChipButton") as Style;
        var pillStyle=Application.Current?.TryFindResource("PillButton") as Style;
        var wrapper=new StackPanel();
        var builder=new MorphemeWrapPanel{HorizontalSpacing=7,VerticalSpacing=8,ExpandLastChild=true,LastChildMinWidth=190};
        var detail=new TextBlock
        {
            Text=breakdown.Explanation,FontSize=12.5,LineHeight=19,
            TextWrapping=TextWrapping.Wrap
        };
        detail.SetResourceReference(TextBlock.ForegroundProperty,"AccentDeepBrush");

        var cognatesRow=new WrapPanel{Margin=new Thickness(0,7,0,0),Visibility=Visibility.Collapsed};
        var familyDrawer=new Border
        {
            Margin=new Thickness(0,8,0,0),Padding=new Thickness(12,10,12,8),
            CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),
            Visibility=Visibility.Collapsed
        };
        familyDrawer.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
        familyDrawer.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        var familyStack=new StackPanel();
        var familyHeaderRow=new DockPanel{Margin=new Thickness(0,0,0,7)};
        var familyPracticeBtn=new Button{Content="练这组",FontSize=11,FontWeight=FontWeights.SemiBold,Padding=new Thickness(10,3,10,3),Margin=new Thickness(8,0,0,0),Visibility=Visibility.Collapsed,ToolTip="把这组同根词放在一起对比词缀并做辨析小测"};
        if(Application.Current?.TryFindResource("AccentButton") is Style accentBtnStyle)familyPracticeBtn.Style=accentBtnStyle;
        DockPanel.SetDock(familyPracticeBtn,Dock.Right);
        familyHeaderRow.Children.Add(familyPracticeBtn);
        var familyHeader=new TextBlock{FontSize=11.5,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
        familyHeader.SetResourceReference(TextBlock.ForegroundProperty,"AccentDeepBrush");
        familyHeaderRow.Children.Add(familyHeader);
        var familyWrap=new WrapPanel();
        familyStack.Children.Add(familyHeaderRow);
        familyStack.Children.Add(familyWrap);
        familyDrawer.Child=familyStack;

        var buttons=new Dictionary<int,(Button Button,MnemonicPart Part)>();
        var selected=new HashSet<int>();
        string? expandedFamilyKey=null;
        MnemonicPart? expandedFamilyPart=null;

        familyPracticeBtn.Click+=(_,e)=>
        {
            e.Handled=true;
            if(expandedFamilyPart is {} part)OpenMorphemePractice(part.Form,part.Meaning,currentWord);
        };

        void PopulateFamilyDrawer(MnemonicPart part)
        {
            var key=MnemonicParts.NormalizeFormKey(part.Form)+"|"+part.Meaning;
            if(familyDrawer.Visibility==Visibility.Visible&&expandedFamilyKey==key)
            {
                familyDrawer.Visibility=Visibility.Collapsed;
                expandedFamilyKey=null;
                expandedFamilyPart=null;
                return;
            }
            var members=_mnemonics.FindMorphemeFamily(part.Form,part.Meaning,currentWord,w=>_dictionary.Lookup(w)?.DisplayMeaning,18);
            familyWrap.Children.Clear();
            if(members.Count==0)
            {
                familyDrawer.Visibility=Visibility.Collapsed;
                expandedFamilyKey=null;
                expandedFamilyPart=null;
                return;
            }
            expandedFamilyKey=key;
            expandedFamilyPart=part;
            var canPractice=MorphemeGroupPractice.Build(_mnemonics,part.Form,part.Meaning,currentWord,w=>_dictionary.Lookup(w)?.DisplayMeaning,BuildPriorityWordSet())!=null;
            familyPracticeBtn.Visibility=canPractice?Visibility.Visible:Visibility.Collapsed;
            familyHeader.Text=$"「{part.Form}（{part.Meaning}）」同根词家族 · 共 {members.Count} 词（点击即可查词）";
            foreach(var member in members)
            {
                var targetWord=member.Word;
                var itemStack=new StackPanel{Orientation=Orientation.Vertical};
                var wText=new TextBlock{Text=member.Word,FontSize=13,FontWeight=FontWeights.Bold};
                wText.SetResourceReference(TextBlock.ForegroundProperty,"AccentDeepBrush");
                var mText=new TextBlock{Text=member.Meaning,FontSize=11,MaxWidth=168,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,2,0,0)};
                mText.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");
                itemStack.Children.Add(wText);
                itemStack.Children.Add(mText);
                var btn=new Button
                {
                    Content=itemStack,Padding=new Thickness(9,6,9,6),Margin=new Thickness(0,0,7,6),
                    HorizontalContentAlignment=HorizontalAlignment.Left,ToolTip=$"{member.Word}\n{member.Meaning}"
                };
                btn.Click+=async(_, _)=>await LoadWordAsync(targetWord);
                familyWrap.Children.Add(btn);
            }
            familyDrawer.Visibility=Visibility.Visible;
            Desktop.UiMotion.Reveal(familyDrawer,distance:4);
        }

        void RenderSelection(Button? clickedChip)
        {
            var allLit=selected.Count==0||selected.Count==buttons.Count;
            cognatesRow.Children.Clear();
            cognatesRow.Visibility=Visibility.Collapsed;
            familyDrawer.Visibility=Visibility.Collapsed;
            expandedFamilyKey=null;

            if(allLit)
            {
                detail.Text=breakdown.Explanation;
            }
            else
            {
                var chosenParts=selected.OrderBy(i=>i).Where(buttons.ContainsKey).Select(i=>buttons[i].Part).ToArray();
                detail.Text=string.Join("\n",chosenParts.Select(p=>p.FocusExplanation??$"{p.Form} · {p.Meaning}"));

                var examples=chosenParts.SelectMany(p=>p.Examples??[]).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray();
                if(examples.Length>0)
                {
                    var label=new TextBlock{Text="同根词：",FontSize=11,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,4,4)};
                    label.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");
                    cognatesRow.Children.Add(label);
                    foreach(var exWord in examples)
                    {
                        var target=exWord;
                        var exBtn=new Button{Content=target,FontSize=11,Padding=new Thickness(8,2.5,8,2.5),Margin=new Thickness(0,0,5,4),ToolTip=$"点击查询「{target}」"};
                        if(pillStyle!=null)exBtn.Style=pillStyle;
                        exBtn.Click+=async(_,e)=>{e.Handled=true;await LoadWordAsync(target);};
                        cognatesRow.Children.Add(exBtn);
                    }
                }
                if(chosenParts.Length==1&&_mnemonics.FindMorphemeFamily(chosenParts[0].Form,chosenParts[0].Meaning,currentWord,null,1).Count>0)
                {
                    var primary=chosenParts[0];
                    var familyBtn=new Button{Content="同根词家族 →",FontSize=11,FontWeight=FontWeights.SemiBold,Padding=new Thickness(9,2.5,9,2.5),Margin=new Thickness(2,0,0,4)};
                    if(pillStyle!=null)familyBtn.Style=pillStyle;
                    familyBtn.Click+=(_,e)=>{e.Handled=true;PopulateFamilyDrawer(primary);};
                    cognatesRow.Children.Add(familyBtn);
                    if(MorphemeGroupPractice.Build(_mnemonics,primary.Form,primary.Meaning,currentWord,w=>_dictionary.Lookup(w)?.DisplayMeaning,BuildPriorityWordSet())!=null)
                    {
                        var practiceBtn=new Button{Content="练这组",FontSize=11,FontWeight=FontWeights.SemiBold,Padding=new Thickness(10,2.5,10,2.5),Margin=new Thickness(5,0,0,4),ToolTip="把这组同根词放在一起对比词缀并做辨析小测"};
                        if(Application.Current?.TryFindResource("AccentButton") is Style accentStyle)practiceBtn.Style=accentStyle;
                        practiceBtn.Click+=(_,e)=>{e.Handled=true;OpenMorphemePractice(primary.Form,primary.Meaning,currentWord);};
                        cognatesRow.Children.Add(practiceBtn);
                    }
                }
                if(cognatesRow.Children.Count>0)cognatesRow.Visibility=Visibility.Visible;
            }

            foreach(var (position,entry) in buttons)
            {
                var lit=allLit||selected.Contains(position);
                var emphasized=!allLit&&selected.Contains(position);
                entry.Button.SetResourceReference(Control.BackgroundProperty,lit?(emphasized?"SelectedBgBrush":"PillBgBrush"):"SurfaceBrush");
                entry.Button.BorderBrush=Brush(emphasized?"#19ACF2":lit?"#38B8F6":(Desktop.ThemeManager.IsDark?"#345068":"#C6E4F7"));
                entry.Button.Opacity=1.0;
                entry.Button.Effect=lit?LitShadow(emphasized?0.22:0.15):null;
            }
            if(clickedChip!=null)Desktop.UiMotion.Pop(clickedChip,peakScale:1.04,durationMs:180);
            Desktop.UiMotion.Reveal(detail,distance:3);
        }

        void ToggleChoice(int choice,Button clickedChip)
        {
            if(selected.Count==0||selected.Count==buttons.Count)
            {
                selected.Clear();
                selected.Add(choice);
            }
            else if(selected.Contains(choice))
            {
                if(selected.Count==1)selected.Clear();
                else selected.Remove(choice);
            }
            else
            {
                selected.Add(choice);
                if(selected.Count==buttons.Count)selected.Clear();
            }
            RenderSelection(clickedChip);
        }

        for(var index=0;index<breakdown.Parts.Count;index++)
        {
            if(index>0)
                builder.Children.Add(new TextBlock{Text="＋",FontSize=14,FontWeight=FontWeights.Bold,Foreground=Brush("#6CA9CC"),VerticalAlignment=VerticalAlignment.Center});
            var part=breakdown.Parts[index];
            if(part.Meaning.Length==0)
            {
                var unlabeled=new Border
                {
                    Padding=new Thickness(11,7,11,7),BorderThickness=new Thickness(1.5),
                    CornerRadius=new CornerRadius(10),VerticalAlignment=VerticalAlignment.Center,
                    Child=new TextBlock{Text=part.Form,FontSize=14.5,FontWeight=FontWeights.Bold}
                };
                unlabeled.SetResourceReference(Border.BackgroundProperty,"SurfaceBrush");
                unlabeled.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
                ((TextBlock)unlabeled.Child).SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");
                builder.Children.Add(unlabeled);
                continue;
            }
            var choice=index;
            var body=new StackPanel{Orientation=Orientation.Vertical};
            var formTb=new TextBlock{Text=part.Form,FontSize=14,FontWeight=FontWeights.Bold};
            formTb.SetResourceReference(TextBlock.ForegroundProperty,"AccentDeepBrush");
            var meanTb=new TextBlock{Text=part.Meaning,FontSize=11,Margin=new Thickness(0,1,0,0),TextWrapping=TextWrapping.Wrap,MaxWidth=185};
            meanTb.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");
            body.Children.Add(formTb);
            body.Children.Add(meanTb);
            var chip=new Button
            {
                Content=body,Style=chipStyle,
                BorderBrush=Brush("#38B8F6"),BorderThickness=new Thickness(1.3),
                Effect=LitShadow(0.11),ToolTip=part.FocusExplanation??part.Meaning,VerticalAlignment=VerticalAlignment.Center
            };
            chip.SetResourceReference(Control.BackgroundProperty,"PillBgBrush");
            AutomationProperties.SetName(chip,$"{part.Form}，点击查看或组合构词含义");
            buttons.Add(index,(chip,part));
            builder.Children.Add(chip);
            chip.Click+=(_,_)=>ToggleChoice(choice,chip);
        }

        builder.Children.Add(new TextBlock{Text="→",FontSize=14,FontWeight=FontWeights.Bold,Foreground=Brush("#6CA9CC"),VerticalAlignment=VerticalAlignment.Center});
        var resultStack=new StackPanel{Orientation=Orientation.Vertical};
        if(breakdown.Word is {Length:>0} word)
        {
            var wordTb=new TextBlock{Text=word,FontSize=13.5,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,1)};
            wordTb.SetResourceReference(TextBlock.ForegroundProperty,"InkStrongBrush");
            resultStack.Children.Add(wordTb);
        }
        resultStack.Children.Add(detail);
        resultStack.Children.Add(cognatesRow);
        var resultCard=new Border
        {
            Padding=new Thickness(11,6,11,6),
            CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(1),
            VerticalAlignment=VerticalAlignment.Center,Cursor=Cursors.Hand,
            ToolTip="点击还原完整构词解释",
            Child=resultStack
        };
        resultCard.SetResourceReference(Border.BackgroundProperty,"AccentWashBrush");
        resultCard.SetResourceReference(Border.BorderBrushProperty,"PillBorderBrush");
        resultCard.MouseLeftButtonUp+=(_,e)=>{if(e.OriginalSource is not Button&&selected.Count>0){selected.Clear();RenderSelection(null);}};
        builder.Children.Add(resultCard);
        wrapper.Children.Add(builder);
        wrapper.Children.Add(familyDrawer);
        return wrapper;
    }

    private sealed class MorphemeWrapPanel : Panel
    {
        public double HorizontalSpacing {get;set;}=7;
        public double VerticalSpacing {get;set;}=8;
        public bool ExpandLastChild {get;set;}=true;
        public double LastChildMinWidth {get;set;}=180;

        protected override Size MeasureOverride(Size availableSize)
        {
            var maxWidth=double.IsInfinity(availableSize.Width)?10000:availableSize.Width;
            var totalHeight=0.0;
            var maxRowWidth=0.0;
            var rowWidth=0.0;
            var rowHeight=0.0;
            var count=InternalChildren.Count;

            for(var i=0;i<count;i++)
            {
                var child=InternalChildren[i];
                var isLast=ExpandLastChild&&i==count-1;
                if(isLast)
                {
                    var remaining=rowWidth>0?maxWidth-rowWidth-HorizontalSpacing:maxWidth;
                    if(rowWidth>0&&remaining<LastChildMinWidth)
                    {
                        maxRowWidth=Math.Max(maxRowWidth,rowWidth);
                        totalHeight+=rowHeight+VerticalSpacing;
                        rowWidth=0;rowHeight=0;
                        remaining=maxWidth;
                    }
                    var targetWidth=Math.Max(1,double.IsInfinity(availableSize.Width)?LastChildMinWidth:remaining);
                    child.Measure(new Size(targetWidth,double.PositiveInfinity));
                    var usedWidth=double.IsInfinity(availableSize.Width)?child.DesiredSize.Width:targetWidth;
                    rowWidth=rowWidth>0?rowWidth+HorizontalSpacing+usedWidth:usedWidth;
                    rowHeight=Math.Max(rowHeight,child.DesiredSize.Height);
                }
                else
                {
                    child.Measure(new Size(maxWidth,double.PositiveInfinity));
                    var desired=child.DesiredSize;
                    if(rowWidth>0&&rowWidth+HorizontalSpacing+desired.Width>maxWidth)
                    {
                        maxRowWidth=Math.Max(maxRowWidth,rowWidth);
                        totalHeight+=rowHeight+VerticalSpacing;
                        rowWidth=desired.Width;
                        rowHeight=desired.Height;
                    }
                    else
                    {
                        rowWidth=rowWidth>0?rowWidth+HorizontalSpacing+desired.Width:desired.Width;
                        rowHeight=Math.Max(rowHeight,desired.Height);
                    }
                }
            }
            maxRowWidth=Math.Max(maxRowWidth,rowWidth);
            totalHeight+=rowHeight;
            return new Size(double.IsInfinity(availableSize.Width)?maxRowWidth:availableSize.Width,totalHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var maxWidth=finalSize.Width;
            var count=InternalChildren.Count;
            var rows=new List<List<(UIElement Child,double Width,double Height)>>();
            var current=new List<(UIElement Child,double Width,double Height)>();
            var rowWidth=0.0;

            for(var i=0;i<count;i++)
            {
                var child=InternalChildren[i];
                var isLast=ExpandLastChild&&i==count-1;
                if(isLast)
                {
                    var remaining=current.Count>0?maxWidth-rowWidth-HorizontalSpacing:maxWidth;
                    if(current.Count>0&&remaining<LastChildMinWidth)
                    {
                        rows.Add(current);
                        current=[];
                        rowWidth=0;
                        remaining=maxWidth;
                    }
                    var width=Math.Max(1,remaining);
                    child.Measure(new Size(width,double.PositiveInfinity));
                    current.Add((child,width,child.DesiredSize.Height));
                }
                else
                {
                    var w=Math.Min(child.DesiredSize.Width,maxWidth);
                    var h=child.DesiredSize.Height;
                    if(current.Count>0&&rowWidth+HorizontalSpacing+w>maxWidth)
                    {
                        rows.Add(current);
                        current=[(child,w,h)];
                        rowWidth=w;
                    }
                    else
                    {
                        rowWidth=current.Count>0?rowWidth+HorizontalSpacing+w:w;
                        current.Add((child,w,h));
                    }
                }
            }
            if(current.Count>0)rows.Add(current);

            var y=0.0;
            foreach(var row in rows)
            {
                var rowHeight=row.Max(item=>item.Height);
                var x=0.0;
                foreach(var (child,w,h) in row)
                {
                    var itemY=y+(rowHeight-h)/2;
                    child.Arrange(new Rect(x,itemY,w,h));
                    x+=w+HorizontalSpacing;
                }
                y+=rowHeight+VerticalSpacing;
            }
            return new Size(finalSize.Width,Math.Max(0,y-VerticalSpacing));
        }
    }

    private HashSet<string> BuildPriorityWordSet()
    {
        var set=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var h in _preferences.SearchHistory)
            if(!string.IsNullOrWhiteSpace(h.Word))set.Add(h.Word);
        if(_stubbornWords!=null)
            foreach(var s in _stubbornWords.Words)
                if(!string.IsNullOrWhiteSpace(s.Spelling))set.Add(s.Spelling);
        foreach(var (_,entry) in _notepadCache)
            foreach(var w in NotepadContent.Words(entry.Pad.Content))
                set.Add(w);
        return set;
    }

    private void OpenMorphemePractice(string form,string meaning,string currentWord)
    {
        var group=MorphemeGroupPractice.Build(
            _mnemonics,form,meaning,currentWord,
            w=>_dictionary.Lookup(w)?.DisplayMeaning,
            BuildPriorityWordSet());
        if(group==null)
        {
            StatusText.Text=$"「{form}」同根词数量不足，暂无法组成辨析练习。";
            return;
        }
        var dialog=Desktop.MorphemeGroupPracticeDialog.Build(
            this,group,
            speak:(w,accent)=>PlaySpeechAsync(w,accent),
            lookupWord:w=>_=LoadWordAsync(w),
            mistakeBook:_mistakeBook);
        dialog.ShowDialog();
        UpdateContextReviewButtons();
    }

    private string LocalNotesHint()=>_displayedNotes.Length>0?$"本地助记 · {_displayedNotes.Length} 条":"暂无助记，点击「编辑助记」添加。";
    private async Task LoadLocalAndCloudNotesAsync(string word,CancellationToken cancellation)
    {
        _notesCts?.Cancel();_notesCts?.Dispose();_notesCts=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var token=_notesCts.Token;var version=++_notesVersion;
        var accountVersion=_accountVersion;var credential=_accountCredentialKey;
        if(credential!=_tokens.CredentialKey){CheckAccountExpiry();return;}
        bool Current()=>!token.IsCancellationRequested&&version==_notesVersion&&word==_word&&accountVersion==_accountVersion&&credential==_tokens.CredentialKey;
        var cacheKey=(credential??"offline")+":"+word.ToLowerInvariant();
        var accountKey=MnemonicAccountKey;
        RenderNotes(word,_mnemonics.Get(word,accountKey));NotesHintText.Text=LocalNotesHint();
        if(!_allowCloud||!_tokens.HasToken)
        {
            SetAccountStatus(!_allowCloud?"离线模式":InactiveAccountStatus);
            StatusText.Text="查词完成。";
            if(_mnemonics.LoadWarning!=null)NotesHintText.Text+=" · "+_mnemonics.LoadWarning;
            return;
        }
        NotesHintText.Text="正在加载云端助记…";
        var studyStarted=false;var studyTask=Task.CompletedTask;
        try
        {
            Vocabulary? voc; IReadOnlyList<MemoNote> notes; IReadOnlyList<MemoPhrase> phrases = []; var phrasesLoaded = false;
            var hitCache = _cloudCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.Time < TimeSpan.FromMinutes(5);
            if (hitCache)
            {
                voc = cached.Voc;
                notes = cached.Notes;
                phrases = cached.Phrases;
                phrasesLoaded = cached.PhrasesLoaded;
            }
            else
            {
                voc = await _api.ResolveWordAsync(word, token); token.ThrowIfCancellationRequested();
                if (!Current()) return;
                _vocabulary = voc; studyTask = LoadTodayStudyStatusAsync(word, voc); studyStarted = true;
                notes = voc == null ? [] : await _api.GetNotesAsync(voc.Id, token);
                token.ThrowIfCancellationRequested(); if (!Current()) return;
                _cloudCache[cacheKey] = (DateTime.UtcNow, voc, notes, [], voc == null);
                phrasesLoaded = voc == null;
            }
            if (!Current()) return;
            _vocabulary = voc; if (!studyStarted) { studyTask = LoadTodayStudyStatusAsync(word, voc); studyStarted = true; }
            SetAccountStatus("已连接");
            var usable = notes.Where(note => !string.IsNullOrWhiteSpace(note.Text)).Select(note => new StoredMnemonic("cloud:" + note.Id, note.NoteType, note.Text, note.Id, "cloud", accountKey)).ToArray();
            if (usable.Length > 0)
            {
                RenderNotes(word, usable, true); NotesHintText.Text = $"云端助记 · {usable.Length} 条";
                try { if (accountKey != null) _mnemonics.SaveCloud(word, usable, accountKey); }
                catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException) { NotesHintText.Text += " · 未能保存到本机"; }
            }
            else
            {
                if (accountKey != null) _mnemonics.SaveCloud(word, [], accountKey);
                RenderNotes(word, _mnemonics.Get(word, accountKey)); NotesHintText.Text = LocalNotesHint();
            }
            StatusText.Text = "查词完成";

            if (voc != null && accountKey != null)
            {
                if (!phrasesLoaded)
                {
                    try
                    {
                        phrases = await _api.GetPhrasesAsync(voc.Id, token);
                        token.ThrowIfCancellationRequested();
                        if (!Current()) return;
                        phrasesLoaded = true;
                        _cloudCache[cacheKey] = (DateTime.UtcNow, voc, notes, phrases, true);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception)
                    {
                        // Do not break notes display if phrase API permission or transient network fails.
                    }
                }
                if (phrasesLoaded && Current())
                {
                    var cloudRecords = phrases
                        .Select(p => new CloudPhraseRecord(p.Id, p.Text, p.Interpretation, p.Origin, p.Tags))
                        .ToArray();
                    _contextSentences.MergeCloud(word, cloudRecords, accountKey);
                    RenderSavedContexts(word);
                    RefreshExampleSaveButtons();
                    UpdateContextCaptureBanner(word);
                    UpdateContextReviewButtons();
                }
            }
        }
        catch(OperationCanceledException){ }
        catch(Exception error)
        {
            if(!Current())return;
            NotesHintText.Text=LocalNotesHint()+" · "+UserError(error);SetAccountError(error);
            if(!studyStarted)FailTodayStudyLookup(word,error);
        }
        finally{await studyTask;}
    }
    private void EditNotesButton_Click(object sender,RoutedEventArgs e)=>OpenMnemonicEditor(_displayedNotes.FirstOrDefault());
    private void OpenMnemonicEditor(StoredMnemonic? original)
    {
        CreateMnemonicEditor(original)?.ShowDialog();
    }
    internal Window? CreateMnemonicEditor(StoredMnemonic? original)
    {
        if(!_ready||!ValidWord(_word))return null;
        var word=_word;var account=_accountVersion;var accountKey=MnemonicAccountKey;var note=original??new StoredMnemonic("local:"+Guid.NewGuid().ToString("N"),"词根词缀","");
        ++_notesVersion;_notesCts?.Cancel();NotesHintText.Text=LocalNotesHint();
        return MnemonicEditor.Build(this,word,note.NoteType,note.Text,_allowCloud&&_tokens.HasToken,async(type,text,sync)=>
        {
            var edited=note with {NoteType=type,Text=text,Origin="user"};
            void Persist(StoredMnemonic value)
            {
                var current=_mnemonics.Get(word,accountKey).ToList();var index=current.FindIndex(item=>item.Id==note.Id);
                if(index<0)current.Add(value);else current[index]=value;
                _mnemonics.SaveEdit(word,value,accountKey);_cloudCache.Remove((_accountCredentialKey??"offline")+":"+word.ToLowerInvariant());
                if(word==_word){RenderNotes(word,current,value.Origin=="cloud");NotesHintText.Text=value.Origin=="cloud"?"已同步到墨墨。":"已保存到本机。";}
            }
            Persist(edited);
            if(!sync)return;
            if(account!=_accountVersion)throw new MaiMemoException("账号已变更，本地已保存，请重新打开助记后同步。");
            try
            {
                var voc=_vocabulary?.Spelling.Equals(word,StringComparison.OrdinalIgnoreCase)==true?_vocabulary:await _api.ResolveWordAsync(word);
                if(voc==null)throw new MaiMemoException("墨墨尚未收录这个单词。");
                var saved=await _api.SaveNoteAsync(voc.Id,note.AccountKey==accountKey?note.CloudId:null,type,text,_accountCts.Token);
                edited=edited with {CloudId=saved.Id,NoteType=saved.NoteType,Text=saved.Text,Origin="cloud",AccountKey=accountKey};
                note=edited;Persist(edited);
            }
            catch(Exception error){SetAccountError(error);throw new MaiMemoException("已保存到本地，未能同步到墨墨："+UserError(error));}
        });
    }
}
