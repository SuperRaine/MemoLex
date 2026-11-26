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
        bool persistProgress = true)
    {
        var freshWords = words.ToArray();
        var practice = new FlashcardPractice(freshWords, persistProgress ? savedProgress : null);
        var dialog = InputDialog.Shell(owner, "顽固单词 · 卡片自测", out var body);
        dialog.Width = 640;
        dialog.MaxHeight = Math.Max(520, SystemParameters.WorkArea.Height - 60);

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
        progress.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0x6A, 0x82));
        progress.VerticalAlignment = VerticalAlignment.Center;
        progress.Margin = new Thickness(0, 0, 10, 0);
        headerRow.Children.Add(progress);
        body.Children.Add(headerRow);

        var progressTrack = new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb(0xE2, 0xF0, 0xFA)),
            Margin = new Thickness(0, 8, 0, 2),
            ClipToBounds = true
        };
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
            MaxHeight = 380,
            Focusable = false
        };
        var cardBg = new LinearGradientBrush(
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xF1, 0xF9, 0xFF),
            new Point(0, 0), new Point(1, 1));
        var card = new Button
        {
            Content = cardScroll,
            Background = cardBg,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xE2, 0xF6)),
            MinHeight = 320,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(26, 20, 26, 20),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Opacity = 0.08, Color = Color.FromRgb(0x10, 0x42, 0x66) }
        };

        var stackBack2 = new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xFA, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDF, 0xEE, 0xF8)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(22, 14, 22, -10),
            Opacity = 0.65,
            IsHitTestVisible = false
        };
        var stackBack1 = new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFC, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0xE8, 0xF7)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(11, 7, 11, -5),
            Opacity = 0.88,
            IsHitTestVisible = false
        };

        var stage = new Grid { Margin = new Thickness(0, 12, 0, 18), ClipToBounds = false };
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
        var preFlipHint = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(14, 6, 14, 6),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF9, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCE, 0xE6, 0xF7)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "先点击卡片翻面核对释义（空格键），再选择掌握程度",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5E, 0x7D, 0x94))
            }
        };
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
        var flipCard = new Button
        {
            Content = "翻转卡片",
            Visibility = Visibility.Collapsed,
            Focusable = false
        };
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
        var restart = new Button
        {
            Content = "重新开始",
            Margin = new Thickness(0, 0, 8, 0),
            Visibility = Visibility.Collapsed
        };
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
        DictionaryEntry? entry = null;

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
            var word = practice.Current;
            progress.Text = $"第 {practice.GroupNumber} / {practice.GroupCount} 组 · 剩余 {practice.Remaining} / {practice.GroupWordCount} 词 · 已掌握 {practice.MasteredCount} / {practice.TotalWordCount}";
            resetButton.IsEnabled = practice.HasProgress || (word == null && !practice.HasNextGroup);
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
                face.Children.Add(new TextBlock
                {
                    Text = practice.HasNextGroup ? "本组已完成" : "全部练习完成",
                    FontSize = 26,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x22, 0x34)),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }
            if (!revealed)
            {
                face.Children.Add(new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 12),
                    Padding = new Thickness(11, 3.5, 11, 3.5),
                    CornerRadius = new CornerRadius(11),
                    Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF6, 0xFF)),
                    Child = new TextBlock
                    {
                        Text = $"第 {practice.GroupNumber} 组 · 点击翻面",
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x0A, 0x7F, 0xB3))
                    }
                });
                face.Children.Add(new TextBlock
                {
                    Text = word,
                    FontSize = 44,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x22, 0x34)),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                });
                if (!string.IsNullOrWhiteSpace(entry?.phonetic))
                {
                    var phonetic = InputDialog.Text($"/{entry.phonetic.Trim('/')}/");
                    phonetic.FontSize = 16;
                    phonetic.TextAlignment = TextAlignment.Center;
                    phonetic.Margin = new Thickness(0, 6, 0, 0);
                    face.Children.Add(phonetic);
                }
                var cuePill = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0),
                    Padding = new Thickness(14, 6, 14, 6),
                    CornerRadius = new CornerRadius(13),
                    Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xF5, 0xFF)),
                    Child = new TextBlock
                    {
                        Text = "↻ 点击翻卡核对释义",
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)Application.Current.FindResource("AccentDeepBrush")
                    }
                };
                face.Children.Add(cuePill);
                return;
            }

            // Back face: compact header row so definition + mnemonic both fit without scrolling
            var backHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var backPill = new Border
            {
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 3, 10, 3),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF6, 0xFF)),
                Child = new TextBlock
                {
                    Text = "点击翻回",
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x0A, 0x7F, 0xB3))
                }
            };
            DockPanel.SetDock(backPill, Dock.Right);
            backHeader.Children.Add(backPill);

            var wordRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            wordRow.Children.Add(new TextBlock
            {
                Text = word,
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x22, 0x34)),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 10, 0)
            });
            if (!string.IsNullOrWhiteSpace(entry?.phonetic))
            {
                var phonetic = InputDialog.Text($"/{entry.phonetic.Trim('/')}/");
                phonetic.FontSize = 15;
                phonetic.VerticalAlignment = VerticalAlignment.Bottom;
                phonetic.Margin = new Thickness(0, 0, 0, 3);
                wordRow.Children.Add(phonetic);
            }
            backHeader.Children.Add(wordRow);
            face.Children.Add(backHeader);

            face.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xDC, 0xEC, 0xF7)), Margin = new Thickness(0, 0, 0, 12) });

            face.Children.Add(new TextBlock
            {
                Text = "中文释义",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("AccentDeepBrush")
            });
            var definition = new WordListMeaningText
            {
                DataContext = new WordListRow(word, entry?.DisplayMeaning ?? "", online),
                FontSize = 18.5,
                LineHeight = 28,
                Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x2F, 0x44)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            definition.SetBinding(TextBlock.TextProperty, new Binding(nameof(WordListRow.DisplayMeaning)));
            face.Children.Add(definition);
            if (mnemonic(word) is { Length: > 0 } note)
            {
                var noteBox = new Border
                {
                    Margin = new Thickness(0, 14, 0, 0),
                    Padding = new Thickness(13, 10, 13, 10),
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF8, 0xFF)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xE7, 0xF8)),
                    BorderThickness = new Thickness(1)
                };
                var noteRow = new Grid();
                noteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                noteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var badge = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromRgb(0xD8, 0xF0, 0xFE)),
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0),
                    Child = new TextBlock { Text = "记", FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = (Brush)Application.Current.FindResource("AccentDeepBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                noteRow.Children.Add(badge);
                var aid = InputDialog.Text(note);
                aid.FontSize = 15;
                aid.LineHeight = 23;
                aid.Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x4F, 0x70));
                Grid.SetColumn(aid, 1);
                noteRow.Children.Add(aid);
                noteBox.Child = noteRow;
                face.Children.Add(noteBox);
            }
        }

        void LoadCurrent(bool animateEntrance = false)
        {
            revealed = false;
            entry = practice.Current is { } word ? dictionary.Lookup(word) : null;
            Render();
            if (animateEntrance)
                UiMotion.Reveal(card, distance: 10);
            if (practice.Current == null) next.Focus();
            else card.Focus();
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
            if (practice.Current == null || flipping || swiping) return;
            void SwitchFace(bool animate) { revealed = !revealed; Render(animateActions: animate && revealed); }
            if (!UiMotion.Enabled) { SwitchFace(false); return; }
            flipping = true;
            var restoreFocus = card.IsKeyboardFocusWithin;
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

        card.Click += (_, _) => Flip();
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
        dialog.Loaded += (_, _) => LoadCurrent(animateEntrance: true);
        return dialog;
    }
}
