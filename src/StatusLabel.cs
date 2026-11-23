using System.Windows;
using System.Windows.Controls;

namespace MemoLex;

public sealed class StatusLabel : TextBlock
{
    static StatusLabel() => TextProperty.OverrideMetadata(typeof(StatusLabel),
        new FrameworkPropertyMetadata("", null, (_, value) => ((string?)value ?? "").TrimEnd().TrimEnd('。')));
}
