using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using MemoLex.Core;
using MemoLex.Services;

namespace MemoLex.Desktop;

internal static class StubbornFlashcards
{
    internal static Window Build(Window owner, IEnumerable<string> words, LocalDictionary dictionary,
        OnlineDictionaryClient? online, Func<string, string?> mnemonic)
    {
        var practice = new FlashcardPractice(words);
        var dialog = InputDialog.Shell(owner, "顽固单词 · 卡片自测", out var body);
        dialog.Width = 570;
        var progress = InputDialog.Text("");
        body.Children.Add(progress);
        var face = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var card = new Button
        {
            Content = new ScrollViewer
            {
                Content = face, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 193, Focusable = false
            },
            MinHeight = 244, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(22, 13, 22, 13)
        };
        var stage = new Grid{Margin=new Thickness(0,12,0,12),ClipToBounds=true};
        stage.Children.Add(card);
        var turn = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        var image = new ImageBrush();
        var surface = new MeshGeometry3D
        {
            TriangleIndices = new Int32Collection([0,1,2,0,2,3]),
            TextureCoordinates = new PointCollection([new(0,1),new(1,1),new(1,0),new(0,0)])
        };
        var viewport = new Viewport3D
        {
            Camera = new PerspectiveCamera(new Point3D(0,0,2.42),new Vector3D(0,0,-1),new Vector3D(0,1,0),45),
            IsHitTestVisible = false, Visibility = Visibility.Hidden
        };
        viewport.Children.Add(new ModelVisual3D
        {
            Content=new GeometryModel3D(surface,new EmissiveMaterial(image)){ Transform=new RotateTransform3D(turn) }
        });
        stage.Children.Add(viewport);
        body.Children.Add(stage);
        var actions = new UniformGrid { Rows = 1 };
        body.Children.Add(actions);
        Button Action(string label)
        {
            var button = new Button { Content = label, Margin = new Thickness(3, 0, 3, 0) };
            actions.Children.Add(button);
            return button;
        }
        var known = Action("认识");
        known.Style = (Style)Application.Current.FindResource("AccentButton");
        var vague = Action("模糊");
        var forgotten = Action("忘记");
        var flipCard = Action("翻转卡片");
        var next = new Button { HorizontalAlignment = HorizontalAlignment.Right };
        body.Children.Add(next);
        var hint = InputDialog.Text("练习结果仅记录在本组自测中");
        hint.Margin = new Thickness(0, 9, 0, 0);
        body.Children.Add(hint);
        dialog.RegisterName("Flashcard", card);
        dialog.RegisterName("FlashcardProgress", progress);
        dialog.RegisterName("FlashcardKnown", known);
        dialog.RegisterName("FlashcardVague", vague);
        dialog.RegisterName("FlashcardForgotten", forgotten);
        dialog.RegisterName("FlashcardFlipButton", flipCard);
        dialog.RegisterName("FlashcardNextGroup", next);
        var revealed = false;
        var flipping = false;
        DictionaryEntry? entry = null;

        void Render()
        {
            var word = practice.Current;
            progress.Text = $"第 {practice.GroupNumber} / {practice.GroupCount} 组 · 剩余 {practice.Remaining} / {practice.GroupWordCount} 词";
            actions.Visibility = word == null ? Visibility.Collapsed : Visibility.Visible;
            foreach (var button in new[] { known, vague, forgotten }) button.IsEnabled = word != null && revealed;
            flipCard.Content = revealed ? "翻回单词" : "翻转卡片";
            next.Visibility = word == null ? Visibility.Visible : Visibility.Collapsed;
            next.Content = practice.HasNextGroup ? "下一组" : "完成";
            card.IsHitTestVisible = card.Focusable = word != null;
            AutomationProperties.SetName(card, word == null ? "本组已完成" : revealed ? $"{word}，点击隐藏释义" : $"{word}，点击查看释义");
            face.Children.Clear();
            face.Children.Add(new TextBlock
            {
                Text = word ?? (practice.HasNextGroup ? "本组已完成" : "全部练习完成"),
                FontSize = word == null ? 25 : 32, FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
            });
            if (word == null) return;
            if (!string.IsNullOrWhiteSpace(entry?.phonetic))
            {
                var phonetic = InputDialog.Text(entry.phonetic);
                phonetic.TextAlignment = TextAlignment.Center;
                face.Children.Add(phonetic);
            }
            if (!revealed)
            {
                var cue = InputDialog.Text("点击翻卡核对释义");
                cue.TextAlignment = TextAlignment.Center;
                cue.Margin = new Thickness(0, 14, 0, 0);
                face.Children.Add(cue);
                return;
            }
            void Label(string label)
            {
                face.Children.Add(new TextBlock{Text=label,FontSize=12,FontWeight=FontWeights.SemiBold,
                    Foreground=(Brush)Application.Current.FindResource("AccentDeepBrush"),Margin=new Thickness(0,7,0,0)});
            }
            Label("中文释义");
            var definition = new WordListMeaningText
            {
                DataContext = new WordListRow(word, entry?.DisplayMeaning ?? "", online),
                FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
            };
            definition.SetBinding(TextBlock.TextProperty, new Binding(nameof(WordListRow.DisplayMeaning)));
            face.Children.Add(definition);
            if (mnemonic(word) is { Length: > 0 } note)
            {
                Label("助记");
                var aid = InputDialog.Text(note);
                aid.Margin = new Thickness(0, 2, 0, 0);
                face.Children.Add(aid);
            }
        }

        void LoadCurrent()
        {
            revealed = false;
            entry = practice.Current is { } word ? dictionary.Lookup(word) : null;
            Render();
            if (practice.Current == null) next.Focus();
            else card.Focus();
        }

        void SnapshotFace()
        {
            var dpi = VisualTreeHelper.GetDpi(card);
            var width = Math.Max(1,(int)Math.Ceiling(card.ActualWidth*dpi.DpiScaleX));
            var height = Math.Max(1,(int)Math.Ceiling(card.ActualHeight*dpi.DpiScaleY));
            var bitmap = new RenderTargetBitmap(width,height,dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);
            bitmap.Render(card);
            bitmap.Freeze();
            image.ImageSource=bitmap;
            var aspect=card.ActualHeight/Math.Max(1,card.ActualWidth);
            surface.Positions=new Point3DCollection([new(-1,-aspect,0),new(1,-aspect,0),new(1,aspect,0),new(-1,aspect,0)]);
        }

        void Flip()
        {
            if (practice.Current == null || flipping) return;
            void SwitchFace() { revealed = !revealed; Render(); }
            if (!SystemParameters.ClientAreaAnimation) { SwitchFace(); return; }
            flipping = true;
            var restoreFocus=card.IsKeyboardFocusWithin;
            void Finish()
            {
                viewport.Visibility=Visibility.Hidden;
                image.ImageSource=null;
                card.Visibility=Visibility.Visible;
                flipping=false;
                if(restoreFocus&&dialog.IsVisible)card.Focus();
            }

            SnapshotFace();
            card.Visibility=Visibility.Hidden;
            viewport.Visibility=Visibility.Visible;
            var frontTurn=new DoubleAnimation(0,90,TimeSpan.FromMilliseconds(120))
                {EasingFunction=new CubicEase{EasingMode=EasingMode.EaseIn}};
            frontTurn.Completed+=(_,_) =>
            {
                if (!dialog.IsVisible) return;
                card.Visibility=Visibility.Visible;
                SwitchFace();
                card.UpdateLayout();
                SnapshotFace();
                card.Visibility=Visibility.Hidden;
                var backTurn=new DoubleAnimation(-90,0,TimeSpan.FromMilliseconds(155))
                    {EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
                backTurn.Completed+=(_,_)=>Finish();
                turn.BeginAnimation(AxisAngleRotation3D.AngleProperty,backTurn);
            };
            turn.BeginAnimation(AxisAngleRotation3D.AngleProperty,frontTurn);
        }

        void Answer(FlashcardAnswer answer)
        {
            if (!revealed || flipping || practice.Current == null) return;
            practice.Answer(answer);
            LoadCurrent();
        }
        card.Click += (_, _) => Flip();
        flipCard.Click += (_, _) => Flip();
        known.Click += (_, _) => Answer(FlashcardAnswer.Known);
        vague.Click += (_, _) => Answer(FlashcardAnswer.Vague);
        forgotten.Click += (_, _) => Answer(FlashcardAnswer.Forgotten);
        next.Click += (_, _) => { if (practice.StartNextGroup()) LoadCurrent(); else dialog.Close(); };
        dialog.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None && card.IsKeyboardFocusWithin)
            {
                args.Handled=true;
                if(!args.IsRepeat)Flip();
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
        dialog.Loaded += (_, _) => LoadCurrent();
        return dialog;
    }
}
