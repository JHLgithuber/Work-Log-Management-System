using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WorkLogManagementSystem_UI.Models;
using WorkLogManagementSystem_UI.Views;

static partial class UiRegressionChecks
{
    static void SetPrivate(MainView view, string field, object value) =>
        typeof(MainView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, value);
    static void QueueEditor(MainView view) => typeof(MainView)
        .GetMethod("QueueFocusedTaskEditorIntoView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
    static void Settle(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    static void TaskAnchor(string mode)
    {
        var c = OpenDateView();
        try
        {
            var view = (MainView)c.Window.Content!;
            c.Model.Tasks = new ObservableCollection<TaskDto>(Enumerable.Range(1, 40)
                .Select(i => new TaskDto { Id = i, Title = "Synthetic " + i }));
            c.Model.SelectedTask = c.Model.Tasks[0];
            Settle(c.Window);
            var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
            var scroll = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset = new Vector(0, 500); Settle(c.Window);
            var first = tree.GetVisualDescendants().OfType<TreeViewItem>()
                .Where(x => x.TranslatePoint(default, scroll) is { } p && p.Y >= 0 && p.Y < scroll.Viewport.Height)
                .OrderBy(x => x.TranslatePoint(default, scroll)!.Value.Y).First();
            int id = ((TaskDto)first.DataContext!).Id;
            double y = first.TranslatePoint(default, scroll)!.Value.Y;
            var replacement = c.Model.Tasks.Select(t => new TaskDto { Id = t.Id, Title = t.Title }).ToList();
            if (mode == "new") replacement.Add(new TaskDto { Id = 41, Title = "New synthetic task" });
            if (mode == "sort") replacement.Reverse();
            if (mode == "delete") replacement = replacement.Take(3).ToList();
            if (mode == "date") { c.Model.IsBackendConnected = false; c.Model.SelectedDate = new DateTime(2099, 1, 10); }
            c.Model.Tasks = new ObservableCollection<TaskDto>(replacement);
            c.Model.SelectedTask = replacement.First(t => t.Id == (mode == "new" ? 41 : mode == "select" ? 40 : 1));
            // Reproduce the template reset that occurs during a real refresh.
            scroll.Offset = default;
            Settle(c.Window);
            if (mode is "same" or "sort")
            {
                var item = tree.GetVisualDescendants().OfType<TreeViewItem>().Single(x => x.DataContext is TaskDto t && t.Id == id);
                Check(Math.Abs(item.TranslatePoint(default, scroll)!.Value.Y - y) < 1, "Visible item anchor moved");
            }
            else if (mode == "delete")
                Check(scroll.Offset.Y >= 0 && scroll.Offset.Y <= Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height), "Deleted anchor left invalid offset");
            else if (mode is "select" or "new")
                Check(c.Model.SelectedTask?.Id == (mode == "new" ? 41 : 40) && Math.Abs(scroll.Offset.Y - 500) > 1, "Old anchor overrode new selection");
            else Check(Math.Abs(scroll.Offset.Y - 500) > 1, "Old date anchor was restored");
        }
        finally { c.Window.Close(); }
    }

    static void ImeScrollPolicy()
    {
        var c = OpenDateView();
        try
        {
            c.Model.Tasks = new ObservableCollection<TaskDto> { new() { Id = 1, Title = "Synthetic" } };
            c.Model.SelectedTask = c.Model.Tasks[0]; c.Window.Height = 500; Settle(c.Window);
            var view = (MainView)c.Window.Content!;
            var scroll = view.FindControl<ScrollViewer>("EditorScrollViewer")!;
            var editor = view.GetVisualDescendants().OfType<TextBox>().Last(x => x.Classes.Contains("editor"));
            editor.Focus(); SetPrivate(view, "_focusedTaskEditor", editor);
            scroll.Offset = default; Settle(c.Window);
            SetPrivate(view, "_imeOcclusionHeight", 0d); QueueEditor(view); Settle(c.Window);
            Check(scroll.Offset.Y == 0, "Closing IME restored focus position");
            SetPrivate(view, "_imeOcclusionHeight", 200d); QueueEditor(view); Settle(c.Window);
            Check(scroll.Offset.Y > 0, "Visible IME failed to expose focused editor");
            scroll.Offset = default; Settle(c.Window);
            QueueEditor(view); SetPrivate(view, "_imeOcclusionHeight", 0d); Settle(c.Window);
            Check(scroll.Offset.Y == 0, "Pending opening callback scrolled after closing IME");
            c.Input.Focus(); SetPrivate(view, "_imeOcclusionHeight", 200d); QueueEditor(view); Settle(c.Window);
            Check(scroll.Offset.Y == 0, "Stale focus scrolled editor");
        }
        finally { c.Window.Close(); }
    }
}
