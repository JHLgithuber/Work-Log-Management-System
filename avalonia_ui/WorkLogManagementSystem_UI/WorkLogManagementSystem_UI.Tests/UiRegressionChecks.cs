using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WorkLogManagementSystem_UI.Services;
using WorkLogManagementSystem_UI.Controls;
using WorkLogManagementSystem_UI.ViewModels;
using WorkLogManagementSystem_UI.Views;

static class UiRegressionChecks
{
    public static int Run()
    {
        AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
        Application.Current!.Resources["AppFontFamily"] = FontFamily.Default;
        Application.Current.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        var cases = new (string Name, Action Run)[]
        {
            ("ISO date entry commits on Enter", () => DateEntry(false)),
            ("ISO date entry commits on focus loss", () => DateEntry(true)),
            ("Invalid date entry retains the selected day", InvalidDateEntry),
            ("Password automation values do not expose plaintext", PasswordAutomationValue),
            ("Password automation change events do not expose plaintext", PasswordAutomationEvents),
            ("Ordinary text remains accessible and editable", OrdinaryTextAutomation),
        };
        int failures = 0;
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
            catch (Exception error) { failures++; Console.WriteLine($"FAIL {test.Name}: {error.Message}"); }
        }
        return failures == 0 ? 0 : 1;
    }

    static (Window Window, MainViewModel Model, CalendarDatePicker Picker, TextBox Input) OpenDateView()
    {
        var handler = new FakeHandler(request => Task.FromResult(FakeHandler.Default(request)));
        var model = new MainViewModel((url, token) => new WorkLogApiClient(url, token, handler: handler), false)
        {
            ApiBaseUrl = "https://test.invalid", AccessToken = "synthetic-test-token",
            IsBackendConnected = true, IsAuthenticated = true, SelectedDate = new DateTime(2099, 1, 9)
        };
        var view = new MainView { DataContext = model };
        var window = new Window { Content = view, Width = 1180, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var picker = view.FindControl<CalendarDatePicker>("TaskListDatePicker")!;
        var input = picker.GetVisualDescendants().OfType<TextBox>().Single();
        return (window, model, picker, input);
    }

    static void DateEntry(bool loseFocus)
    {
        var context = OpenDateView();
        try
        {
            context.Input.Focus();
            context.Input.Text = "2099-01-10";
            if (loseFocus)
                context.Input.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
            else
                context.Input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs();
            Check(context.Model.SelectedDate == new DateTime(2099, 1, 10), "A valid typed ISO date was discarded");
            Check(context.Picker.SelectedDate == context.Model.SelectedDate, "Date picker and selected work day differ");
        }
        finally { context.Window.Close(); }
    }

    static void InvalidDateEntry()
    {
        var context = OpenDateView();
        try
        {
            context.Input.Text = "2099-02-31";
            context.Input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs();
            Check(context.Model.SelectedDate == new DateTime(2099, 1, 9), "An invalid date changed the work day");
        }
        finally { context.Window.Close(); }
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void PasswordAutomationValue()
    {
        var input = new ImeSafeTextBox { PasswordChar = '*', Text = "TEST ONLY synthetic password" };
        var provider = ControlAutomationPeer.CreatePeerForElement(input)!.GetProvider<IValueProvider>()!;
        Check(string.IsNullOrEmpty(provider.Value), "The password is readable through the automation value provider");
        input.Text = "TEST ONLY changed synthetic password";
        Check(string.IsNullOrEmpty(provider.Value), "A changed password is readable through automation");
    }

    static void OrdinaryTextAutomation()
    {
        var input = new ImeSafeTextBox { Text = "TEST ONLY accessible text" };
        var provider = ControlAutomationPeer.CreatePeerForElement(input)!.GetProvider<IValueProvider>()!;
        Check(provider.Value == input.Text, "Ordinary text is no longer accessible");
        provider.SetValue("TEST ONLY edited text");
        Check(input.Text == "TEST ONLY edited text", "Automation can no longer edit ordinary text");
    }

    static void PasswordAutomationEvents()
    {
        var input = new ImeSafeTextBox { PasswordChar = '*', Text = "TEST ONLY old secret" };
        var peer = ControlAutomationPeer.CreatePeerForElement(input)!;
        bool leaked = false;
        peer.PropertyChanged += (_, change) =>
        {
            leaked |= change.OldValue is string oldValue && oldValue.Contains("secret") ||
                change.NewValue is string newValue && newValue.Contains("secret");
        };
        input.Text = "TEST ONLY new secret";
        Check(!leaked, "An automation change event contained the password");
    }
}
