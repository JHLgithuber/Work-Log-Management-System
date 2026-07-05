using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkLogManagementSystem_UI.Models;
using WorkLogManagementSystem_UI.Services;

namespace WorkLogManagementSystem_UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private static readonly DateTime UnboundedStart = DateTime.MinValue;
    private static readonly DateTime UnboundedEnd = DateTime.MaxValue;
    private readonly ExportFileService _exportFileService = new();
    private readonly AppSettingsService _settingsService = new();
    private CancellationTokenSource? _loadCancellationTokenSource;

    [ObservableProperty] private string _apiBaseUrl = "http://localhost:8000";
    [ObservableProperty] private string _themeMode = AppThemeMode.System;
    [ObservableProperty] private DateTime? _selectedDate = DateTime.Today;
    [ObservableProperty] private ObservableCollection<TaskDto> _tasks = new();
    [ObservableProperty] private TaskDto? _selectedTask;
    [ObservableProperty] private bool _isEditorEnabled;
    [ObservableProperty] private bool _isNewTask;
    [ObservableProperty] private string _statusMessage = "백엔드 서버를 실행한 뒤 새로고침하세요.";
    [ObservableProperty] private string _editorTitle = string.Empty;
    [ObservableProperty] private string _editorContent = string.Empty;
    [ObservableProperty] private string _editorPerformedContent = string.Empty;
    [ObservableProperty] private string _editorDailyRetrospective = string.Empty;
    [ObservableProperty] private int? _editorParentId;
    [ObservableProperty] private string _editorParentTitle = string.Empty;
    [ObservableProperty] private DateTime? _editorStartDate = DateTime.Today;
    [ObservableProperty] private DateTime? _editorDueDate = DateTime.Today;
    [ObservableProperty] private DateTime? _editorActualEndDate;

    public MainViewModel()
    {
        _ = LoadTasksAsync();
    }

    public string CurrentScopeLabel => SelectedDate?.ToString("yyyy년 MM월 dd일") ?? "날짜 없음";
    public string EditorModeLabel
    {
        get
        {
            if (!IsEditorEnabled)
            {
                return "과업 선택 없음";
            }

            string title = EditorTitle.Trim();
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }

            return IsNewTask ? "새 과업 작성" : "과업 제목";
        }
    }

    public string EditorParentTitleLabel => $"상위 과업: {EditorParentTitle}";
    public bool HasEditorParentTitle => !string.IsNullOrWhiteSpace(EditorParentTitle);
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool CanEditExistingTask => IsEditorEnabled && !IsNewTask && SelectedTask is not null;

    partial void OnSelectedDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(CurrentScopeLabel));
        _ = LoadTasksAsync();
        LoadSelectedTaskEntry();
    }

    partial void OnSelectedTaskChanged(TaskDto? value)
    {
        if (value is null)
        {
            return;
        }

        BeginEdit(value);
    }

    partial void OnIsNewTaskChanged(bool value)
    {
        OnPropertyChanged(nameof(EditorModeLabel));
        OnPropertyChanged(nameof(CanEditExistingTask));
    }

    partial void OnIsEditorEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(EditorModeLabel));
        OnPropertyChanged(nameof(CanEditExistingTask));
    }

    partial void OnEditorTitleChanged(string value)
    {
        OnPropertyChanged(nameof(EditorModeLabel));
    }

    partial void OnEditorParentTitleChanged(string value)
    {
        OnPropertyChanged(nameof(EditorParentTitleLabel));
        OnPropertyChanged(nameof(HasEditorParentTitle));
    }

    partial void OnStatusMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatusMessage));
    }

    [RelayCommand]
    private async Task LoadTasksAsync()
    {
        if (SelectedDate is null)
        {
            return;
        }

        _loadCancellationTokenSource?.Cancel();
        _loadCancellationTokenSource = new CancellationTokenSource();
        CancellationToken cancellationToken = _loadCancellationTokenSource.Token;

        try
        {
            using WorkLogApiClient client = new(ApiBaseUrl);
            IReadOnlyList<TaskDto> loadedTasks = await client.GetTasksAsync(SelectedDate.Value, cancellationToken);
            Tasks = new ObservableCollection<TaskDto>(loadedTasks);
            StatusMessage = $"{loadedTasks.Count}개 과업을 불러왔습니다.";

            if (SelectedTask is not null && FindTaskById(Tasks, SelectedTask.Id) is TaskDto reloaded)
            {
                SelectedTask = reloaded;
            }
            else if (!IsNewTask)
            {
                ClearEditor();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            StatusMessage = $"로드 실패: {error.Message}";
        }
    }

    [RelayCommand]
    private void MovePreviousDay()
    {
        SelectedDate = (SelectedDate ?? DateTime.Today).AddDays(-1);
    }

    [RelayCommand]
    private void MoveNextDay()
    {
        SelectedDate = (SelectedDate ?? DateTime.Today).AddDays(1);
    }

    [RelayCommand]
    private void MoveToday()
    {
        SelectedDate = DateTime.Today;
    }

    [RelayCommand]
    private void NewRootTask()
    {
        BeginNewTask(null, string.Empty);
    }

    [RelayCommand]
    private void NewChildTask()
    {
        if (SelectedTask is null)
        {
            StatusMessage = "하위 과업을 만들 상위 과업을 먼저 선택하세요.";
            return;
        }

        BeginNewTask(SelectedTask.Id, SelectedTask.Title);
    }

    [RelayCommand]
    private async Task SaveTaskAsync()
    {
        if (!IsEditorEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(EditorTitle))
        {
            StatusMessage = "제목은 반드시 입력해야 합니다.";
            return;
        }

        try
        {
            TaskWriteRequest request = BuildWriteRequest();
            using WorkLogApiClient client = new(ApiBaseUrl);
            TaskDto savedTask = IsNewTask || SelectedTask is null
                ? await client.CreateTaskAsync(request, CancellationToken.None)
                : await client.UpdateTaskAsync(SelectedTask.Id, request, CancellationToken.None);

            IsNewTask = false;
            StatusMessage = $"저장됨: {savedTask.Title}";
            if (SelectedDate is not null)
            {
                await client.SaveWorkEntryAsync(
                    savedTask.Id,
                    SelectedDate.Value,
                    new WorkEntryWriteRequest
                    {
                        PerformedContent = EditorPerformedContent,
                        Retrospective = EditorDailyRetrospective
                    },
                    CancellationToken.None);
            }
            await LoadTasksAsync();
            SelectedTask = FindTaskById(Tasks, savedTask.Id);
        }
        catch (Exception error)
        {
            StatusMessage = $"저장 실패: {error.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteTaskAsync()
    {
        if (SelectedTask is null || IsNewTask)
        {
            return;
        }

        try
        {
            using WorkLogApiClient client = new(ApiBaseUrl);
            await client.DeleteTaskAsync(SelectedTask.Id, CancellationToken.None);
            StatusMessage = "선택한 과업과 모든 하위 과업을 삭제했습니다.";
            SelectedTask = null;
            ClearEditor();
            await LoadTasksAsync();
        }
        catch (Exception error)
        {
            StatusMessage = $"삭제 실패: {error.Message}";
        }
    }

    [RelayCommand]
    private void SetStartUnbounded()
    {
        EditorStartDate = UnboundedStart;
    }

    [RelayCommand]
    private void SetStartToday()
    {
        EditorStartDate = DateTime.Today;
    }

    [RelayCommand]
    private void SetDueUnbounded()
    {
        EditorDueDate = UnboundedEnd;
    }

    [RelayCommand]
    private void SetDueToday()
    {
        EditorDueDate = DateTime.Today;
    }

    [RelayCommand]
    private void SetActualEndToday()
    {
        EditorActualEndDate = DateTime.Today;
    }

    [RelayCommand]
    private void ClearActualEnd()
    {
        EditorActualEndDate = null;
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        try
        {
            AppSettingsResult? settings = await _settingsService.ConfigureSettingsAsync(
                ApiBaseUrl,
                ThemeMode,
                CancellationToken.None);
            if (settings is null)
            {
                return;
            }

            bool apiBaseUrlChanged = ApiBaseUrl != settings.ApiBaseUrl;
            ApiBaseUrl = settings.ApiBaseUrl;
            ThemeMode = settings.ThemeMode;
            _settingsService.ApplyThemeMode(ThemeMode);
            StatusMessage = "설정 저장됨.";

            if (apiBaseUrlChanged)
            {
                await LoadTasksAsync();
            }
        }
        catch (Exception error)
        {
            StatusMessage = $"설정 실패: {error.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        try
        {
            DateTime defaultDate = SelectedDate ?? DateTime.Today;
            DateTime defaultStartDate = new(defaultDate.Year, defaultDate.Month, 1);
            DateTime defaultEndDate = new(
                defaultDate.Year,
                defaultDate.Month,
                DateTime.DaysInMonth(defaultDate.Year, defaultDate.Month));
            ExportDateRange? range = await _exportFileService.SelectExportRangeAsync(
                defaultStartDate,
                defaultEndDate,
                CancellationToken.None);
            if (range is null)
            {
                StatusMessage = "엑셀 내보내기를 취소했습니다.";
                return;
            }

            using WorkLogApiClient client = new(ApiBaseUrl);
            byte[] content = await client.ExportExcelAsync(range.StartDate, range.EndDate, CancellationToken.None);
            string fileName = $"{range.StartDate:yyyy-MM-dd} - {range.EndDate:yyyy-MM-dd} 업무일지.xlsx";
            string? path = await _exportFileService.SaveExcelAsync(content, fileName, CancellationToken.None);
            StatusMessage = path is null ? "엑셀 저장을 취소했습니다." : string.Empty;
        }
        catch (Exception error)
        {
            StatusMessage = $"엑셀 내보내기 실패: {error.Message}";
        }
    }

    private void BeginNewTask(int? parentId, string parentTitle)
    {
        SelectedTask = null;
        IsEditorEnabled = true;
        IsNewTask = true;
        EditorParentId = parentId;
        EditorParentTitle = parentTitle;
        EditorTitle = string.Empty;
        EditorContent = string.Empty;
        EditorPerformedContent = string.Empty;
        EditorDailyRetrospective = string.Empty;
        EditorStartDate = SelectedDate ?? DateTime.Today;
        EditorDueDate = SelectedDate ?? DateTime.Today;
        EditorActualEndDate = null;
        StatusMessage = parentId is null ? "새 최상위 과업을 작성 중입니다." : $"#{parentId}의 하위 과업을 작성 중입니다.";
        OnPropertyChanged(nameof(EditorModeLabel));
    }

    private void BeginEdit(TaskDto task)
    {
        IsEditorEnabled = true;
        IsNewTask = false;
        EditorParentId = task.ParentId;
        EditorParentTitle = ResolveParentTitle(task.ParentId);
        EditorTitle = task.Title;
        EditorContent = task.Content;
        EditorStartDate = ToPickerDate(task.StartAt);
        EditorDueDate = ToPickerDate(task.DueAt);
        EditorActualEndDate = task.ActualEndAt is null ? null : ToPickerDate(task.ActualEndAt.Value);
        LoadSelectedTaskEntry();
        OnPropertyChanged(nameof(EditorModeLabel));
        OnPropertyChanged(nameof(CanEditExistingTask));
    }

    private void ClearEditor()
    {
        IsEditorEnabled = false;
        IsNewTask = false;
        EditorParentId = null;
        EditorParentTitle = string.Empty;
        EditorTitle = string.Empty;
        EditorContent = string.Empty;
        EditorPerformedContent = string.Empty;
        EditorDailyRetrospective = string.Empty;
        EditorActualEndDate = null;
        OnPropertyChanged(nameof(EditorModeLabel));
        OnPropertyChanged(nameof(CanEditExistingTask));
    }

    private TaskWriteRequest BuildWriteRequest()
    {
        DateTimeOffset start = ToRequestDateTimeOffset(EditorStartDate, DateTimeOffset.Now);
        DateTimeOffset due = ToRequestDateTimeOffset(EditorDueDate, DateTimeOffset.Now);
        return new TaskWriteRequest
        {
            ParentId = EditorParentId,
            Title = EditorTitle.Trim(),
            Content = EditorContent,
            StartAt = start.ToUniversalTime(),
            DueAt = due.ToUniversalTime(),
            ActualEndAt = EditorActualEndDate is null
                ? null
                : ToRequestDateTimeOffset(EditorActualEndDate, DateTimeOffset.Now).ToUniversalTime()
        };
    }

    private void LoadSelectedTaskEntry()
    {
        if (SelectedTask is null || SelectedDate is null || IsNewTask)
        {
            if (!IsNewTask)
            {
                EditorPerformedContent = string.Empty;
                EditorDailyRetrospective = string.Empty;
            }
            return;
        }

        string selectedDate = SelectedDate.Value.ToString("yyyy-MM-dd");
        foreach (WorkEntryDto entry in SelectedTask.Entries)
        {
            if (entry.EntryDate.ToString("yyyy-MM-dd") == selectedDate)
            {
                EditorPerformedContent = entry.PerformedContent;
                EditorDailyRetrospective = entry.Retrospective;
                return;
            }
        }

        EditorPerformedContent = string.Empty;
        EditorDailyRetrospective = string.Empty;
    }

    private static TaskDto? FindTaskById(ObservableCollection<TaskDto> tasks, int taskId)
    {
        foreach (TaskDto task in tasks)
        {
            if (task.Id == taskId)
            {
                return task;
            }

            TaskDto? found = FindTaskById(task.Children, taskId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private string ResolveParentTitle(int? parentId)
    {
        if (parentId is null)
        {
            return string.Empty;
        }

        return FindTaskById(Tasks, parentId.Value)?.Title ?? string.Empty;
    }

    private static DateTime ToPickerDate(DateTimeOffset value)
    {
        if (value.Year <= 1)
        {
            return DateTime.MinValue;
        }

        if (value.Year >= 9999)
        {
            return DateTime.MaxValue;
        }

        return value.ToLocalTime().Date;
    }

    private static DateTimeOffset ToRequestDateTimeOffset(DateTime? value, DateTimeOffset fallback)
    {
        if (value is null)
        {
            return fallback;
        }

        DateTime date = value.Value.Date;
        if (date.Year <= 1)
        {
            return DateTimeOffset.MinValue;
        }

        if (date.Year >= 9999)
        {
            return DateTimeOffset.MaxValue;
        }

        return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Local));
    }
}
