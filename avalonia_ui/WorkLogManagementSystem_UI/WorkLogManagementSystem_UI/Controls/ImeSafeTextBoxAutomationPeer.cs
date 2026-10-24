using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace WorkLogManagementSystem_UI.Controls;

// Avalonia 11's standard TextBox peer exposes Text even when PasswordChar is set.
// Keep ordinary edit accessibility while withholding masked values and events.
internal sealed class ImeSafeTextBoxAutomationPeer : ControlAutomationPeer, IValueProvider
{
    public ImeSafeTextBoxAutomationPeer(ImeSafeTextBox owner) : base(owner)
    {
        owner.PropertyChanged += OnOwnerPropertyChanged;
    }

    private new ImeSafeTextBox Owner => (ImeSafeTextBox)base.Owner;
    public bool IsReadOnly => Owner.IsReadOnly;
    public string? Value => Owner.PasswordChar == default ? Owner.Text : string.Empty;
    public void SetValue(string? value) => Owner.Text = value;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
    protected override string? GetPlaceholderTextCore() => Owner.Watermark;

    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == TextBox.TextProperty && Owner.PasswordChar == default)
        {
            RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, change.OldValue, change.NewValue);
        }
    }
}
