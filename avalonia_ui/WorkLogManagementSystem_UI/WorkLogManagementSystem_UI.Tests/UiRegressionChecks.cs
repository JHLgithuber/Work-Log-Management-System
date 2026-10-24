using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WorkLogManagementSystem_UI.Services;
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
}
