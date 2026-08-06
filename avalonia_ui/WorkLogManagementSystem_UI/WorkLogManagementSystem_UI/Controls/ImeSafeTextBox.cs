using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WorkLogManagementSystem_UI.Controls;

public class ImeSafeTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    public bool CommitPendingComposition()
    {
        if (!OperatingSystem.IsLinux() || IsReadOnly)
        {
            return false;
        }

        TextPresenter? presenter = this
            .GetVisualDescendants()
            .OfType<TextPresenter>()
            .FirstOrDefault();
        string? preeditText = presenter?.PreeditText;
        if (string.IsNullOrEmpty(preeditText))
        {
            return false;
        }

        // Avalonia keeps Linux IME preedit text outside Text and clears it on focus loss.
        // Commit it through TextBox's normal insertion path before the presenter is detached.
        presenter!.SetCurrentValue(TextPresenter.PreeditTextProperty, null);
        SelectedText = preeditText;
        return true;
    }

    protected override void OnLostFocus(RoutedEventArgs eventArgs)
    {
        CommitPendingComposition();
        base.OnLostFocus(eventArgs);
    }
}
