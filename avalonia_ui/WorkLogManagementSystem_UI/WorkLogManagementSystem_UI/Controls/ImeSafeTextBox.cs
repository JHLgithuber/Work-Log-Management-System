using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace WorkLogManagementSystem_UI.Controls;

public class ImeSafeTextBox : TextBox
{
    public ImeSafeTextBox()
    {
        // Avalonia 11's browser clipboard bridge lets permission/security errors
        // escape from TextBox, which terminates the WASM runtime. Handle browser
        // clipboard shortcuts here so a denied permission only cancels the action.
        AddHandler(KeyDownEvent, OnBrowserClipboardShortcutKeyDown, RoutingStrategies.Tunnel);
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    private async void OnBrowserClipboardShortcutKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (!OperatingSystem.IsBrowser() ||
            (eventArgs.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == KeyModifiers.None)
        {
            return;
        }

        switch (eventArgs.Key)
        {
            case Key.C:
                eventArgs.Handled = true;
                await CopySelectionToClipboardAsync(cut: false);
                break;
            case Key.X when !IsReadOnly:
                eventArgs.Handled = true;
                await CopySelectionToClipboardAsync(cut: true);
                break;
            case Key.V when !IsReadOnly:
                eventArgs.Handled = true;
                await PasteFromClipboardAsync();
                break;
        }
    }

    private async Task CopySelectionToClipboardAsync(bool cut)
    {
        string selectedText = SelectedText;
        if (string.IsNullOrEmpty(selectedText))
        {
            return;
        }

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            await clipboard.SetTextAsync(selectedText);
            if (cut)
            {
                SelectedText = string.Empty;
            }
        }
        catch (Exception)
        {
            // Clipboard access can be unavailable on HTTP or denied by the browser.
            // Keep the UI alive; the user can grant clipboard permission and retry.
        }
    }

    private async Task PasteFromClipboardAsync()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            string? clipboardText = clipboard is null ? null : await clipboard.GetTextAsync();
            if (clipboardText is not null)
            {
                SelectedText = clipboardText;
            }
        }
        catch (Exception)
        {
            // See CopySelectionToClipboardAsync: clipboard failure must not exit WASM.
        }
    }

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
