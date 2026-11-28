using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MemoLex.Core;

namespace MemoLex;

internal sealed record ContextSentenceDraft(
    string Word,
    string Sentence,
    string Translation,
    string Source,
    IReadOnlyList<string> Tags);

internal static class ContextSentenceEditor
{
    private static readonly string[] QuickSources = ["自建例句", "真题例句", "段落翻译", "阅读摘录"];
    private static readonly string[] QuickTags = ["考研", "四级", "六级", "高考", "雅思", "托福"];

    internal static Window Build(
        Window owner,
        string word,
        ContextSentenceItem? original,
        bool canSync,
        Func<ContextSentenceDraft, bool, Task> save,
        Func<Task<bool>>? delete = null,
        bool allowEditWord = false)
    {
        var isEdit = original != null;
        var initialWord = (original?.Word ?? word ?? "").Trim();
        var titleWord = initialWord.Length > 0 ? " · " + initialWord : "";
        var dialog = InputDialog.Shell(owner, (isEdit ? "编辑例句" : "添加例句") + titleWord, out _);
        dialog.Width = 660;
        dialog.Height = allowEditWord && !isEdit ? 625 : 575;
        dialog.MinWidth = 540;
        dialog.MinHeight = 500;
        dialog.SizeToContent = SizeToContent.Manual;

        var body = new Grid { Margin = new Thickness(24, 16, 24, 20) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: status banner
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: optional word input
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.15, GridUnitType.Star) }); // 2: sentence
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.85, GridUnitType.Star) }); // 3: translation
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: source & tags
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5: status text
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 6: buttons

        var pillStyle = Application.Current?.TryFindResource("PillButton") as Style;
        var accentStyle = Application.Current?.TryFindResource("AccentButton") as Style;

        // 0. Banner
        var bannerRow = new DockPanel();
        var badgeText = original switch
        {
            null => "新建例句",
            { IsCloud: true } => "云端例句",
            { HasUnsyncedCloudEdit: true } => "待同步修改",
            _ => "本地例句"
        };
        var badge = new Border
        {
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = badgeText, FontSize = 11, FontWeight = FontWeights.SemiBold }
        };
        if (original is { HasUnsyncedCloudEdit: true })
        {
            badge.SetResourceReference(Border.BackgroundProperty, "WarnBgBrush");
            badge.SetResourceReference(Border.BorderBrushProperty, "WarnBorderBrush");
            ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, "WarnFgBrush");
        }
        else
        {
            badge.SetResourceReference(Border.BackgroundProperty, "PillBgBrush");
            badge.SetResourceReference(Border.BorderBrushProperty, "PillBorderBrush");
            ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, "AccentDeepBrush");
        }
        DockPanel.SetDock(badge, Dock.Left);
        bannerRow.Children.Add(badge);

        var bannerTip = new TextBlock
        {
            Text = original switch
            {
                { IsCloud: true } => "已与墨墨云端同步，点击「同步到墨墨」会直接更新云端内容。",
                { HasUnsyncedCloudEdit: true } => "本地修改尚未同步，点击「同步到墨墨」即可更新云端例句。",
                _ => canSync
                    ? "支持保存到本地离线自测，或一键同步到墨墨云端（手机端同步可见）。"
                    : "当前未登录墨墨账号，可先保存到本地，登录后随时同步到墨墨云端。"
            },
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        bannerTip.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        bannerRow.Children.Add(bannerTip);

        var bannerCard = new Border
        {
            Child = bannerRow,
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1)
        };
        bannerCard.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
        bannerCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        body.Children.Add(bannerCard);

        // 1. Optional word input (when adding from My Sentences dialog)
        TextBox? wordBox = null;
        if (allowEditWord && !isEdit)
        {
            var wordRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            wordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            wordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var wordLabel = new TextBlock { Text = "所属单词 *", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            wordLabel.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
            wordRow.Children.Add(wordLabel);

            wordBox = new TextBox
            {
                Text = initialWord,
                MaxLength = 100,
                FontSize = 14,
                Height = 32,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                Padding = new Thickness(9, 2, 9, 2),
                ToolTip = "填写该例句对应的英文单词或短语"
            };
            Grid.SetColumn(wordBox, 1);
            wordRow.Children.Add(wordBox);
            Grid.SetRow(wordRow, 1);
            body.Children.Add(wordRow);
            dialog.RegisterName("ContextWordText", wordBox);
        }

        // 2. English sentence
        var sentenceGroup = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        sentenceGroup.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sentenceGroup.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var sentenceLabel = new TextBlock { Text = "英文例句 *", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(1, 0, 0, 5) };
        sentenceLabel.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        sentenceGroup.Children.Add(sentenceLabel);

        var sentenceBox = new TextBox
        {
            Text = original?.Sentence ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxLength = 2000,
            FontSize = 14.5,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            Padding = new Thickness(10, 8, 10, 8)
        };
        Grid.SetRow(sentenceBox, 1);
        sentenceGroup.Children.Add(sentenceBox);
        Grid.SetRow(sentenceGroup, 2);
        body.Children.Add(sentenceGroup);
        dialog.RegisterName("ContextSentenceText", sentenceBox);

        // 3. Chinese translation
        var translationGroup = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        translationGroup.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        translationGroup.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var translationLabel = new TextBlock { Text = "中文译文", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(1, 0, 0, 5) };
        translationLabel.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        translationGroup.Children.Add(translationLabel);

        var translationBox = new TextBox
        {
            Text = original?.Translation ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxLength = 2000,
            FontSize = 13.5,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            Padding = new Thickness(10, 8, 10, 8)
        };
        Grid.SetRow(translationBox, 1);
        translationGroup.Children.Add(translationBox);
        Grid.SetRow(translationGroup, 3);
        body.Children.Add(translationGroup);
        dialog.RegisterName("ContextTranslationText", translationBox);

        // 3. Source & tags metadata card
        var metaStack = new StackPanel();

        var sourceRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
        sourceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sourceLabel = new TextBlock { Text = "来源", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        sourceLabel.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        sourceRow.Children.Add(sourceLabel);

        var sourceBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(original?.Source) ? "自建例句" : original.Source,
            MaxLength = 100,
            FontSize = 12.5,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(sourceBox, 1);
        sourceRow.Children.Add(sourceBox);
        dialog.RegisterName("ContextSourceText", sourceBox);

        var sourcePills = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var preset in QuickSources)
        {
            var value = preset;
            var btn = new Button
            {
                Content = value,
                FontSize = 11,
                Padding = new Thickness(8, 2.5, 8, 2.5),
                Margin = new Thickness(0, 2, 5, 2)
            };
            if (pillStyle != null) btn.Style = pillStyle;
            btn.Click += (_, _) => sourceBox.Text = value;
            sourcePills.Children.Add(btn);
        }
        Grid.SetColumn(sourcePills, 2);
        sourceRow.Children.Add(sourcePills);
        metaStack.Children.Add(sourceRow);

        var tagsRow = new Grid();
        tagsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        tagsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
        tagsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tagsLabel = new TextBlock { Text = "标签", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        tagsLabel.SetResourceReference(TextBlock.ForegroundProperty, "InkStrongBrush");
        tagsRow.Children.Add(tagsLabel);

        var tagsBox = new TextBox
        {
            Text = original?.Tags is { Count: > 0 } existingTags ? string.Join("，", existingTags) : "",
            MaxLength = 200,
            FontSize = 12.5,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "多个标签可用逗号或空格分隔"
        };
        Grid.SetColumn(tagsBox, 1);
        tagsRow.Children.Add(tagsBox);
        dialog.RegisterName("ContextTagsText", tagsBox);

        var tagPills = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var preset in QuickTags)
        {
            var tagValue = preset;
            var btn = new Button
            {
                Content = tagValue,
                FontSize = 11,
                Padding = new Thickness(8, 2.5, 8, 2.5),
                Margin = new Thickness(0, 2, 5, 2)
            };
            if (pillStyle != null) btn.Style = pillStyle;
            btn.Click += (_, _) =>
            {
                var current = ContextSentenceHelper.ParseTags(tagsBox.Text).ToList();
                var idx = current.FindIndex(t => t.Equals(tagValue, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) current.RemoveAt(idx);
                else if (current.Count < 10) current.Add(tagValue);
                tagsBox.Text = string.Join("，", current);
            };
            tagPills.Children.Add(btn);
        }
        Grid.SetColumn(tagPills, 2);
        tagsRow.Children.Add(tagPills);
        metaStack.Children.Add(tagsRow);

        var metaCard = new Border
        {
            Child = metaStack,
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1)
        };
        metaCard.SetResourceReference(Border.BackgroundProperty, "CardSoftBrush");
        metaCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Grid.SetRow(metaCard, 4);
        body.Children.Add(metaCard);

        // 5. Status text
        var status = InputDialog.Text("多个标签可用逗号或空格分隔；本地保存支持离线自测。", status: true);
        status.Margin = new Thickness(0, 10, 0, 12);
        status.FontSize = 12;
        Grid.SetRow(status, 5);
        body.Children.Add(status);

        // 5. Action bar
        var busy = false;
        dialog.Closing += (_, e) => { if (busy) e.Cancel = true; };

        async Task RunBusyAsync(Func<Task> action)
        {
            if (busy) return;
            busy = true;
            body.IsEnabled = false;
            try
            {
                await action();
            }
            catch (Exception error)
            {
                status.Text = error is Services.MaiMemoException or ArgumentException or System.IO.IOException
                    ? error.Message
                    : "操作未完成，请稍后重试。";
            }
            finally
            {
                busy = false;
                body.IsEnabled = true;
            }
        }

        var footer = new DockPanel();
        if (isEdit && delete != null)
        {
            var deleteBtn = new Button
            {
                Content = original!.IsCloud ? "删除云端与本地例句" : "删除例句",
                Padding = new Thickness(12, 6, 12, 6),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(202, 58, 74)),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(deleteBtn, Dock.Left);
            footer.Children.Add(deleteBtn);
            dialog.RegisterName("DeleteContextSentence", deleteBtn);
            deleteBtn.Click += async (_, _) =>
            {
                await RunBusyAsync(async () =>
                {
                    status.Text = original.IsCloud ? "正在删除例句…" : "正在移除例句…";
                    var deleted = await delete();
                    if (deleted) dialog.Close();
                    else status.Text = "已取消删除。";
                });
            };
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6) };
        cancel.Click += (_, _) => dialog.Close();
        buttons.Children.Add(cancel);

        var local = new Button
        {
            Content = "保存到本地",
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(14, 6, 14, 6)
        };
        if (pillStyle != null) local.Style = pillStyle;
        buttons.Children.Add(local);
        dialog.RegisterName("SaveContextLocal", local);

        var cloud = new Button
        {
            Content = "同步到墨墨",
            Padding = new Thickness(16, 6, 16, 6),
            IsEnabled = canSync,
            ToolTip = canSync ? "保存到本地并同步到你的墨墨账号" : "请先在设置中登录墨墨账号"
        };
        if (accentStyle != null) cloud.Style = accentStyle;
        buttons.Children.Add(cloud);
        dialog.RegisterName("SaveContextCloud", cloud);

        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 6);
        body.Children.Add(footer);

        InputDialog.SetBody(dialog, body);

        async Task SaveAsync(bool sync)
        {
            var resolvedWord = (wordBox?.Text ?? initialWord).Trim();
            if (resolvedWord.Length == 0 || resolvedWord.Length > 100)
            {
                status.Text = "请先填写有效的英文单词。";
                wordBox?.Focus();
                return;
            }
            var sentence = sentenceBox.Text.Trim();
            if (sentence.Length == 0)
            {
                status.Text = "请先填写英文例句内容。";
                sentenceBox.Focus();
                return;
            }
            var translation = translationBox.Text.Trim();
            var source = string.IsNullOrWhiteSpace(sourceBox.Text) ? "自建例句" : sourceBox.Text.Trim();
            var tags = ContextSentenceHelper.ParseTags(tagsBox.Text);
            var draft = new ContextSentenceDraft(resolvedWord, sentence, translation, source, tags);

            await RunBusyAsync(async () =>
            {
                status.Text = sync ? "正在保存并同步到墨墨…" : "正在保存到本地…";
                await save(draft, sync);
                dialog.Close();
            });
        }

        local.Click += async (_, _) => await SaveAsync(false);
        cloud.Click += async (_, _) => await SaveAsync(true);
        dialog.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                e.Handled = true;
                await SaveAsync(canSync);
            }
        };
        dialog.Loaded += (_, _) =>
        {
            if (wordBox != null && string.IsNullOrWhiteSpace(wordBox.Text))
            {
                wordBox.Focus();
            }
            else
            {
                sentenceBox.Focus();
                sentenceBox.CaretIndex = sentenceBox.Text.Length;
            }
        };
        return dialog;
    }
}
