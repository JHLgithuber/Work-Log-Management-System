using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WorkLogManagementSystem_UI.Models;
using WorkLogManagementSystem_UI.Services;
using WorkLogManagementSystem_UI.ViewModels;

// Dependency-free regression runner. All requests stay in this fake handler.
var cases = new (string Name, Func<Task> Run)[]
{
    ("Saving keeps the original date and entry when navigation changes", SaveUsesSnapshot),
    ("A failed entry save retries the existing task instead of creating a duplicate", PartialSaveKeepsIdentity),
    ("Failed daily-note loading cannot overwrite an unseen note", FailedNoteLoadDisablesSave),
    ("An in-flight task load cannot repopulate a logged-out session", LogoutInvalidatesLoad),
    ("Refresh recovers failed note loading before allowing writes", RefreshRecoversNote),
    ("Logging out invalidates an in-flight daily-note read", LogoutInvalidatesNote),
    ("Clearing and reselecting a date recovers both loaders", ReselectDate),
    ("Task dates retain the Korean calendar day on every device timezone", TaskDatesUseKoreaCalendar),
    ("Korean day boundaries and unbounded dates remain stable", KoreaDayBoundaries),
    ("Refresh waits for an in-flight note save", () => RefreshDuringNoteSave(false, false)),
    ("Refresh retains a failed note-save draft", () => RefreshDuringNoteSave(true, false)),
    ("Returning to a date waits for its in-flight note save", () => RefreshDuringNoteSave(false, true)),
    ("Refresh cannot replace an in-flight task entry with stale data", () => RefreshDuringTaskSave(false, false, false)),
    ("Returning to a date loads the completed task entry", () => RefreshDuringTaskSave(true, false, false)),
    ("Reselecting a task loads its completed entry", () => RefreshDuringTaskSave(false, true, false)),
    ("Refresh retains the task-entry draft when a partial save fails", () => RefreshDuringTaskSave(false, false, true)),
    ("A failed write does not leave another date showing stale tasks", FailedWriteAfterNavigation),
};
int failures = 0;
foreach (var test in cases)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failures++; Console.WriteLine($"FAIL {test.Name}: {error.Message}"); }
}
return failures == 0 ? 0 : 1;

static MainViewModel Model(FakeHandler handler)
{
    var model = new MainViewModel((url, token) => new WorkLogApiClient(url, token, handler), false)
    {
        ApiBaseUrl = "https://test.invalid",
        AccessToken = "synthetic-test-token",
        SelectedDate = new DateTime(2099, 1, 10),
        IsBackendConnected = true,
        IsAuthenticated = true
    };
    return model;
}

static TaskDto SavedTask() => new()
{
    Id = 7, Title = "Synthetic future task",
    StartAt = new DateTimeOffset(2099, 1, 10, 0, 0, 0, TimeSpan.FromHours(9)),
    DueAt = new DateTimeOffset(2099, 1, 11, 23, 59, 59, TimeSpan.FromHours(9))
};
static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

static async Task SaveUsesSnapshot()
{
    var create = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    string? entryPath = null;
    string? entryBody = null;
    var handler = new FakeHandler(async request =>
    {
        if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/tasks") return await create.Task;
        if (request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath.Contains("/entries/"))
        {
            entryPath = request.RequestUri.AbsolutePath;
            entryBody = await request.Content!.ReadAsStringAsync();
            return Json(new WorkEntryDto());
        }
        return FakeHandler.Default(request);
    });
    var model = Model(handler);
    model.NewRootTaskCommand.Execute(null);
    model.EditorTitle = "Synthetic future task";
    model.EditorPerformedContent = "original day work";
    model.EditorDailyRetrospective = "original day reflection";
    Task save = model.SaveTaskCommand.ExecuteAsync(null);
    model.SelectedDate = new DateTime(2099, 1, 11);
    model.NewRootTaskCommand.Execute(null);
    model.EditorTitle = "newer editor";
    model.EditorPerformedContent = "newer day work";
    create.SetResult(Json(SavedTask()));
    await save;
    Check(entryPath == "/tasks/7/entries/2099-01-10", $"Unexpected entry target: {entryPath}");
    var body = JsonDocument.Parse(entryBody!).RootElement;
    Check(body.GetProperty("performed_content").GetString() == "original day work", "Saved the newer editor content");
    Check(body.GetProperty("retrospective").GetString() == "original day reflection", "Saved the newer reflection");
    Check(model.IsNewTask && model.EditorTitle == "newer editor", "Completion replaced the newer editor");
}

static async Task PartialSaveKeepsIdentity()
{
    int creates = 0;
    int updates = 0;
    int entries = 0;
    var handler = new FakeHandler(request =>
    {
        if (request.RequestUri!.AbsolutePath == "/tasks" && request.Method == HttpMethod.Post)
        { creates++; return Task.FromResult(Json(SavedTask())); }
        if (request.RequestUri.AbsolutePath == "/tasks/7" && request.Method == HttpMethod.Put)
        { updates++; return Task.FromResult(Json(SavedTask())); }
        if (request.Method == HttpMethod.Put && request.RequestUri.AbsolutePath.Contains("/entries/"))
        {
            entries++;
            return Task.FromResult(entries == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("synthetic failure") }
                : Json(new WorkEntryDto()));
        }
        return Task.FromResult(FakeHandler.Default(request));
    });
    var model = Model(handler);
    model.NewRootTaskCommand.Execute(null);
    model.EditorTitle = "Synthetic future task";
    model.EditorPerformedContent = "keep this draft";
    await model.SaveTaskCommand.ExecuteAsync(null);
    Check(model.EditorPerformedContent == "keep this draft", "Partial failure cleared the entry draft");
    await model.SaveTaskCommand.ExecuteAsync(null);
    Check(creates == 1 && updates == 1, $"Retry created {creates} tasks and made {updates} updates");
}

static async Task FailedNoteLoadDisablesSave()
{
    int writes = 0;
    var handler = new FakeHandler(request =>
    {
        if (request.RequestUri!.AbsolutePath.Contains("/daily-notes/"))
        {
            if (request.Method == HttpMethod.Put) writes++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
        return Task.FromResult(FakeHandler.Default(request));
    });
    var model = Model(handler);
    Check(!model.IsDailyNoteBusy, "Failed request never finished");
    Check(!model.CanEditDailyNote, "Failed note read enabled writing an unseen note");
    await model.SaveDailyNoteCommand.ExecuteAsync(null);
    Check(writes == 0, "A failed note read still allowed a write");
}

static async Task LogoutInvalidatesLoad()
{
    var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath == "/tasks"
        ? response.Task : Task.FromResult(FakeHandler.Default(request)));
    var model = Model(handler);
    Task loading = model.LoadTasksCommand.ExecuteAsync(null);
    model.IsAuthenticated = false;
    model.Tasks.Clear();
    response.SetResult(Json(new[] { SavedTask() }));
    await loading;
    Check(model.Tasks.Count == 0, "Previous session's tasks returned after logout");
}

static async Task RefreshRecoversNote()
{
    bool fail = true;
    string? savedContent = null;
    var handler = new FakeHandler(async request =>
    {
        if (request.RequestUri!.AbsolutePath.Contains("/daily-notes/"))
        {
            if (fail) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (request.Method == HttpMethod.Put)
            {
                savedContent = JsonDocument.Parse(await request.Content!.ReadAsStringAsync())
                    .RootElement.GetProperty("content").GetString();
            }
            return Json(new DailyNoteDto { Content = "existing note", NoteDate = new DateOnly(2099, 1, 10) });
        }
        return FakeHandler.Default(request);
    });
    var model = Model(handler);
    Check(!model.CanEditDailyNote, "Failed load did not disable editing");
    fail = false;
    await model.RefreshCommand.ExecuteAsync(null);
    Check(model.CanEditDailyNote && model.DailyNoteContent == "existing note", "Refresh did not recover the existing note");
    model.DailyNoteContent = "edited safely";
    await model.SaveDailyNoteCommand.ExecuteAsync(null);
    Check(savedContent == "edited safely", "Successful note load did not permit saving");
}

static async Task LogoutInvalidatesNote()
{
    var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    bool delayed = false;
    var handler = new FakeHandler(request => delayed && request.RequestUri!.AbsolutePath.Contains("/daily-notes/")
        ? response.Task : Task.FromResult(FakeHandler.Default(request)));
    var model = Model(handler);
    delayed = true;
    Task refresh = model.RefreshCommand.ExecuteAsync(null);
    model.IsAuthenticated = false;
    response.SetResult(Json(new DailyNoteDto { Content = "previous user's note" }));
    await refresh;
    Check(model.DailyNoteContent == "" && !model.CanEditDailyNote, "Previous user's note returned after logout");
}

static async Task ReselectDate()
{
    var handler = new FakeHandler(request => Task.FromResult(FakeHandler.Default(request)));
    var model = Model(handler);
    await model.RefreshCommand.ExecuteAsync(null);
    model.SelectedDate = null;
    Check(!model.CanEditDailyNote, "Missing date still allowed note editing");
    model.SelectedDate = new DateTime(2099, 1, 12);
    await model.RefreshCommand.ExecuteAsync(null);
    Check(model.CanEditDailyNote && !model.IsDailyNoteBusy, "Reselected date did not recover loaders");
}

static async Task TaskDatesUseKoreaCalendar()
{
    string? payload = null;
    var handler = new FakeHandler(async request =>
    {
        if (request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath == "/tasks/7")
        {
            payload = await request.Content!.ReadAsStringAsync();
            return Json(SavedTask());
        }
        if (request.Method == HttpMethod.Put) return Json(new WorkEntryDto());
        return FakeHandler.Default(request);
    });
    var model = Model(handler);
    model.SelectedTask = SavedTask();
    Check(model.EditorStartDate == new DateTime(2099, 1, 10), $"Start date shifted in {TimeZoneInfo.Local.Id}: {model.EditorStartDate}");
    Check(model.EditorDueDate == new DateTime(2099, 1, 11), $"Due date shifted in {TimeZoneInfo.Local.Id}: {model.EditorDueDate}");
    Check(model.SelectedTask.DateSummary == "2099-01-10 - 2099-01-11", "Task summary shifted calendar dates");
    await model.SaveTaskCommand.ExecuteAsync(null);
    var body = JsonDocument.Parse(payload!).RootElement;
    Check(body.GetProperty("start_at").GetDateTimeOffset().ToOffset(TimeSpan.FromHours(9)).Date == new DateTime(2099, 1, 10), "No-op edit changed the start day");
    Check(body.GetProperty("due_at").GetDateTimeOffset().ToOffset(TimeSpan.FromHours(9)).Date == new DateTime(2099, 1, 11), "No-op edit changed the due day");
}

static Task KoreaDayBoundaries()
{
    Check(WorkLogCalendar.ToDate(new DateTimeOffset(2099, 1, 10, 14, 59, 59, TimeSpan.Zero)) == new DateTime(2099, 1, 10), "Before Korean midnight changed day");
    Check(WorkLogCalendar.ToDate(new DateTimeOffset(2099, 1, 10, 15, 0, 0, TimeSpan.Zero)) == new DateTime(2099, 1, 11), "Korean midnight did not change day");
    Check(WorkLogCalendar.ToDate(WorkLogCalendar.ToRequestDate(DateTime.MinValue)) == DateTime.MinValue, "Unbounded start changed");
    Check(WorkLogCalendar.ToDate(WorkLogCalendar.ToRequestDate(DateTime.MaxValue)) == DateTime.MaxValue, "Unbounded end changed");
    return Task.CompletedTask;
}

static async Task RefreshDuringNoteSave(bool failSave, bool navigate)
{
    string serverNote = "before";
    var write = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    var handler = new FakeHandler(request =>
    {
        if (request.RequestUri!.AbsolutePath.Contains("/daily-notes/"))
        {
            if (request.Method == HttpMethod.Put) return write.Task;
            return Task.FromResult(Json(new DailyNoteDto { Content = serverNote }));
        }
        return Task.FromResult(FakeHandler.Default(request));
    });
    var model = Model(handler);
    model.DailyNoteContent = "after";
    Task saving = model.SaveDailyNoteCommand.ExecuteAsync(null);
    if (navigate)
    {
        model.SelectedDate = new DateTime(2099, 1, 11);
        model.SelectedDate = new DateTime(2099, 1, 10);
    }
    Task refresh = model.RefreshCommand.ExecuteAsync(null);
    Check(!model.CanEditDailyNote, "Pending note save exposed a stale writable editor");
    serverNote = failSave ? "before" : "after";
    write.SetResult(failSave
        ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("synthetic write failure") }
        : Json(new DailyNoteDto { Content = serverNote }));
    await Task.WhenAll(saving, refresh);
    Check(model.DailyNoteContent == "after" && model.CanEditDailyNote,
        $"Note draft/result lost: server='{serverNote}', editor='{model.DailyNoteContent}', editable={model.CanEditDailyNote}");
}

static async Task RefreshDuringTaskSave(bool changeDate, bool changeTask, bool failSave)
{
    bool entryWritten = false;
    var write = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    var handler = new FakeHandler(request =>
    {
        if (request.RequestUri!.AbsolutePath == "/tasks" && request.Method == HttpMethod.Post)
            return Task.FromResult(Json(SavedTask()));
        if (request.RequestUri.AbsolutePath == "/tasks")
        {
            var task = SavedTask();
            if (entryWritten) task.Entries.Add(new WorkEntryDto { EntryDate = new DateOnly(2099, 1, 10), PerformedContent = "new saved work" });
            return Task.FromResult(Json(new[] { task }));
        }
        if (request.RequestUri.AbsolutePath.Contains("/entries/") && request.Method == HttpMethod.Put) return write.Task;
        return Task.FromResult(FakeHandler.Default(request));
    });
    var model = Model(handler);
    model.NewRootTaskCommand.Execute(null);
    model.EditorTitle = "Synthetic future task";
    model.EditorPerformedContent = "new saved work";
    Task saving = model.SaveTaskCommand.ExecuteAsync(null);
    if (changeDate)
    {
        model.SelectedDate = new DateTime(2099, 1, 11);
        model.SelectedDate = new DateTime(2099, 1, 10);
    }
    if (changeTask)
    {
        model.SelectedTask = new TaskDto { Id = 99, Title = "other task" };
        model.SelectedTask = SavedTask();
    }
    Task refresh = model.RefreshCommand.ExecuteAsync(null);
    Check(!model.CanEditTask, "Refresh enabled editing stale task data during a write");
    entryWritten = !failSave;
    write.SetResult(failSave
        ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("synthetic entry write failure") }
        : Json(new WorkEntryDto { PerformedContent = "new saved work" }));
    await Task.WhenAll(saving, refresh);
    Check(model.EditorPerformedContent == "new saved work" && model.CanEditTask,
        $"Saved entry missing: '{model.EditorPerformedContent}', editable={model.CanEditTask}");
}

static async Task FailedWriteAfterNavigation()
{
    var write = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    var handler = new FakeHandler(request =>
    {
        if (request.RequestUri!.AbsolutePath == "/tasks/7" && request.Method == HttpMethod.Put) return Task.FromResult(Json(SavedTask()));
        if (request.RequestUri.AbsolutePath == "/tasks") return Task.FromResult(Json(request.RequestUri.Query.Contains("2099-01-12") ? Array.Empty<TaskDto>() : new[] { SavedTask() }));
        if (request.RequestUri.AbsolutePath.Contains("/entries/") && request.Method == HttpMethod.Put) return write.Task;
        return Task.FromResult(FakeHandler.Default(request));
    });
    var model = Model(handler);
    await model.RefreshCommand.ExecuteAsync(null);
    model.SelectedTask = model.Tasks[0];
    model.EditorPerformedContent = "unsaved entry";
    Task saving = model.SaveTaskCommand.ExecuteAsync(null);
    model.SelectedDate = new DateTime(2099, 1, 12);
    Task refresh = model.RefreshCommand.ExecuteAsync(null);
    write.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    await Task.WhenAll(saving, refresh);
    Check(model.Tasks.Count == 0 && model.SelectedTask is null, $"Day12 has {model.Tasks.Count} task from day10, selected={model.SelectedTask?.Id}, editor enabled={model.CanEditTask}");
}

sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    public static HttpResponseMessage Default(HttpRequestMessage request)
    {
        object value = request.RequestUri!.AbsolutePath.Contains("/daily-notes/")
            ? new DailyNoteDto { NoteDate = new DateOnly(2099, 1, 10) }
            : Array.Empty<TaskDto>();
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value, value.GetType()) };
    }
}
