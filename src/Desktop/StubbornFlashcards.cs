using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex.Desktop;

internal static class StubbornFlashcards
{
    internal static Window Build(Window owner, IEnumerable<string> words, LocalDictionary dictionary,
        OnlineDictionaryClient? online, Func<string, string?> mnemonic,
        FlashcardProgressSnapshot? savedProgress = null,
        Action<FlashcardProgressSnapshot>? onProgressChanged = null,
        Action? onResetProgress = null,
        bool persistProgress = true,
        string title = "顽固单词 · 卡片自测",
        Func<string, EnglishAccent, Task>? speak = null,
        Func<string, IReadOnlyList<StoredMnemonic>>? getMnemonics = null,
        MnemonicStore? mnemonicStore = null,
        Preferences? preferences = null)
    {
        var freshWords = words.ToArray();
        var practice = new FlashcardPractice(freshWords, persistProgress ? savedProgress : null);
        var dialog = InputDialog.Shell(owner, title, out var body);
        dialog.Width = 660;
        dialog.MaxHeight = Math.Max(560, SystemParameters.WorkArea.Height - 50);

        var headerRow = new DockPanel { LastChildFill = true };
        var resetButton = new Button
        {
            Content = "重置进度",
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "清除本地保存的自测进度，从第 1 组重新开始"
        };
        DockPanel.SetDock(resetButton, Dock.Right);
        headerRow.Children.Add(resetButton);

        var progress = InputDialog.Text("");
        progress.FontWeight = FontWeights.SemiBold;
        progress.VerticalAlignment = VerticalAlignment.Center;
        progress.Margin = new Thickness(0, 0, 10, 0);
        headerRow.Children.Add(progress);
        body.Children.Add(headerRow);

        // Mode & Pronunciation control bar
        var toolsBar = new DockPanel { Margin = new Thickness(0, 8, 0, 2) };
        var modePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var flipModeBtn = new Button { Content = "翻卡模式", FontSize = 11.5, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 5, 0) };
        var spellModeBtn = new Button { Content = "拼写默写", FontSize = 11.5, Padding = new Thickness(10, 4, 10, 4) };
        modePanel.Children.Add(flipModeBtn);
        modePanel.Children.Add(spellModeBtn);
        toolsBar.Children.Add(modePanel);

        var speakTools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(speakTools, Dock.Right);
        var autoSpeakLabel = new TextBlock { Text = "自动朗读", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        autoSpeakLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var autoSpeakCombo = new ComboBox
        {
            Width = 84, Height = 28, FontSize = 11.5, Padding = new Thickness(8, 2, 8, 2),
            ItemsSource = new[]
            {
                new AutoSpeakChoice("off", "关闭"),
                new AutoSpeakChoice("en-GB", "英音"),
                new AutoSpeakChoice("en-US", "美音")
            },
            DisplayMemberPath = nameof(AutoSpeakChoice.Label),
            SelectedValuePath = nameof(AutoSpeakChoice.Key),
            SelectedValue = preferences?.FlashcardAutoSpeak ?? "off",
            Margin = new Thickness(0, 0, 6, 0)
        };
        var speakNowContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        speakNowContent.Children.Add(new TextBlock
        {
            Text = "\uE767",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });
        speakNowContent.Children.Add(new TextBlock
        {
            Text = "朗读",
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center
        });
        var speakNowBtn = new Button
        {
            Content = speakNowContent,
            Padding = new Thickness(10, 4, 10, 4),
            ToolTip = "朗读当前单词"
        };
        if (Application.Current.TryFindResource("PillButton") is Style pillStyle) speakNowBtn.Style = pillStyle;
        if (speak != null)
        {
            speakTools.Children.Add(autoSpeakLabel);
            speakTools.Children.Add(autoSpeakCombo);
            speakTools.Children.Add(speakNowBtn);
            toolsBar.Children.Add(speakTools);
        }
        body.Children.Add(toolsBar);

        var progressTrack = new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 8, 0, 2),
            ClipToBounds = true
        };
        progressTrack.SetResourceReference(Border.BackgroundProperty, "AccentWashBrush");
        var progressFill = new Border
        {
            Height = 6,
            Width = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(3),
            Background = new LinearGradientBrush(
                Color.FromRgb(0x36, 0xBE, 0xFA),
                Color.FromRgb(0x0E, 0x86, 0xC2),
                new Point(0, 0), new Point(1, 0))
        };
        progressTrack.Child = progressFill;
        body.Children.Add(progressTrack);

        var face = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var cardScroll = new ScrollViewer
        {
            Content = face,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 385,
            Focusable = false
        };
        var card = new Button
        {
            Content = cardScroll,
            MinHeight = 325,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(26, 20, 26, 20),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Opacity = 0.09, Color = Color.FromRgb(0x10, 0x42, 0x66) }
        };
        card.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");

        var stackBack2 = new Border
        {
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(22, 14, 22, -10),
            Opacity = 0.65,
            IsHitTestVisible = false
        };
        stackBack2.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
        stackBack2.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var stackBack1 = new Border
        {
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(11, 7, 11, -5),
            Opacity = 0.88,
            IsHitTestVisible = false
        };
        stackBack1.SetResourceReference(Border.BackgroundProperty, "CardAltBrush");
        stackBack1.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var stage = new Grid { Margin = new Thickness(0, 10, 0, 16), ClipToBounds = false };
        stage.Children.Add(stackBack2);
        stage.Children.Add(stackBack1);
        stage.Children.Add(card);

        var turn = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        var image = new ImageBrush();
        var surface = new MeshGeometry3D
        {
            TriangleIndices = new Int32Collection([0, 1, 2, 0, 2, 3]),
            TextureCoordinates = new PointCollection([new(0, 1), new(1, 1), new(1, 0), new(0, 0)])
        };
        var viewport = new Viewport3D
        {
            Camera = new PerspectiveCamera(new Point3D(0, 0, 2.42), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 45),
            IsHitTestVisible = false,
            Visibility = Visibility.Hidden
        };
        viewport.Children.Add(new ModelVisual3D
        {
            Content = new GeometryModel3D(surface, new EmissiveMaterial(image)) { Transform = new RotateTransform3D(turn) }
        });
        stage.Children.Add(viewport);
        body.Children.Add(stage);

        var actionSlot = new Grid { MinHeight = 38 };
        var preFlipHintText = new TextBlock { Text = "先点击卡片翻面核对释义（空格键），再选择掌握程度", FontSize = 12 };
        preFlipHintText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var preFlipHint = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(14, 6, 14, 6),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = preFlipHintText
        };
        preFlipHint.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
        preFlipHint.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var actions = new UniformGrid { Rows = 1, Visibility = Visibility.Collapsed };
        Button Action(string label, string? styleKey = null)
        {
            var button = new Button { Content = label, Margin = new Thickness(4, 0, 4, 0), Padding = new Thickness(12, 9, 12, 9) };
            if (styleKey != null && Application.Current.TryFindResource(styleKey) is Style style)
                button.Style = style;
            actions.Children.Add(button);
            return button;
        }
        var known = Action("认识", "KnownButton");
        var vague = Action("模糊", "VagueButton");
        var forgotten = Action("忘记", "ForgottenButton");
        var flipCard = new Button { Content = "翻转卡片", Visibility = Visibility.Collapsed, Focusable = false };
        actionSlot.Children.Add(preFlipHint);
        actionSlot.Children.Add(actions);
        actionSlot.Children.Add(flipCard);
        body.Children.Add(actionSlot);

        var completionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = Visibility.Collapsed
        };
        var restart = new Button { Content = "重新开始", Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
        var next = new Button { Style = (Style)Application.Current.FindResource("AccentButton") };
        completionRow.Children.Add(restart);
        completionRow.Children.Add(next);
        body.Children.Add(completionRow);
        var hint = InputDialog.Text(persistProgress
            ? "快捷键：空格翻卡 · 1 认识 · 2 模糊 · 3 忘记（自测进度自动保存到本地，可随时重置）"
            : "快捷键：空格翻卡 · 1 认识 · 2 模糊 · 3 忘记（当前为筛选结果自测，不覆盖全部自测进度）");
        hint.FontSize = 11;
        hint.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(hint);

        dialog.RegisterName("Flashcard", card);
        dialog.RegisterName("FlashcardProgress", progress);
        dialog.RegisterName("FlashcardKnown", known);
        dialog.RegisterName("FlashcardVague", vague);
        dialog.RegisterName("FlashcardForgotten", forgotten);
        dialog.RegisterName("FlashcardFlipButton", flipCard);
        dialog.RegisterName("FlashcardNextGroup", next);
        dialog.RegisterName("FlashcardReset", resetButton);

        var revealed = false;
        var flipping = false;
        var swiping = false;
        var spellingMode = false;
        var spellingFeedback = "";
        var spellingCorrect = false;
        TextBox? activeSpellingBox = null;
        System.Windows.Threading.DispatcherTimer? autoFlipTimer = null;
        DictionaryEntry? entry = null;

        void CancelAutoFlip()
        {
            if (autoFlipTimer == null) return;
            autoFlipTimer.Stop();
            autoFlipTimer = null;
        }

        void UpdateModeButtons()
        {
            var pill = Application.Current.TryFindResource("PillButton") as Style;
            var normal = Application.Current.TryFindResource(typeof(Button)) as Style;
            flipModeBtn.Style = !spellingMode ? pill : normal;
            spellModeBtn.Style = spellingMode ? pill : normal;
            preFlipHintText.Text = spellingMode
                ? "根据释义输入拼写，拼对自动翻面；没拼完点「核对拼写」获取提示"
                : "先点击卡片翻面核对释义（空格键），再选择掌握程度";
        }
        UpdateModeButtons();

        void SpeakCurrentWord(EnglishAccent? overrideAccent = null)
        {
            if (speak == null || practice.Current is not { } current) return;
            var mode = autoSpeakCombo.SelectedValue as string ?? "off";
            var accent = overrideAccent ?? (mode == "en-GB" ? EnglishAccent.British : EnglishAccent.American);
            _ = speak(current, accent);
        }

        void Persist()
        {
            if (persistProgress)
                onProgressChanged?.Invoke(practice.ExportSnapshot());
        }

        void UpdateProgressBar()
        {
            if (progressTrack.ActualWidth <= 0) return;
            var total = Math.Max(1, practice.GroupWordCount);
            var ratio = Math.Clamp((double)(total - practice.Remaining) / total, 0, 1);
            UiMotion.AnimateWidth(progressFill, progressTrack.ActualWidth * ratio, 280);
        }
        progressTrack.SizeChanged += (_, _) => UpdateProgressBar();

        void Render(bool animateActions = false)
        {
            activeSpellingBox = null;
            var word = practice.Current;
            progress.Text = $"第 {practice.GroupNumber} / {practice.GroupCount} 组 · 剩余 {practice.Remaining} / {practice.GroupWordCount} 词 · 已掌握 {practice.MasteredCount} / {practice.TotalWordCount}";
            resetButton.IsEnabled = practice.HasProgress || (word == null && !practice.HasNextGroup);
            speakNowBtn.IsEnabled = word != null;
            UpdateProgressBar();

            var showRating = word != null && revealed;
            actions.Visibility = showRating ? Visibility.Visible : Visibility.Collapsed;
            preFlipHint.Visibility = word != null && !revealed ? Visibility.Visible : Visibility.Collapsed;
            stackBack1.Visibility = word != null && practice.Remaining > 1 ? Visibility.Visible : Visibility.Collapsed;
            stackBack2.Visibility = word != null && practice.Remaining > 2 ? Visibility.Visible : Visibility.Collapsed;
            if (showRating && animateActions)
                UiMotion.Reveal(actions, distance: 6);

            foreach (var button in new[] { known, vague, forgotten }) button.IsEnabled = showRating;
            flipCard.Content = revealed ? "翻回单词" : "翻转卡片";
            completionRow.Visibility = word == null ? Visibility.Visible : Visibility.Collapsed;
            restart.Visibility = word == null && !practice.HasNextGroup ? Visibility.Visible : Visibility.Collapsed;
            next.Content = practice.HasNextGroup ? "下一组" : "完成";
            card.IsHitTestVisible = card.Focusable = word != null;
            AutomationProperties.SetName(card, word == null ? "本组已完成" : revealed ? $"{word}，点击隐藏释义" : $"{word}，点击查看释义");

            face.Children.Clear();
            cardScroll.ScrollToTop();
            card.VerticalContentAlignment = revealed && word != null ? VerticalAlignment.Top : VerticalAlignment.Center;
            if (word == null)
            {
                var doneText = new TextBlock
                {
                    Text = practice.HasNextGroup ? "本组已完成" : "全部练习完成",
                    FontSize = 26,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };
                doneText.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
                face.Children.Add(doneText);
                return;
            }

            if (!revealed)
            {
                if (spellingMode)
                {
                    RenderSpellingFront(word);
                    return;
                }

                var badgeText = new TextBlock { Text = $"第 {practice.GroupNumber} 组 · 点击翻面", FontSize = 12, FontWeight = FontWeights.SemiBold };
                badgeText.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
                var badge = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 12),
                    Padding = new Thickness(11, 3.5, 11, 3.5),
                    CornerRadius = new CornerRadius(11),
                    Child = badgeText
                };
                badge.SetResourceReference(Border.BackgroundProperty, "PillBgBrush");
                face.Children.Add(badge);

                var wordBlock = new TextBlock
                {
                    Text = word,
                    FontSize = 44,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                };
                wordBlock.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
                face.Children.Add(wordBlock);

                if (!string.IsNullOrWhiteSpace(entry?.phonetic))
                {
                    var phonetic = InputDialog.Text($"/{entry.phonetic.Trim('/')}/");
                    phonetic.FontSize = 16;
                    phonetic.TextAlignment = TextAlignment.Center;
                    phonetic.Margin = new Thickness(0, 6, 0, 0);
                    face.Children.Add(phonetic);
                }

                var cueText = new TextBlock { Text = "↻ 点击翻卡核对释义", FontSize = 13, FontWeight = FontWeights.SemiBold };
                cueText.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
                var cuePill = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0),
                    Padding = new Thickness(14, 6, 14, 6),
                    CornerRadius = new CornerRadius(13),
                    Child = cueText
                };
                cuePill.SetResourceReference(Border.BackgroundProperty, "PillBgBrush");
                face.Children.Add(cuePill);
                return;
            }

            // Back face: compact header row so definition + mnemonic both fit without scrolling
            var backHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var backPillText = new TextBlock { Text = "点击翻回", FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            backPillText.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
            var backPill = new Border
            {
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 3, 10, 3),
                CornerRadius = new CornerRadius(10),
                Child = backPillText
            };
            backPill.SetResourceReference(Border.BackgroundProperty, "PillBgBrush");
            DockPanel.SetDock(backPill, Dock.Right);
            backHeader.Children.Add(backPill);

            var wordRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            var backWord = new TextBlock
            {
                Text = word,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 10, 0)
            };
            backWord.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
            wordRow.Children.Add(backWord);
            if (!string.IsNullOrWhiteSpace(entry?.phonetic))
            {
                var phonetic = InputDialog.Text($"/{entry.phonetic.Trim('/')}/");
                phonetic.FontSize = 15;
                phonetic.VerticalAlignment = VerticalAlignment.Bottom;
                phonetic.Margin = new Thickness(0, 0, 8, 3);
                wordRow.Children.Add(phonetic);
            }
            if (spellingFeedback.Length > 0)
            {
                var fbBadge = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 2.5, 8, 2.5),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = ThemeManager.Hex(spellingCorrect ? "#E6FAF1" : "#FFF1F3"),
                    Child = new TextBlock
                    {
                        Text = spellingFeedback,
                        FontSize = 11.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = ThemeManager.Hex(spellingCorrect ? "#0E7C55" : "#C4253F")
                    }
                };
                wordRow.Children.Add(fbBadge);
            }
            backHeader.Children.Add(wordRow);
            face.Children.Add(backHeader);

            var sep = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 10) };
            sep.SetResourceReference(Border.BackgroundProperty, "RuleBrush");
            face.Children.Add(sep);

            var defLabel = new TextBlock { Text = "中文释义", FontSize = 12.5, FontWeight = FontWeights.SemiBold };
            defLabel.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
            face.Children.Add(defLabel);

            var definition = new WordListMeaningText
            {
                DataContext = new WordListRow(word, entry?.DisplayMeaning ?? "", online),
                FontSize = 18.5,
                LineHeight = 28,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            definition.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            definition.SetBinding(TextBlock.TextProperty, new Binding(nameof(WordListRow.DisplayMeaning)));
            face.Children.Add(definition);

            RenderBackMnemonic(word);
        }

        void RenderSpellingFront(string word)
        {
            var badgeText = new TextBlock { Text = $"第 {practice.GroupNumber} 组 · 拼写默写", FontSize = 12, FontWeight = FontWeights.SemiBold };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
            var badge = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(11, 3.5, 11, 3.5),
                CornerRadius = new CornerRadius(11),
                Child = badgeText
            };
            badge.SetResourceReference(Border.BackgroundProperty, "PillBgBrush");
            face.Children.Add(badge);

            var meaningText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry?.DisplayMeaning) ? "请听发音并拼写该单词" : entry.DisplayMeaning,
                FontSize = 19,
                LineHeight = 28,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(6, 0, 6, 8)
            };
            meaningText.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
            face.Children.Add(meaningText);

            if (!string.IsNullOrWhiteSpace(entry?.phonetic))
            {
                var phonetic = InputDialog.Text($"/{entry.phonetic.Trim('/')}/");
                phonetic.FontSize = 14.5;
                phonetic.TextAlignment = TextAlignment.Center;
                phonetic.Margin = new Thickness(0, 0, 0, 14);
                face.Children.Add(phonetic);
            }

            var inputBox = new TextBox
            {
                Width = 350,
                MinHeight = 48,
                Padding = new Thickness(14, 7, 14, 7),
                FontSize = 20,
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                HorizontalAlignment = HorizontalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 10)
            };
            // Prevent card click from firing when interacting with the input box
            inputBox.PreviewMouseDown += (_, e) => e.Handled = false;
            activeSpellingBox = inputBox;
            face.Children.Add(inputBox);

            var hintLetter = new TextBlock
            {
                Text = "",
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            var hintPill = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 0, 10),
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed,
                Child = hintLetter
            };
            face.Children.Add(hintPill);

            var hintLevel = 0;

            void ShowSpellingHint(string message, string tone)
            {
                hintLetter.Text = message;
                if (tone == "success")
                {
                    hintPill.Background = ThemeManager.Hex(ThemeManager.IsDark ? "#123528" : "#E6FAF1");
                    hintPill.BorderBrush = ThemeManager.Hex(ThemeManager.IsDark ? "#1E6F50" : "#A7E8CC");
                    hintLetter.Foreground = ThemeManager.Hex(ThemeManager.IsDark ? "#34D399" : "#0E7C55");
                    inputBox.BorderBrush = ThemeManager.Hex("#10B981");
                }
                else if (tone == "warn")
                {
                    hintPill.Background = ThemeManager.Hex(ThemeManager.IsDark ? "#382714" : "#FFF7ED");
                    hintPill.BorderBrush = ThemeManager.Hex(ThemeManager.IsDark ? "#784C1E" : "#FBD3A5");
                    hintLetter.Foreground = ThemeManager.Hex(ThemeManager.IsDark ? "#FBBF24" : "#B45309");
                    inputBox.BorderBrush = ThemeManager.Hex("#F59E0B");
                }
                else
                {
                    hintPill.SetResourceReference(Border.BackgroundProperty, "PillBgBrush");
                    hintPill.SetResourceReference(Border.BorderBrushProperty, "PillBorderBrush");
                    hintLetter.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
                    inputBox.ClearValue(Control.BorderBrushProperty);
                }
                hintPill.Visibility = Visibility.Visible;
            }

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var checkBtn = new Button { Content = "核对拼写", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0) };
            if (Application.Current.TryFindResource("AccentButton") is Style accentStyle) checkBtn.Style = accentStyle;
            var clueBtn = new Button { Content = "提示字母", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
            var revealBtn = new Button { Content = "直接看答案", Padding = new Thickness(12, 6, 12, 6) };

            void CompleteSpellingAndFlip()
            {
                CancelAutoFlip();
                if (revealed || flipping || swiping || !string.Equals(practice.Current, word, StringComparison.Ordinal)) return;
                spellingCorrect = true;
                spellingFeedback = "✓ 拼写正确";
                Flip();
            }

            void SubmitSpelling()
            {
                CancelAutoFlip();
                var typed = inputBox.Text.Trim();
                if (typed.Equals(word, StringComparison.OrdinalIgnoreCase))
                {
                    CompleteSpellingAndFlip();
                    return;
                }

                var (message, isProgress) = BuildSpellingHint(word, typed, ref hintLevel);
                ShowSpellingHint(message, isProgress ? "info" : "warn");
                inputBox.Focus();
                inputBox.CaretIndex = inputBox.Text.Length;
            }

            inputBox.TextChanged += (_, _) =>
            {
                CancelAutoFlip();
                if (revealed || flipping || swiping) return;
                var typed = inputBox.Text.Trim();
                if (typed.Equals(word, StringComparison.OrdinalIgnoreCase))
                {
                    ShowSpellingHint("✓ 拼写正确，自动翻面…", "success");
                    autoFlipTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
                    autoFlipTimer.Tick += (_, _) =>
                    {
                        CancelAutoFlip();
                        if (!dialog.IsVisible || revealed || flipping || swiping) return;
                        if (!string.Equals(practice.Current, word, StringComparison.Ordinal)) return;
                        if (inputBox.Text.Trim().Equals(word, StringComparison.OrdinalIgnoreCase))
                            CompleteSpellingAndFlip();
                    };
                    autoFlipTimer.Start();
                }
                else
                {
                    inputBox.ClearValue(Control.BorderBrushProperty);
                }
            };

            checkBtn.Click += (_, e) => { e.Handled = true; SubmitSpelling(); };
            clueBtn.Click += (_, e) =>
            {
                e.Handled = true;
                var maxReveal = Math.Max(1, word.Length - 1);
                hintLevel = Math.Min(maxReveal, hintLevel + 1);
                var mask = MaskSpellingWord(word, hintLevel);
                ShowSpellingHint($"提示：{mask}（共 {word.Length} 字母）", "info");
                inputBox.Focus();
                inputBox.CaretIndex = inputBox.Text.Length;
            };
            revealBtn.Click += (_, e) => { e.Handled = true; CancelAutoFlip(); spellingFeedback = ""; Flip(); };
            inputBox.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    SubmitSpelling();
                }
            };

            btnRow.Children.Add(checkBtn);
            btnRow.Children.Add(clueBtn);
            btnRow.Children.Add(revealBtn);
            face.Children.Add(btnRow);

            dialog.Dispatcher.BeginInvoke(new Action(() => inputBox.Focus()));
        }

        void RenderBackMnemonic(string word)
        {
            var stored = getMnemonics?.Invoke(word).FirstOrDefault();
            var noteText = stored?.Text ?? mnemonic(word);
            if (string.IsNullOrWhiteSpace(noteText)) return;

            var analysis = stored != null && mnemonicStore != null
                ? MnemonicParts.Analyze(word, stored.NoteType, stored.Text, (f, m, c) => mnemonicStore.FindMorphemeExamples(f, m, c))
                : MnemonicParts.Analyze(word, "词根词缀", noteText);

            var noteBox = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                Padding = new Thickness(12, 9, 12, 9),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1)
            };
            noteBox.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
            noteBox.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            if (analysis.Breakdowns.Count > 0)
            {
                var breakdown = analysis.Breakdowns[0];
                var bStack = new StackPanel();
                var chipsRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
                var detailText = new TextBlock { Text = breakdown.Explanation, FontSize = 13.5, LineHeight = 20, TextWrapping = TextWrapping.Wrap };
                detailText.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");

                var chipButtons = new Dictionary<int, (Button Button, MnemonicPart Part)>();
                var selectedChips = new HashSet<int>();

                void RenderChipSelection()
                {
                    var allLit = selectedChips.Count == 0 || selectedChips.Count == chipButtons.Count;
                    if (allLit)
                    {
                        detailText.Text = breakdown.Explanation;
                    }
                    else
                    {
                        var lines = selectedChips.OrderBy(idx => idx)
                            .Where(chipButtons.ContainsKey)
                            .Select(idx => chipButtons[idx].Part.FocusExplanation ?? $"{chipButtons[idx].Part.Form} · {chipButtons[idx].Part.Meaning}");
                        detailText.Text = string.Join("\n", lines);
                    }

                    foreach (var (pos, pair) in chipButtons)
                    {
                        var lit = allLit || selectedChips.Contains(pos);
                        var emphasized = !allLit && selectedChips.Contains(pos);
                        pair.Button.SetResourceReference(Control.BackgroundProperty, lit ? (emphasized ? "SelectedBgBrush" : "PillBgBrush") : "SurfaceBrush");
                        pair.Button.BorderBrush = ThemeManager.Hex(emphasized ? "#19ACF2" : lit ? "#38B8F6" : (ThemeManager.IsDark ? "#345068" : "#C6E4F7"));
                        pair.Button.BorderThickness = new Thickness(1.5);
                        pair.Button.Effect = lit ? new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = emphasized ? 0.22 : 0.13, Color = Color.FromRgb(18, 134, 196) } : null;
                    }
                }

                void ToggleChip(int pos)
                {
                    if (selectedChips.Count == 0 || selectedChips.Count == chipButtons.Count)
                    {
                        selectedChips.Clear();
                        selectedChips.Add(pos);
                    }
                    else if (selectedChips.Contains(pos))
                    {
                        if (selectedChips.Count == 1) selectedChips.Clear();
                        else selectedChips.Remove(pos);
                    }
                    else
                    {
                        selectedChips.Add(pos);
                        if (selectedChips.Count == chipButtons.Count) selectedChips.Clear();
                    }
                    RenderChipSelection();
                }

                for (var i = 0; i < breakdown.Parts.Count; i++)
                {
                    if (i > 0)
                    {
                        chipsRow.Children.Add(new TextBlock
                        {
                            Text = "＋", FontSize = 12.5, FontWeight = FontWeights.Bold,
                            Foreground = ThemeManager.Hex("#5BBDF4"), VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(5, 0, 5, 5)
                        });
                    }
                    var part = breakdown.Parts[i];
                    if (part.Meaning.Length == 0)
                    {
                        var staticChip = new Border
                        {
                            Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 0, 5),
                            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1.2),
                            Child = new TextBlock { Text = part.Form, FontSize = 13.5, FontWeight = FontWeights.Bold }
                        };
                        staticChip.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                        staticChip.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                        ((TextBlock)staticChip.Child).SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                        chipsRow.Children.Add(staticChip);
                        continue;
                    }

                    var idx = i;
                    var chipText = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    var formLbl = new TextBlock { Text = part.Form, FontSize = 13.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 5, 0) };
                    formLbl.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
                    chipText.Children.Add(formLbl);
                    var meanLbl = new TextBlock { Text = part.Meaning, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
                    meanLbl.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                    chipText.Children.Add(meanLbl);

                    var chipBtn = new Button
                    {
                        Content = chipText,
                        Padding = new Thickness(9, 4, 9, 4),
                        Margin = new Thickness(0, 0, 0, 5),
                        ToolTip = part.FocusExplanation ?? part.Meaning
                    };
                    chipBtn.Click += (_, e) => { e.Handled = true; ToggleChip(idx); };
                    chipButtons[idx] = (chipBtn, part);
                    chipsRow.Children.Add(chipBtn);
                }

                RenderChipSelection();

                var detailCard = new Border
                {
                    Padding = new Thickness(10, 7, 10, 7),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = "点击还原完整构词解释",
                    Child = detailText
                };
                detailCard.SetResourceReference(Border.BackgroundProperty, "AccentWashBrush");
                detailCard.SetResourceReference(Border.BorderBrushProperty, "PillBorderBrush");
                detailCard.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    if (selectedChips.Count > 0) { selectedChips.Clear(); RenderChipSelection(); }
                };

                bStack.Children.Add(chipsRow);
                bStack.Children.Add(detailCard);
                noteBox.Child = bStack;
                face.Children.Add(noteBox);
                return;
            }

            var noteRow = new Grid();
            noteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            noteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badge = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0),
                Child = new TextBlock { Text = "记", FontSize = 11.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            badge.SetResourceReference(Border.BackgroundProperty, "PillBgBrush");
            ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
            noteRow.Children.Add(badge);
            var aid = InputDialog.Text(noteText);
            aid.FontSize = 15;
            aid.LineHeight = 23;
            aid.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            Grid.SetColumn(aid, 1);
            noteRow.Children.Add(aid);
            noteBox.Child = noteRow;
            face.Children.Add(noteBox);
        }

        void LoadCurrent(bool animateEntrance = false)
        {
            CancelAutoFlip();
            revealed = false;
            spellingFeedback = "";
            spellingCorrect = false;
            entry = practice.Current is { } word ? dictionary.Lookup(word) : null;
            Render();
            if (animateEntrance)
                UiMotion.Reveal(card, distance: 10);
            if (practice.Current == null) next.Focus();
            else if (!spellingMode) card.Focus();
        }

        void TriggerAutoSpeakOnReveal()
        {
            var autoMode = autoSpeakCombo.SelectedValue as string ?? "off";
            if (revealed && practice.Current != null && autoMode is "en-GB" or "en-US")
                SpeakCurrentWord(autoMode == "en-GB" ? EnglishAccent.British : EnglishAccent.American);
        }

        void SnapshotFace()
        {
            var dpi = VisualTreeHelper.GetDpi(card);
            var width = Math.Max(1, (int)Math.Ceiling(card.ActualWidth * dpi.DpiScaleX));
            var height = Math.Max(1, (int)Math.Ceiling(card.ActualHeight * dpi.DpiScaleY));
            var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(card);
            bitmap.Freeze();
            image.ImageSource = bitmap;
            var aspect = card.ActualHeight / Math.Max(1, card.ActualWidth);
            surface.Positions = new Point3DCollection([new(-1, -aspect, 0), new(1, -aspect, 0), new(1, aspect, 0), new(-1, aspect, 0)]);
        }

        void Flip()
        {
            CancelAutoFlip();
            if (practice.Current == null || flipping || swiping) return;
            void SwitchFace(bool animate)
            {
                revealed = !revealed;
                Render(animateActions: animate && revealed);
                if (revealed) TriggerAutoSpeakOnReveal();
            }
            if (!UiMotion.Enabled) { SwitchFace(false); return; }
            flipping = true;
            var restoreFocus = card.IsKeyboardFocusWithin && activeSpellingBox == null;
            void Finish()
            {
                viewport.Visibility = Visibility.Hidden;
                image.ImageSource = null;
                card.Visibility = Visibility.Visible;
                flipping = false;
                if (revealed) UiMotion.Reveal(actions, distance: 6);
                if (restoreFocus && dialog.IsVisible) card.Focus();
            }

            SnapshotFace();
            card.Visibility = Visibility.Hidden;
            viewport.Visibility = Visibility.Visible;
            var frontTurn = new DoubleAnimation(0, 90, TimeSpan.FromMilliseconds(125))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            frontTurn.Completed += (_, _) =>
            {
                if (!dialog.IsVisible) return;
                card.Visibility = Visibility.Visible;
                SwitchFace(false);
                card.UpdateLayout();
                SnapshotFace();
                card.Visibility = Visibility.Hidden;
                var backTurn = new DoubleAnimation(-90, 0, TimeSpan.FromMilliseconds(165))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                backTurn.Completed += (_, _) => Finish();
                turn.BeginAnimation(AxisAngleRotation3D.AngleProperty, backTurn);
            };
            turn.BeginAnimation(AxisAngleRotation3D.AngleProperty, frontTurn);
        }

        void Answer(FlashcardAnswer answer)
        {
            if (!revealed || flipping || swiping || practice.Current == null) return;
            swiping = true;
            var toX = answer == FlashcardAnswer.Forgotten ? -115.0 : 115.0;
            var tilt = answer == FlashcardAnswer.Forgotten ? -6.5 : 6.5;
            UiMotion.SwipeOut(card, toX, tilt, 190, () =>
            {
                swiping = false;
                if (!dialog.IsVisible) return;
                practice.Answer(answer);
                Persist();
                LoadCurrent(animateEntrance: true);
            });
        }

        void ResetProgress(bool requireConfirm)
        {
            if (flipping || swiping) return;
            if (requireConfirm && !InputDialog.Confirm(dialog, "重置自测进度",
                "将清除本地保存的自测进度，并从第 1 组重新开始。", "重置"))
                return;
            practice.Reset(freshWords);
            if (persistProgress) onResetProgress?.Invoke();
            LoadCurrent(animateEntrance: true);
        }

        flipModeBtn.Click += (_, _) =>
        {
            if (!spellingMode) return;
            CancelAutoFlip();
            spellingMode = false;
            revealed = false;
            spellingFeedback = "";
            UpdateModeButtons();
            Render();
        };
        spellModeBtn.Click += (_, _) =>
        {
            if (spellingMode) return;
            CancelAutoFlip();
            spellingMode = true;
            revealed = false;
            spellingFeedback = "";
            UpdateModeButtons();
            Render();
        };
        autoSpeakCombo.SelectionChanged += (_, _) =>
        {
            if (preferences != null && autoSpeakCombo.SelectedValue is string val)
            {
                preferences.FlashcardAutoSpeak = val;
                preferences.Save();
            }
        };
        speakNowBtn.Click += (_, _) => SpeakCurrentWord();

        card.Click += (_, e) =>
        {
            if (spellingMode && !revealed) return;
            Flip();
        };
        flipCard.Click += (_, _) => Flip();
        known.Click += (_, _) => Answer(FlashcardAnswer.Known);
        vague.Click += (_, _) => Answer(FlashcardAnswer.Vague);
        forgotten.Click += (_, _) => Answer(FlashcardAnswer.Forgotten);
        resetButton.Click += (_, _) => ResetProgress(requireConfirm: true);
        restart.Click += (_, _) => ResetProgress(requireConfirm: false);
        next.Click += (_, _) =>
        {
            if (practice.StartNextGroup())
            {
                Persist();
                LoadCurrent(animateEntrance: true);
            }
            else dialog.Close();
        };
        dialog.PreviewKeyDown += (_, args) =>
        {
            if (activeSpellingBox != null && activeSpellingBox.IsKeyboardFocusWithin && !revealed) return;
            if (args.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None && (card.IsKeyboardFocusWithin || !revealed))
            {
                args.Handled = true;
                if (!args.IsRepeat) Flip();
                return;
            }
            var answer = args.Key switch
            {
                Key.D1 or Key.NumPad1 => FlashcardAnswer.Known,
                Key.D2 or Key.NumPad2 => FlashcardAnswer.Vague,
                Key.D3 or Key.NumPad3 => FlashcardAnswer.Forgotten,
                _ => (FlashcardAnswer?)null
            };
            if (answer is { } choice && Keyboard.Modifiers == ModifierKeys.None)
            {
                args.Handled = true;
                if (!args.IsRepeat) Answer(choice);
            }
        };
        dialog.Closed += (_, _) => CancelAutoFlip();
        dialog.Loaded += (_, _) => LoadCurrent(animateEntrance: true);
        return dialog;
    }

    internal static string MaskSpellingWord(string word, int revealCount)
    {
        if (string.IsNullOrEmpty(word)) return "";
        var limit = Math.Clamp(revealCount, 1, Math.Max(1, word.Length - 1));
        var chars = new char[word.Length];
        for (var i = 0; i < word.Length; i++)
            chars[i] = i < limit || !char.IsLetter(word[i]) ? word[i] : '_';
        return new string(chars);
    }

    internal static int CommonPrefixLength(string target, string typed)
    {
        var max = Math.Min(target.Length, typed.Length);
        var matched = 0;
        while (matched < max && char.ToLowerInvariant(target[matched]) == char.ToLowerInvariant(typed[matched]))
            matched++;
        return matched;
    }

    internal static (string Message, bool IsProgress) BuildSpellingHint(string word, string typed, ref int hintLevel)
    {
        var maxReveal = Math.Max(1, word.Length - 1);
        if (typed.Length == 0)
        {
            hintLevel = Math.Min(maxReveal, hintLevel + 1);
            return ($"还没输入哦 · 提示：{MaskSpellingWord(word, hintLevel)}（共 {word.Length} 字母），先试着想想", true);
        }

        var matched = CommonPrefixLength(word, typed);
        if (typed.Length < word.Length && matched == typed.Length)
        {
            var remaining = word.Length - typed.Length;
            if (remaining == 1)
                return ($"前 {matched} 位全对！只差最后 1 个字母（共 {word.Length} 字母），再想想～", true);
            hintLevel = Math.Min(maxReveal, Math.Max(hintLevel + 1, matched + 1));
            return ($"前 {matched} 位全对，还差 {remaining} 个字母 · 提示：{MaskSpellingWord(word, hintLevel)}", true);
        }

        if (typed.Length > word.Length && matched == word.Length)
            return ($"前面 {word.Length} 位已经拼对啦，末尾多输入了 {typed.Length - word.Length} 个字母，删掉再试试", false);

        if (matched > 0)
        {
            if (matched >= word.Length - 1)
                return ($"前 {matched} 位都对啦，只差最后 1 位有误（共 {word.Length} 字母），再想想～", false);
            hintLevel = Math.Min(maxReveal, Math.Max(hintLevel + 1, matched + 1));
            return ($"前 {matched} 位正确，第 {matched + 1} 位有误 · 提示：{MaskSpellingWord(word, hintLevel)}（共 {word.Length} 字母）", false);
        }

        hintLevel = Math.Min(maxReveal, Math.Max(1, hintLevel + 1));
        return ($"第 1 位有误 · 提示：{MaskSpellingWord(word, hintLevel)}（共 {word.Length} 字母），再想想看", false);
    }

    private sealed record AutoSpeakChoice(string Key, string Label);
}
