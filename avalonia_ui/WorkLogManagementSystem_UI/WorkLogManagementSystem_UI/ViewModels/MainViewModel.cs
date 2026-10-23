using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkLogManagementSystem_UI.Models;
using WorkLogManagementSystem_UI.Services;

namespace WorkLogManagementSystem_UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private enum PendingEditorAction
    {
        None,
        SelectTask,
        SelectDate,
        NewRootTask,
        NewChildTask,
        Logout,
        CloseWindow
    }

    private static readonly DateTime UnboundedStart = DateTime.MinValue;
    private static readonly DateTime UnboundedEnd = DateTime.MaxValue;
    private readonly HashSet<int> _expandedTaskIds = new();
    private static readonly TimeSpan BackendTimezoneOffset = TimeSpan.FromHours(9);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatRequestTimeout = TimeSpan.FromSeconds(3);
    private static readonly CultureInfo KoreanCulture = CultureInfo.GetCultureInfo("ko-KR");
    private readonly ExportFileService _exportFileService = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly ICredentialStore _credentialStore = PlatformCredentialStore.Create();
    private readonly DispatcherTimer _lastSaveTimer = new();
    private readonly DispatcherTimer _heartbeatTimer = new();
    private CancellationTokenSource? _loadCancellationTokenSource;
    private CancellationTokenSource? _dailyNoteCancellationTokenSource;
    private DateTimeOffset? _lastSavedAt;
    private TaskDto? _activeEditorTask;
    private TaskDto? _pendingTaskSelection;
    private DateTime? _pendingSelectedDate;
    private PendingEditorAction _pendingEditorAction;
    private int? _pendingParentTaskId;
    private string _pendingParentTaskTitle = string.Empty;
    private bool _isApplyingTaskSelection;
    private bool _suppressSelectedDateChange;
    private bool _suppressEditorDirtyTracking;
    private bool _suppressDailyNoteDirtyTracking;
    private bool _isHeartbeatCheckRunning;
    private bool _heartbeatFailureNotified;
    private int _activeSaveOperationCount;
    private int _dailyNoteOperationVersion;
    private string _dailyNoteBaseline = string.Empty;
    private string _editorTitleBaseline = string.Empty;
    private string _editorContentBaseline = string.Empty;
    private string _editorPerformedContentBaseline = string.Empty;
    private string _editorDailyRetrospectiveBaseline = string.Empty;
    private decimal _editorPriorityBaseline;
    private int? _editorParentIdBaseline;
    private DateTime? _editorStartDateBaseline;
    private DateTime? _editorDueDateBaseline;
    private DateTime? _editorActualEndDateBaseline;
    private int _taskLoadOperationVersion;
    private int _editorContextVersion;
    private int _sessionVersion;
    private int _taskWriteVersion;
    private TaskCompletionSource<int?>? _taskSaveCompletion;
    private int _taskSaveSessionVersion;
    private Task<DailyNoteDto>? _dailyNoteSaveTask;
    private DateTime? _dailyNoteSaveDate;
    private int _dailyNoteSaveSessionVersion;
    private string _dailyNoteSaveContent = string.Empty;
    private string _dailyNoteSaveBaseline = string.Empty;
    private readonly Func<string, string?, WorkLogApiClient>? _apiClientFactory;

    [ObservableProperty] private string _apiBaseUrl = AppDefaultsService.Load().ApiBaseUrl;
    [ObservableProperty] private string _accessToken = string.Empty;
    [ObservableProperty] private string _refreshToken = string.Empty;
    [ObservableProperty] private string _currentUsername = string.Empty;
    [ObservableProperty] private string _currentDisplayName = string.Empty;
    [ObservableProperty] private bool _currentUserIsAdmin;
    [ObservableProperty] private string _loginUsername = string.Empty;
    [ObservableProperty] private string _loginPassword = string.Empty;
    [ObservableProperty] private bool _rememberCredentials;
    [ObservableProperty] private string _themeMode = AppThemeMode.System;
    [ObservableProperty] private ThemeVariant _activeThemeVariant = ThemeVariant.Default;
    [ObservableProperty] private DateTime? _selectedDate = DateTime.Today;
    [ObservableProperty] private ObservableCollection<TaskDto> _tasks = new();
    [ObservableProperty] private TaskDto? _selectedTask;
    [ObservableProperty] private bool _isEditorEnabled;
    [ObservableProperty] private bool _isNewTask;
    [ObservableProperty] private string _statusMessage = "백엔드 서버를 실행한 뒤 새로고침하세요.";
    [ObservableProperty] private string _editorTitle = string.Empty;
    [ObservableProperty] private string _editorDisplayTitle = string.Empty;
    [ObservableProperty] private string _editorContent = string.Empty;
    [ObservableProperty] private string _editorPerformedContent = string.Empty;
    [ObservableProperty] private string _editorDailyRetrospective = string.Empty;
    [ObservableProperty] private decimal _editorPriority;
    [ObservableProperty] private int? _editorParentId;
    [ObservableProperty] private string _editorParentTitle = string.Empty;
    [ObservableProperty] private DateTime? _editorStartDate = DateTime.Today;
    [ObservableProperty] private DateTime? _editorDueDate = DateTime.Today;
    [ObservableProperty] private DateTime? _editorActualEndDate;
    [ObservableProperty] private bool _isSettingsDialogOpen;
    [ObservableProperty] private string _settingsApiBaseUrl = string.Empty;
    [ObservableProperty] private bool _settingsUseSystemTheme = true;
    [ObservableProperty] private bool _settingsUseLightTheme;
    [ObservableProperty] private bool _settingsUseDarkTheme;
    [ObservableProperty] private string _settingsErrorMessage = string.Empty;
    [ObservableProperty] private bool _isChangePasswordDialogOpen;
    [ObservableProperty] private string _changePasswordErrorMessage = string.Empty;
    [ObservableProperty] private string _settingsCurrentPassword = string.Empty;
    [ObservableProperty] private string _settingsNewPassword = string.Empty;
    [ObservableProperty] private string _settingsNewPasswordConfirm = string.Empty;
    [ObservableProperty] private bool _isRegisterUserDialogOpen;
    [ObservableProperty] private bool _isDeleteTaskConfirmationOpen;
    [ObservableProperty] private bool _isDeleteDateEntryConfirmationOpen;
    [ObservableProperty] private bool _isUnsavedChangesDialogOpen;
    [ObservableProperty] private string _registerUserErrorMessage = string.Empty;
    [ObservableProperty] private string _newUserUsername = string.Empty;
    [ObservableProperty] private string _newUserDisplayName = string.Empty;
    [ObservableProperty] private string _newUserPassword = string.Empty;
    [ObservableProperty] private string _newUserPasswordConfirm = string.Empty;
    [ObservableProperty] private bool _isConnectionErrorDialogOpen;
    [ObservableProperty] private string _connectionErrorDialogTitle = "서버 통신 실패";
    [ObservableProperty] private string _connectionErrorMessage = string.Empty;
    [ObservableProperty] private string _connectionErrorHtml = string.Empty;
    [ObservableProperty] private bool _isConnectionErrorHtml;
    [ObservableProperty] private bool _isBackendConnected;
    [ObservableProperty] private bool _isConnectionBusy;
    [ObservableProperty] private string _connectionGateApiBaseUrl = string.Empty;
    [ObservableProperty] private string _connectionGateErrorMessage = string.Empty;
    [ObservableProperty] private string _connectionGateStatusMessage = string.Empty;
    [ObservableProperty] private bool _isExportRangeDialogOpen;
    [ObservableProperty] private DateTime? _exportStartDate = DateTime.Today;
    [ObservableProperty] private DateTime? _exportEndDate = DateTime.Today;
    [ObservableProperty] private string _exportRangeErrorMessage = string.Empty;
    [ObservableProperty] private bool _isAuthenticated;
    [ObservableProperty] private bool _isDailyNoteExpanded;
    [ObservableProperty] private bool _isDailyNoteBusy;
    [ObservableProperty] private bool _isDailyNoteLoaded;
    [ObservableProperty] private bool _isTaskSaveBusy;
    [ObservableProperty] private bool _isTaskLoadBusy;
    [ObservableProperty] private string _dailyNoteContent = string.Empty;
    [ObservableProperty] private string _lastSaveElapsedLabel = "마지막 저장 없음";
    [ObservableProperty] private bool _hasUnsavedTaskChanges;
    [ObservableProperty] private bool _hasUnsavedDailyNoteChanges;

    public event EventHandler? CloseWindowRequested;

    public MainViewModel() : this(null, true)
    {
    }

    internal MainViewModel(Func<string, string?, WorkLogApiClient>? apiClientFactory, bool initializeConnection)
    {
        _apiClientFactory = apiClientFactory;
        if (!initializeConnection)
        {
            return;
        }

        _lastSaveTimer.Interval = TimeSpan.FromSeconds(1);
        _lastSaveTimer.Tick += OnLastSaveTimerTick;
        _lastSaveTimer.Start();
        _heartbeatTimer.Interval = HeartbeatInterval;
        _heartbeatTimer.Tick += OnHeartbeatTimerTick;
        _heartbeatTimer.Start();
        LoadPersistedSettings();
        ConnectionGateApiBaseUrl = ApiBaseUrl;
        _ = InitializeConnectionAsync();
    }

    public string CurrentScopeLabel => SelectedDate?.ToString("yyyy년 MM월 dd일 (ddd)", KoreanCulture) ?? "날짜 없음";
    public string CurrentUserLabel => string.IsNullOrWhiteSpace(CurrentDisplayName)
        ? CurrentUsername
        : CurrentDisplayName;
    public string EditorModeLabel
    {
        get
        {
            if (!IsEditorEnabled)
            {
                return "과업 선택 없음";
            }

            if (IsNewTask)
            {
                return "새 과업 작성";
            }

            string title = EditorDisplayTitle.Trim();
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }

            return "과업 제목";
        }
    }

    public string EditorParentTitleLabel => $"상위 과업: {EditorParentTitle}";
    public bool HasEditorParentTitle => !string.IsNullOrWhiteSpace(EditorParentTitle);
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool HasSettingsErrorMessage => !string.IsNullOrWhiteSpace(SettingsErrorMessage);
    public bool HasChangePasswordErrorMessage => !string.IsNullOrWhiteSpace(ChangePasswordErrorMessage);
    public bool HasRegisterUserErrorMessage => !string.IsNullOrWhiteSpace(RegisterUserErrorMessage);
    public bool IsConnectionGateVisible => !IsBackendConnected || !IsAuthenticated;
    public bool IsLoggedInStatusVisible => IsBackendConnected && IsAuthenticated;
    public bool HasConnectionGateErrorMessage => !string.IsNullOrWhiteSpace(ConnectionGateErrorMessage);
    public bool HasConnectionGateStatusMessage => !string.IsNullOrWhiteSpace(ConnectionGateStatusMessage);
    public bool IsCredentialSaveUiVisible => !OperatingSystem.IsBrowser();
    public bool IsConnectionErrorTextVisible => IsConnectionErrorDialogOpen && !IsConnectionErrorHtml;
    public bool IsConnectionErrorHtmlVisible => IsConnectionErrorDialogOpen && IsConnectionErrorHtml;
    public bool HasExportRangeErrorMessage => !string.IsNullOrWhiteSpace(ExportRangeErrorMessage);
    public bool CanConfigureApiBaseUrl => !OperatingSystem.IsBrowser();
    public string ConnectionGateDescription => CanConfigureApiBaseUrl
        ? "업무일지 백엔드 주소와 계정을 입력하세요."
        : "업무일지 계정을 입력하세요.";
    public bool CanEditTask => IsEditorEnabled && !IsTaskSaveBusy && !IsTaskLoadBusy;
    public bool CanEditExistingTask => CanEditTask && !IsNewTask && SelectedTask is not null;
    public bool CanCreateChildTask =>
        CanEditTask &&
        !IsNewTask &&
        SelectedTask is { ParentId: null };
    public bool CanDeleteSelectedDateEntry =>
        CanEditExistingTask &&
        SelectedDate is not null &&
        (!string.IsNullOrEmpty(EditorPerformedContent) || !string.IsNullOrEmpty(EditorDailyRetrospective));
    public string DeleteTaskConfirmationMessage => SelectedTask is null
        ? "선택한 과업을 삭제하시겠습니까?"
        : $"‘{SelectedTask.Title}’ 과업을 전체 삭제하시겠습니까?";
    public bool IsCredentialSaveSupported => _credentialStore.IsSaveSupported;
    public bool IsCredentialSaveUnsupported => !_credentialStore.IsSaveSupported;
    public string CredentialSaveUnsupportedReason => _credentialStore.UnsupportedReason;
    public bool CanEditDailyNote => SelectedDate is not null && IsBackendConnected && IsAuthenticated && IsDailyNoteLoaded && !IsDailyNoteBusy;

    partial void OnSelectedDateChanged(DateTime? oldValue, DateTime? newValue)
    {
        OnPropertyChanged(nameof(CurrentScopeLabel));
        OnPropertyChanged(nameof(CanEditDailyNote));
        OnPropertyChanged(nameof(CanDeleteSelectedDateEntry));

        if (_suppressSelectedDateChange)
        {
            return;
        }

        if (HasUnsavedTaskChanges || HasUnsavedDailyNoteChanges)
        {
            _pendingSelectedDate = newValue;
            _suppressSelectedDateChange = true;
            try
            {
                SelectedDate = oldValue;
            }
            finally
            {
                _suppressSelectedDateChange = false;
            }

            PreparePendingEditorAction(PendingEditorAction.SelectDate);
            return;
        }

        BeginSelectedDateChange();
    }

    private void BeginSelectedDateChange()
    {
        _editorContextVersion++;
        _ = LoadTasksAsync();
        _ = LoadDailyNoteAsync();
        LoadSelectedTaskEntry();
    }

    partial void OnSelectedTaskChanged(TaskDto? oldValue, TaskDto? newValue)
    {
        OnPropertyChanged(nameof(DeleteTaskConfirmationMessage));
        OnPropertyChanged(nameof(CanDeleteSelectedDateEntry));
        OnPropertyChanged(nameof(CanCreateChildTask));
        if (_isApplyingTaskSelection)
        {
            return;
        }

        if (!IsNewTask && newValue is not null && _activeEditorTask?.Id == newValue.Id)
        {
            _activeEditorTask = newValue;
            if (!HasUnsavedTaskChanges)
            {
                BeginEdit(newValue);
            }
            return;
        }

        if (HasUnsavedTaskChanges && IsEditorEnabled)
        {
            _pendingEditorAction = PendingEditorAction.SelectTask;
            _pendingTaskSelection = newValue;
            RestoreActiveTaskSelection();
            IsUnsavedChangesDialogOpen = true;
            return;
        }

        ApplyTaskSelection(newValue);
    }

    partial void OnChangePasswordErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasChangePasswordErrorMessage));
    }

    partial void OnRegisterUserErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasRegisterUserErrorMessage));
    }

    partial void OnIsNewTaskChanged(bool value)
    {
        OnPropertyChanged(nameof(EditorModeLabel));
        OnPropertyChanged(nameof(CanEditExistingTask));
        OnPropertyChanged(nameof(CanDeleteSelectedDateEntry));
        OnPropertyChanged(nameof(CanCreateChildTask));
    }

    partial void OnIsEditorEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditTask));
        OnPropertyChanged(nameof(EditorModeLabel));
        OnPropertyChanged(nameof(CanEditExistingTask));
        OnPropertyChanged(nameof(CanDeleteSelectedDateEntry));
        OnPropertyChanged(nameof(CanCreateChildTask));
    }

    partial void OnCurrentUsernameChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentUserLabel));
    }

    partial void OnCurrentDisplayNameChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentUserLabel));
    }

    partial void OnEditorDisplayTitleChanged(string value)
    {
        OnPropertyChanged(nameof(EditorModeLabel));
    }

    partial void OnEditorPerformedContentChanged(string value)
    {
        OnPropertyChanged(nameof(CanDeleteSelectedDateEntry));
        MarkEditorDirty();
    }

    partial void OnEditorDailyRetrospectiveChanged(string value)
    {
        OnPropertyChanged(nameof(CanDeleteSelectedDateEntry));
        MarkEditorDirty();
    }

    partial void OnEditorTitleChanged(string value) => MarkEditorDirty();
    partial void OnEditorContentChanged(string value) => MarkEditorDirty();
    partial void OnEditorPriorityChanged(decimal value) => MarkEditorDirty();
    partial void OnEditorParentIdChanged(int? value) => MarkEditorDirty();
    partial void OnEditorStartDateChanged(DateTime? value) => MarkEditorDirty();
    partial void OnEditorDueDateChanged(DateTime? value) => MarkEditorDirty();
    partial void OnEditorActualEndDateChanged(DateTime? value) => MarkEditorDirty();

    partial void OnDailyNoteContentChanged(string value)
    {
        if (!_suppressDailyNoteDirtyTracking)
        {
            HasUnsavedDailyNoteChanges = value != _dailyNoteBaseline;
        }
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

    partial void OnSettingsErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasSettingsErrorMessage));
    }

    partial void OnIsBackendConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsConnectionGateVisible));
        OnPropertyChanged(nameof(IsLoggedInStatusVisible));
        OnPropertyChanged(nameof(CanEditDailyNote));
        if (value)
        {
            _heartbeatFailureNotified = false;
        }
    }

    partial void OnIsAuthenticatedChanged(bool value)
    {
        _sessionVersion++;
        InvalidatePendingReads();
        OnPropertyChanged(nameof(IsConnectionGateVisible));
        OnPropertyChanged(nameof(IsLoggedInStatusVisible));
        OnPropertyChanged(nameof(CanEditDailyNote));
        if (value)
        {
            _ = LoadDailyNoteAsync();
        }
        else
        {
            SetDailyNoteContentFromServer(string.Empty);
        }
    }

    private void InvalidatePendingReads()
    {
        _taskLoadOperationVersion++;
        _dailyNoteOperationVersion++;
        _loadCancellationTokenSource?.Cancel();
        _dailyNoteCancellationTokenSource?.Cancel();
        IsDailyNoteLoaded = false;
        IsDailyNoteBusy = false;
        IsTaskLoadBusy = false;
    }

    partial void OnIsTaskLoadBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCreateChildTask));
        OnPropertyChanged(nameof(CanDeleteSelectedDateEntry));
        OnPropertyChanged(nameof(CanEditTask));
        OnPropertyChanged(nameof(CanEditExistingTask));
    }

    partial void OnIsTaskSaveBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCreateChildTask));
        OnPropertyChanged(nameof(CanDeleteSelectedDateEntry));
        OnPropertyChanged(nameof(CanEditTask));
        OnPropertyChanged(nameof(CanEditExistingTask));
    }

    partial void OnIsDailyNoteLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditDailyNote));
    }

    partial void OnIsDailyNoteBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditDailyNote));
    }

    partial void OnConnectionGateErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasConnectionGateErrorMessage));
    }

    partial void OnConnectionGateStatusMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasConnectionGateStatusMessage));
    }

    partial void OnIsConnectionErrorDialogOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsConnectionErrorTextVisible));
        OnPropertyChanged(nameof(IsConnectionErrorHtmlVisible));
    }

    partial void OnIsConnectionErrorHtmlChanged(bool value)
    {
        OnPropertyChanged(nameof(IsConnectionErrorTextVisible));
        OnPropertyChanged(nameof(IsConnectionErrorHtmlVisible));
    }

    partial void OnSettingsUseSystemThemeChanged(bool value)
    {
        if (value)
        {
            ApplyThemePreview(AppThemeMode.System);
        }
    }

    partial void OnSettingsUseLightThemeChanged(bool value)
    {
        if (value)
        {
            ApplyThemePreview(AppThemeMode.Light);
        }
    }

    partial void OnSettingsUseDarkThemeChanged(bool value)
    {
        if (value)
        {
            ApplyThemePreview(AppThemeMode.Dark);
        }
    }

    partial void OnExportRangeErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasExportRangeErrorMessage));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await Task.WhenAll(LoadTasksAsync(), LoadDailyNoteAsync());
    }

    [RelayCommand]
    private async Task LoadTasksAsync()
    {
        _loadCancellationTokenSource?.Cancel();
        _loadCancellationTokenSource?.Dispose();
        _loadCancellationTokenSource = null;
        int operationVersion = ++_taskLoadOperationVersion;
        IsTaskLoadBusy = false;
        if (SelectedDate is null || !IsBackendConnected || !IsAuthenticated)
        {
            return;
        }

        DateTime requestedDate = SelectedDate.Value.Date;
        int sessionVersion = _sessionVersion;
        int writeVersion = _taskWriteVersion;
        Task<int?>? pendingSave = _taskSaveSessionVersion == sessionVersion ? _taskSaveCompletion?.Task : null;
        IsTaskLoadBusy = true;
        _loadCancellationTokenSource = new CancellationTokenSource();
        CancellationToken cancellationToken = _loadCancellationTokenSource.Token;

        bool IsCurrentRequest() => operationVersion == _taskLoadOperationVersion &&
            sessionVersion == _sessionVersion && writeVersion == _taskWriteVersion &&
            IsAuthenticated && SelectedDate?.Date == requestedDate;
        try
        {
            CaptureExpansionState(Tasks);
            int? focusedTaskId = SelectedTask?.Id ?? _activeEditorTask?.Id;
            using WorkLogApiClient client = CreateAuthenticatedClient();
            // Wait for both task and entry writes before reading. This also
            // covers navigating away and back, or selecting another task.
            if (pendingSave is not null)
            {
                int? failedEditorContext = await pendingSave;
                if (!IsCurrentRequest() || failedEditorContext == _editorContextVersion)
                {
                    // Only preserve the unchanged editor's failed-save draft.
                    // A different date/selection still needs its own fresh list.
                    return;
                }
            }

            IReadOnlyList<TaskDto> loadedTasks = await client.GetTasksAsync(
                requestedDate,
                focusedTaskId,
                cancellationToken);
            if (!IsCurrentRequest()) return;
            ApplyTaskDisplayState(loadedTasks, requestedDate);
            int? selectedTaskId = SelectedTask?.Id ?? focusedTaskId;
            TaskDto? reloadedTask = null;
            _isApplyingTaskSelection = true;
            try
            {
                Tasks = new ObservableCollection<TaskDto>(SortTasks(loadedTasks));
                reloadedTask = selectedTaskId is null ? null : FindTaskById(Tasks, selectedTaskId.Value);
                SelectedTask = reloadedTask;
            }
            finally
            {
                _isApplyingTaskSelection = false;
            }

            StatusMessage = $"{loadedTasks.Count}개 과업을 불러왔습니다.";

            if (reloadedTask is not null)
            {
                _activeEditorTask = reloadedTask;
                if (!IsNewTask && !HasUnsavedTaskChanges)
                {
                    BeginEdit(reloadedTask);
                }
            }
            else if (!IsNewTask && !HasUnsavedTaskChanges)
            {
                ClearEditor();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (IsCurrentRequest())
            {
                StatusMessage = $"로드 실패: {error.Message}";
                ShowServerCommunicationFailure(error, "과업 로드 실패");
            }
        }
        finally
        {
            if (operationVersion == _taskLoadOperationVersion)
            {
                IsTaskLoadBusy = false;
            }
        }
    }

    private async Task LoadDailyNoteAsync()
    {
        if (HasUnsavedDailyNoteChanges && _dailyNoteSaveTask is null && IsAuthenticated && IsDailyNoteLoaded) return;
        _dailyNoteCancellationTokenSource?.Cancel();
        _dailyNoteCancellationTokenSource?.Dispose();
        _dailyNoteCancellationTokenSource = null;
        int operationVersion = ++_dailyNoteOperationVersion;
        IsDailyNoteLoaded = false;
        IsDailyNoteBusy = false;
        if (SelectedDate is null || !IsBackendConnected || !IsAuthenticated)
        {
            SetDailyNoteContentFromServer(string.Empty);
            return;
        }

        DateTime requestedDate = SelectedDate.Value.Date;
        // Refreshing this same day must observe the pending write, not race it
        // with a GET that can still return the previous server value.
        Task<DailyNoteDto>? pendingSave = _dailyNoteSaveDate == requestedDate &&
            _dailyNoteSaveSessionVersion == _sessionVersion ? _dailyNoteSaveTask : null;
        string pendingSaveContent = _dailyNoteSaveContent;
        string pendingSaveBaseline = _dailyNoteSaveBaseline;
        _dailyNoteCancellationTokenSource = new CancellationTokenSource();
        CancellationToken cancellationToken = _dailyNoteCancellationTokenSource.Token;
        SetDailyNoteContentFromServer(string.Empty);
        IsDailyNoteBusy = true;

        try
        {
            using WorkLogApiClient client = CreateAuthenticatedClient();
            DailyNoteDto note = pendingSave is null
                ? await client.GetDailyNoteAsync(requestedDate, cancellationToken)
                : await pendingSave;
            if (operationVersion == _dailyNoteOperationVersion && SelectedDate?.Date == requestedDate)
            {
                SetDailyNoteContentFromServer(note.Content);
                IsDailyNoteLoaded = true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (operationVersion == _dailyNoteOperationVersion && SelectedDate?.Date == requestedDate)
            {
                SetDailyNoteContentFromServer(pendingSave is null ? string.Empty : pendingSaveContent);
                if (pendingSave is not null) { _dailyNoteBaseline = pendingSaveBaseline; HasUnsavedDailyNoteChanges = true; }
                IsDailyNoteLoaded = pendingSave is not null;
                StatusMessage = pendingSave is null
                    ? $"비고 로드 실패: {error.Message}"
                    : $"비고 저장 실패: {error.Message}";
            }
        }
        finally
        {
            if (operationVersion == _dailyNoteOperationVersion && SelectedDate?.Date == requestedDate)
            {
                IsDailyNoteBusy = false;
            }
        }
    }

    [RelayCommand]
    private async Task SaveDailyNoteAsync()
    {
        await SaveDailyNoteCoreAsync();
    }

    private async Task<bool> SaveDailyNoteCoreAsync()
    {
        if (!CanEditDailyNote || SelectedDate is null)
        {
            return false;
        }

        DateTime noteDate = SelectedDate.Value.Date;
        _dailyNoteCancellationTokenSource?.Cancel();
        int operationVersion = ++_dailyNoteOperationVersion;
        IsDailyNoteBusy = true;
        BeginSaving();
        try
        {
            using WorkLogApiClient client = CreateAuthenticatedClient();
            _dailyNoteSaveDate = noteDate;
            _dailyNoteSaveSessionVersion = _sessionVersion;
            _dailyNoteSaveContent = DailyNoteContent;
            _dailyNoteSaveBaseline = _dailyNoteBaseline;
            _dailyNoteSaveTask = client.SaveDailyNoteAsync(
                noteDate,
                new DailyNoteWriteRequest { Content = _dailyNoteSaveContent },
                CancellationToken.None);
            DailyNoteDto saved = await _dailyNoteSaveTask;
            if (operationVersion == _dailyNoteOperationVersion && SelectedDate?.Date == noteDate)
            {
                SetDailyNoteContentFromServer(saved.Content);
                StatusMessage = $"{noteDate:yyyy-MM-dd} 비고를 저장했습니다.";
            }
            MarkSaved();
            return true;
        }
        catch (Exception error)
        {
            MarkSaveFailed();
            if (operationVersion == _dailyNoteOperationVersion)
            {
                StatusMessage = $"비고 저장 실패: {error.Message}";
            }
            ShowServerCommunicationFailure(error, "비고 저장 실패");
            return false;
        }
        finally
        {
            _dailyNoteSaveTask = null;
            _dailyNoteSaveDate = null;
            if (operationVersion == _dailyNoteOperationVersion)
            {
                IsDailyNoteBusy = false;
            }
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
        if (HasUnsavedTaskChanges)
        {
            PreparePendingEditorAction(PendingEditorAction.NewRootTask);
            return;
        }

        BeginNewTaskCore(null, string.Empty);
    }

    [RelayCommand]
    private void NewChildTask()
    {
        if (!CanCreateChildTask || SelectedTask is null)
        {
            return;
        }

        int parentId = SelectedTask.Id;
        string parentTitle = SelectedTask.Title;
        if (HasUnsavedTaskChanges)
        {
            _pendingParentTaskId = parentId;
            _pendingParentTaskTitle = parentTitle;
            PreparePendingEditorAction(PendingEditorAction.NewChildTask);
            return;
        }

        BeginNewTaskCore(parentId, parentTitle);
    }

    [RelayCommand]
    private async Task SaveTaskAsync()
    {
        await SaveTaskCoreAsync();
    }

    private async Task<bool> SaveTaskCoreAsync()
    {
        if (!CanEditTask) return false;
        if (string.IsNullOrWhiteSpace(EditorTitle))
        {
            StatusMessage = "제목은 반드시 입력해야 합니다.";
            return false;
        }

        DateTime? entryDate = SelectedDate?.Date;
        WorkEntryWriteRequest entryRequest = new()
        {
            PerformedContent = EditorPerformedContent,
            Retrospective = EditorDailyRetrospective
        };
        TaskWriteRequest request = BuildWriteRequest();
        int? taskId = IsNewTask ? null : SelectedTask?.Id;
        int editorVersion = _editorContextVersion;
        int sessionVersion = _sessionVersion;
        bool IsCurrentEditor() => editorVersion == _editorContextVersion &&
            sessionVersion == _sessionVersion && SelectedDate?.Date == entryDate;
        IsTaskSaveBusy = true;
        var completion = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _taskSaveCompletion = completion;
        _taskSaveSessionVersion = sessionVersion;
        _taskWriteVersion++;
        BeginSaving();
        bool writeCompleted = false;
        try
        {
            using WorkLogApiClient client = CreateAuthenticatedClient();
            TaskDto savedTask = taskId is null
                ? await client.CreateTaskAsync(request, CancellationToken.None)
                : await client.UpdateTaskAsync(taskId.Value, request, CancellationToken.None);
            if (IsCurrentEditor())
            {
                // Preserve the assigned ID even when the following entry write fails.
                IsNewTask = false;
                SetSelectedTaskWithoutPrompt(savedTask);
                _activeEditorTask = savedTask;
            }
            if (entryDate is not null)
                await client.SaveWorkEntryAsync(savedTask.Id, entryDate.Value, entryRequest, CancellationToken.None);

            if (IsCurrentEditor())
            {
                EditorDisplayTitle = savedTask.Title;
                CaptureEditorBaseline();
                HasUnsavedTaskChanges = false;
                StatusMessage = $"저장됨: {savedTask.Title}";
            }
            writeCompleted = true;
            MarkSaved();
            completion.TrySetResult(null);
            if (sessionVersion == _sessionVersion && IsAuthenticated) await LoadTasksAsync();
            return true;
        }
        catch (Exception error)
        {
            if (!writeCompleted) MarkSaveFailed();
            if (IsCurrentEditor())
            {
                StatusMessage = $"저장 실패: {error.Message}";
                ShowServerCommunicationFailure(error, "저장 실패");
            }
            return false;
        }
        finally
        {
            completion.TrySetResult(editorVersion);
            if (ReferenceEquals(_taskSaveCompletion, completion)) _taskSaveCompletion = null;
            IsTaskSaveBusy = false;
        }
    }

    [RelayCommand]
    private void CancelUnsavedChanges()
    {
        IsUnsavedChangesDialogOpen = false;
        ClearPendingEditorAction();
    }

    [RelayCommand]
    private void DiscardUnsavedChanges()
    {
        if (HasUnsavedTaskChanges)
        {
            if (IsNewTask || _activeEditorTask is null)
            {
                ClearEditor();
            }
            else
            {
                BeginEdit(_activeEditorTask);
            }
        }

        HasUnsavedTaskChanges = false;
        if (PendingActionIncludesDailyNote())
        {
            SetDailyNoteContentFromServer(_dailyNoteBaseline);
        }
        IsUnsavedChangesDialogOpen = false;
        ExecutePendingEditorAction();
    }

    [RelayCommand]
    private async Task SaveUnsavedChangesAsync()
    {
        if (HasUnsavedTaskChanges && !await SaveTaskCoreAsync())
        {
            return;
        }

        if (PendingActionIncludesDailyNote() &&
            HasUnsavedDailyNoteChanges &&
            !await SaveDailyNoteCoreAsync())
        {
            return;
        }

        IsUnsavedChangesDialogOpen = false;
        ExecutePendingEditorAction();
    }

    public void RequestCloseWithUnsavedChanges()
    {
        if (!HasUnsavedTaskChanges && !HasUnsavedDailyNoteChanges)
        {
            CloseWindowRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        PreparePendingEditorAction(PendingEditorAction.CloseWindow);
    }

    [RelayCommand]
    private void RequestDeleteSelectedDateEntry()
    {
        if (CanDeleteSelectedDateEntry)
        {
            IsDeleteDateEntryConfirmationOpen = true;
        }
    }

    [RelayCommand]
    private void CancelDeleteSelectedDateEntry()
    {
        IsDeleteDateEntryConfirmationOpen = false;
    }

    [RelayCommand]
    private async Task ConfirmDeleteSelectedDateEntryAsync()
    {
        if (!CanDeleteSelectedDateEntry || SelectedTask is null || SelectedDate is null)
        {
            IsDeleteDateEntryConfirmationOpen = false;
            return;
        }

        int taskId = SelectedTask.Id;
        DateTime entryDate = SelectedDate.Value.Date;
        try
        {
            IsDeleteDateEntryConfirmationOpen = false;
            using WorkLogApiClient client = CreateAuthenticatedClient();
            await client.DeleteWorkEntryAsync(taskId, entryDate, CancellationToken.None);

            UpdateEditorWithoutDirtyTracking(() =>
            {
                EditorPerformedContent = string.Empty;
                EditorDailyRetrospective = string.Empty;
            });
            _editorPerformedContentBaseline = string.Empty;
            _editorDailyRetrospectiveBaseline = string.Empty;
            RecomputeEditorDirtyState();
            await LoadTasksAsync();
            SelectedTask = FindTaskById(Tasks, taskId);
            StatusMessage = $"{entryDate:yyyy-MM-dd}의 수행내용과 회고를 삭제했습니다.";
        }
        catch (Exception error)
        {
            StatusMessage = $"해당일 기록 삭제 실패: {error.Message}";
            ShowServerCommunicationFailure(error, "당일 기록 삭제 실패");
        }
    }

    [RelayCommand]
    private void RequestDeleteTask()
    {
        if (!CanEditExistingTask)
        {
            return;
        }

        OnPropertyChanged(nameof(DeleteTaskConfirmationMessage));
        IsDeleteTaskConfirmationOpen = true;
    }

    [RelayCommand]
    private void CancelDeleteTask()
    {
        IsDeleteTaskConfirmationOpen = false;
    }

    [RelayCommand]
    private async Task ConfirmDeleteTaskAsync()
    {
        if (SelectedTask is null || IsNewTask)
        {
            IsDeleteTaskConfirmationOpen = false;
            return;
        }

        try
        {
            IsDeleteTaskConfirmationOpen = false;
            using WorkLogApiClient client = CreateAuthenticatedClient();
            await client.DeleteTaskAsync(SelectedTask.Id, CancellationToken.None);
            StatusMessage = "선택한 과업과 모든 하위 과업을 삭제했습니다.";
            HasUnsavedTaskChanges = false;
            ClearEditor();
            await LoadTasksAsync();
        }
        catch (Exception error)
        {
            StatusMessage = $"삭제 실패: {error.Message}";
            ShowServerCommunicationFailure(error, "과업 삭제 실패");
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
    private void OpenSettings()
    {
        SettingsApiBaseUrl = ApiBaseUrl;
        SettingsErrorMessage = string.Empty;
        ClearSettingsAccountFields();
        SetSettingsThemeMode(ThemeMode);
        IsSettingsDialogOpen = true;
    }

    [RelayCommand]
    private void CancelSettings()
    {
        IsSettingsDialogOpen = false;
        IsChangePasswordDialogOpen = false;
        IsRegisterUserDialogOpen = false;
        IsDeleteTaskConfirmationOpen = false;
        SettingsErrorMessage = string.Empty;
        ApplyThemePreview(ThemeMode);
        ClearSettingsAccountFields();
    }

    [RelayCommand]
    private void OpenChangePasswordDialog()
    {
        ChangePasswordErrorMessage = string.Empty;
        SettingsCurrentPassword = string.Empty;
        SettingsNewPassword = string.Empty;
        SettingsNewPasswordConfirm = string.Empty;
        IsChangePasswordDialogOpen = true;
    }

    [RelayCommand]
    private void CancelChangePassword()
    {
        IsChangePasswordDialogOpen = false;
        ChangePasswordErrorMessage = string.Empty;
        SettingsCurrentPassword = string.Empty;
        SettingsNewPassword = string.Empty;
        SettingsNewPasswordConfirm = string.Empty;
    }

    [RelayCommand]
    private void OpenRegisterUserDialog()
    {
        if (!CurrentUserIsAdmin)
        {
            StatusMessage = "사용자 추가는 관리자만 할 수 있습니다.";
            return;
        }

        RegisterUserErrorMessage = string.Empty;
        NewUserUsername = string.Empty;
        NewUserDisplayName = string.Empty;
        NewUserPassword = string.Empty;
        NewUserPasswordConfirm = string.Empty;
        IsRegisterUserDialogOpen = true;
    }

    [RelayCommand]
    private void CancelRegisterUser()
    {
        IsRegisterUserDialogOpen = false;
        RegisterUserErrorMessage = string.Empty;
        NewUserUsername = string.Empty;
        NewUserDisplayName = string.Empty;
        NewUserPassword = string.Empty;
        NewUserPasswordConfirm = string.Empty;
    }

    [RelayCommand]
    private void DismissConnectionError()
    {
        IsConnectionErrorDialogOpen = false;
        ConnectionErrorDialogTitle = "서버 통신 실패";
        ConnectionErrorMessage = string.Empty;
        ConnectionErrorHtml = string.Empty;
        IsConnectionErrorHtml = false;
    }

    [RelayCommand]
    private async Task ConnectToBackendAsync()
    {
        await ConnectToBackendAsync(ConnectionGateApiBaseUrl, saveSettings: true);
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        string apiBaseUrl = (ConnectionGateApiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!ValidateApiBaseUrl(apiBaseUrl, out string? validationError))
        {
            ConnectionGateErrorMessage = validationError ?? "API 주소를 확인하세요.";
            ConnectionGateStatusMessage = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(LoginUsername) || string.IsNullOrWhiteSpace(LoginPassword))
        {
            ConnectionGateErrorMessage = "아이디와 비밀번호를 입력하세요.";
            ConnectionGateStatusMessage = string.Empty;
            return;
        }

        try
        {
            IsConnectionBusy = true;
            ConnectionGateErrorMessage = string.Empty;
            ConnectionGateStatusMessage = string.Empty;
            using WorkLogApiClient client = new(apiBaseUrl);
            await client.CheckConnectionAsync(CancellationToken.None);
            TokenResponse token = await client.LoginAsync(
                new LoginRequest
                {
                    Username = LoginUsername.Trim(),
                    Password = LoginPassword
                },
                CancellationToken.None);

            ApiBaseUrl = apiBaseUrl;
            ConnectionGateApiBaseUrl = apiBaseUrl;
            AccessToken = token.AccessToken;
            RefreshToken = token.RefreshToken;
            using WorkLogApiClient authenticatedClient = new(apiBaseUrl, AccessToken);
            UserDto currentUser = await authenticatedClient.GetCurrentUserAsync(CancellationToken.None);
            CurrentUsername = currentUser.Username;
            CurrentDisplayName = currentUser.DisplayName;
            CurrentUserIsAdmin = currentUser.IsAdmin;
            LoginUsername = currentUser.Username;
            IsBackendConnected = true;
            IsAuthenticated = true;
            await SaveOrClearCredentialsAsync(apiBaseUrl, CurrentUsername, LoginPassword);
            LoginPassword = string.Empty;
            StatusMessage = $"{CurrentUserLabel} 로그인됨.";
            await _settingsService.SaveSettingsAsync(CreateSettingsResult(), CancellationToken.None);
            await LoadTasksAsync();
        }
        catch (Exception error)
        {
            IsAuthenticated = false;
            ShowServerCommunicationFailure(error, "로그인 실패");
            ConnectionGateErrorMessage = CreateConnectionGateErrorMessage(error, "로그인 실패");
            ConnectionGateStatusMessage = string.Empty;
            StatusMessage = "로그인 실패.";
        }
        finally
        {
            IsConnectionBusy = false;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (HasUnsavedTaskChanges || HasUnsavedDailyNoteChanges)
        {
            PreparePendingEditorAction(PendingEditorAction.Logout);
            return;
        }

        await CompleteLogoutAsync();
    }

    private async Task CompleteLogoutAsync()
    {
        AccessToken = string.Empty;
        RefreshToken = string.Empty;
        CurrentUsername = string.Empty;
        CurrentDisplayName = string.Empty;
        CurrentUserIsAdmin = false;
        _activeSaveOperationCount = 0;
        _lastSavedAt = null;
        UpdateLastSaveElapsedLabel();
        IsAuthenticated = false;
        Tasks = new ObservableCollection<TaskDto>();
        IsSettingsDialogOpen = false;
        IsChangePasswordDialogOpen = false;
        IsRegisterUserDialogOpen = false;
        HasUnsavedTaskChanges = false;
        HasUnsavedDailyNoteChanges = false;
        ClearEditor();
        ClearSettingsAccountFields();
        StatusMessage = "로그아웃됨.";
        await _settingsService.SaveSettingsAsync(CreateSettingsResult(), CancellationToken.None);
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(SettingsCurrentPassword) ||
            string.IsNullOrWhiteSpace(SettingsNewPassword) ||
            string.IsNullOrWhiteSpace(SettingsNewPasswordConfirm))
        {
            ChangePasswordErrorMessage = "현재 비밀번호와 새 비밀번호를 모두 입력하세요.";
            return;
        }

        if (SettingsNewPassword != SettingsNewPasswordConfirm)
        {
            ChangePasswordErrorMessage = "새 비밀번호 확인이 일치하지 않습니다.";
            return;
        }

        try
        {
            using WorkLogApiClient client = CreateAuthenticatedClient();
            await client.ChangePasswordAsync(
                new ChangePasswordRequest
                {
                    CurrentPassword = SettingsCurrentPassword,
                    NewPassword = SettingsNewPassword
                },
                CancellationToken.None);

            SettingsCurrentPassword = string.Empty;
            SettingsNewPassword = string.Empty;
            SettingsNewPasswordConfirm = string.Empty;
            ChangePasswordErrorMessage = string.Empty;
            IsChangePasswordDialogOpen = false;
            StatusMessage = "비밀번호가 변경되었습니다.";
        }
        catch (Exception error)
        {
            ChangePasswordErrorMessage = $"비밀번호 변경 실패: {error.Message}";
            ShowServerCommunicationFailure(error, "비밀번호 변경 실패");
        }
    }

    [RelayCommand]
    private async Task RegisterUserAsync()
    {
        if (!CurrentUserIsAdmin)
        {
            RegisterUserErrorMessage = "사용자 추가는 관리자만 할 수 있습니다.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewUserUsername) ||
            string.IsNullOrWhiteSpace(NewUserPassword) ||
            string.IsNullOrWhiteSpace(NewUserPasswordConfirm))
        {
            RegisterUserErrorMessage = "추가할 사용자의 아이디와 비밀번호를 입력하세요.";
            return;
        }

        if (NewUserPassword != NewUserPasswordConfirm)
        {
            RegisterUserErrorMessage = "추가할 사용자의 비밀번호 확인이 일치하지 않습니다.";
            return;
        }

        try
        {
            using WorkLogApiClient client = CreateAuthenticatedClient();
            UserDto user = await client.RegisterAsync(
                new RegisterRequest
                {
                    Username = NewUserUsername.Trim(),
                    Password = NewUserPassword,
                    DisplayName = string.IsNullOrWhiteSpace(NewUserDisplayName)
                        ? null
                        : NewUserDisplayName.Trim()
                },
                CancellationToken.None);

            NewUserUsername = string.Empty;
            NewUserDisplayName = string.Empty;
            NewUserPassword = string.Empty;
            NewUserPasswordConfirm = string.Empty;
            RegisterUserErrorMessage = string.Empty;
            IsRegisterUserDialogOpen = false;
            StatusMessage = $"사용자 추가됨: {user.Username}";
        }
        catch (Exception error)
        {
            RegisterUserErrorMessage = $"사용자 추가 실패: {error.Message}";
            ShowServerCommunicationFailure(error, "사용자 추가 실패");
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        string apiBaseUrl = (SettingsApiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!ValidateApiBaseUrl(apiBaseUrl, out string? validationError))
        {
            SettingsErrorMessage = validationError ?? "API 주소를 확인하세요.";
            return;
        }

        try
        {
            using WorkLogApiClient client = new(apiBaseUrl);
            await client.CheckConnectionAsync(CancellationToken.None);

            bool apiBaseUrlChanged = ApiBaseUrl != apiBaseUrl;
            ApiBaseUrl = apiBaseUrl;
            ConnectionGateApiBaseUrl = apiBaseUrl;
            IsBackendConnected = true;
            ThemeMode = ResolveSettingsThemeMode();
            ApplyThemePreview(ThemeMode);
            await _settingsService.SaveSettingsAsync(CreateSettingsResult(), CancellationToken.None);
            IsSettingsDialogOpen = false;
            SettingsErrorMessage = string.Empty;
            StatusMessage = "설정 저장됨.";

            if (apiBaseUrlChanged)
            {
                await LoadTasksAsync();
            }
        }
        catch (Exception error)
        {
            ShowServerCommunicationFailure(error, "백엔드 연결 확인 실패");
            SettingsErrorMessage = "연결 확인에 실패했습니다.";
            StatusMessage = "설정 저장 실패.";
        }
    }

    private async Task InitializeConnectionAsync()
    {
        await ConnectToBackendAsync(ApiBaseUrl, saveSettings: false);
    }

    private async Task ConnectToBackendAsync(string apiBaseUrlValue, bool saveSettings)
    {
        string apiBaseUrl = (apiBaseUrlValue ?? string.Empty).Trim().TrimEnd('/');
        if (!ValidateApiBaseUrl(apiBaseUrl, out string? validationError))
        {
            ConnectionGateErrorMessage = validationError ?? "API 주소를 확인하세요.";
            ConnectionGateStatusMessage = string.Empty;
            IsBackendConnected = false;
            return;
        }

        try
        {
            IsConnectionBusy = true;
            ConnectionGateErrorMessage = string.Empty;
            ConnectionGateStatusMessage = string.Empty;
            using WorkLogApiClient client = new(apiBaseUrl);
            await client.CheckConnectionAsync(CancellationToken.None);

            ApiBaseUrl = apiBaseUrl;
            ConnectionGateApiBaseUrl = apiBaseUrl;
            IsBackendConnected = true;
            StatusMessage = "백엔드 연결됨.";
            ConnectionGateStatusMessage = "서버 연결이 정상입니다.";

            if (!string.IsNullOrWhiteSpace(AccessToken) || !string.IsNullOrWhiteSpace(RefreshToken))
            {
                try
                {
                    using WorkLogApiClient authenticatedClient = CreateAuthenticatedClient(apiBaseUrl);
                    UserDto currentUser = await authenticatedClient.GetCurrentUserAsync(CancellationToken.None);
                    CurrentUsername = currentUser.Username;
                    CurrentDisplayName = currentUser.DisplayName;
                    CurrentUserIsAdmin = currentUser.IsAdmin;
                    LoginUsername = currentUser.Username;
                    IsAuthenticated = true;
                }
                catch (Exception error)
                {
                    ShowServerCommunicationFailure(error, "저장된 로그인 확인 실패");
                    AccessToken = string.Empty;
                    RefreshToken = string.Empty;
                    CurrentUsername = string.Empty;
                    CurrentDisplayName = string.Empty;
                    CurrentUserIsAdmin = false;
                    IsAuthenticated = false;
                    StatusMessage = "저장된 로그인이 만료되었습니다.";
                }
            }
            else
            {
                IsAuthenticated = false;
                StatusMessage = "로그인이 필요합니다.";
            }

            if (saveSettings)
            {
                await _settingsService.SaveSettingsAsync(CreateSettingsResult(), CancellationToken.None);
            }

            if (IsAuthenticated)
            {
                await LoadTasksAsync();
            }
        }
        catch (Exception error)
        {
            IsBackendConnected = false;
            ShowServerCommunicationFailure(error, "연결 확인 실패");
            ConnectionGateErrorMessage = CreateConnectionGateErrorMessage(error, "연결 확인 실패");
            ConnectionGateStatusMessage = string.Empty;
            StatusMessage = "백엔드 연결 실패.";
        }
        finally
        {
            IsConnectionBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        DateTime defaultDate = SelectedDate ?? DateTime.Today;
        DateTime defaultStartDate = new(defaultDate.Year, defaultDate.Month, 1);
        DateTime defaultEndDate = new(
            defaultDate.Year,
            defaultDate.Month,
            DateTime.DaysInMonth(defaultDate.Year, defaultDate.Month));
        ExportStartDate = defaultStartDate;
        ExportEndDate = defaultEndDate;
        ExportRangeErrorMessage = string.Empty;
        IsExportRangeDialogOpen = true;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void CancelExportRange()
    {
        IsExportRangeDialogOpen = false;
        ExportRangeErrorMessage = string.Empty;
        StatusMessage = "엑셀 내보내기를 취소했습니다.";
    }

    [RelayCommand]
    private async Task ConfirmExportExcelAsync()
    {
        DateTime? startDate = ExportStartDate?.Date;
        DateTime? endDate = ExportEndDate?.Date;
        if (startDate is null || endDate is null)
        {
            ExportRangeErrorMessage = "시작일과 종료일을 모두 선택하세요.";
            return;
        }

        if (startDate > endDate)
        {
            ExportRangeErrorMessage = "시작일은 종료일보다 늦을 수 없습니다.";
            return;
        }

        try
        {
            IsExportRangeDialogOpen = false;
            ExportRangeErrorMessage = string.Empty;
            using WorkLogApiClient client = CreateAuthenticatedClient();
            byte[] content = await client.ExportExcelAsync(startDate.Value, endDate.Value, CancellationToken.None);
            string fileName = $"{startDate:yyyy-MM-dd} - {endDate:yyyy-MM-dd} 업무일지.xlsx";
            string? path = await _exportFileService.SaveExcelAsync(content, fileName, CancellationToken.None);
            StatusMessage = path is null ? "엑셀 저장을 취소했습니다." : $"엑셀 저장됨: {path}";
        }
        catch (Exception error)
        {
            StatusMessage = $"엑셀 내보내기 실패: {error.Message}";
            ShowServerCommunicationFailure(error, "엑셀 내보내기 실패");
        }
    }

    private void SetSettingsThemeMode(string themeMode)
    {
        SettingsUseSystemTheme = themeMode == AppThemeMode.System;
        SettingsUseLightTheme = themeMode == AppThemeMode.Light;
        SettingsUseDarkTheme = themeMode == AppThemeMode.Dark;

        if (!SettingsUseSystemTheme && !SettingsUseLightTheme && !SettingsUseDarkTheme)
        {
            SettingsUseSystemTheme = true;
        }
    }

    private string ResolveSettingsThemeMode()
    {
        if (SettingsUseLightTheme)
        {
            return AppThemeMode.Light;
        }

        if (SettingsUseDarkTheme)
        {
            return AppThemeMode.Dark;
        }

        return AppThemeMode.System;
    }

    private void ApplyThemePreview(string themeMode)
    {
        ActiveThemeVariant = ToThemeVariant(themeMode);
        _settingsService.ApplyThemeMode(themeMode);
    }

    private static ThemeVariant ToThemeVariant(string themeMode)
    {
        return themeMode switch
        {
            AppThemeMode.Light => ThemeVariant.Light,
            AppThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    private static bool ValidateApiBaseUrl(string apiBaseUrl, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            errorMessage = "API 주소를 입력하세요.";
            return false;
        }

        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errorMessage = "http:// 또는 https:// 주소를 입력하세요.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    private void LoadPersistedSettings()
    {
        AppDefaults defaults = AppDefaultsService.Load();
        ApiBaseUrl = defaults.ApiBaseUrl;
        ConnectionGateApiBaseUrl = defaults.ApiBaseUrl;
        LoginUsername = defaults.Username;
        ThemeMode = defaults.ThemeMode;
        ApplyThemePreview(ThemeMode);

        AppSettingsResult? settings = _settingsService.LoadSettings();
        if (settings is null)
        {
            _ = LoadStoredCredentialsAsync();
            return;
        }

        if (CanConfigureApiBaseUrl)
        {
            ApiBaseUrl = settings.ApiBaseUrl;
            ConnectionGateApiBaseUrl = settings.ApiBaseUrl;
        }

        LoginUsername = settings.Username;
        CurrentUsername = settings.Username;
        ThemeMode = settings.ThemeMode;
        ApplyThemePreview(ThemeMode);
        _ = LoadStoredCredentialsAsync();
    }

    private void BeginNewTaskCore(int? parentId, string parentTitle)
    {
        _editorContextVersion++;
        SetSelectedTaskWithoutPrompt(null);
        _activeEditorTask = null;
        UpdateEditorWithoutDirtyTracking(() =>
        {
            IsEditorEnabled = true;
            IsNewTask = true;
            EditorParentId = parentId;
            EditorParentTitle = parentTitle;
            EditorTitle = string.Empty;
            EditorDisplayTitle = string.Empty;
            EditorContent = string.Empty;
            EditorPerformedContent = string.Empty;
            EditorDailyRetrospective = string.Empty;
            EditorPriority = 0;
            EditorStartDate = SelectedDate ?? DateTime.Today;
            EditorDueDate = SelectedDate ?? DateTime.Today;
            EditorActualEndDate = null;
        });
        CaptureEditorBaseline();
        HasUnsavedTaskChanges = false;
        StatusMessage = parentId is null ? "새 최상위 과업을 작성 중입니다." : $"#{parentId}의 하위 과업을 작성 중입니다.";
        OnPropertyChanged(nameof(EditorModeLabel));
    }

    private void BeginEdit(TaskDto task)
    {
        _editorContextVersion++;
        _activeEditorTask = task;
        UpdateEditorWithoutDirtyTracking(() =>
        {
            IsEditorEnabled = true;
            IsNewTask = false;
            EditorParentId = task.ParentId;
            EditorParentTitle = ResolveParentTitle(task.ParentId);
            EditorTitle = task.Title;
            EditorDisplayTitle = task.Title;
            EditorContent = task.Content;
            EditorPriority = task.Priority;
            EditorStartDate = ToPickerDate(task.StartAt);
            EditorDueDate = ToPickerDate(task.DueAt);
            EditorActualEndDate = task.ActualEndAt is null ? null : ToPickerDate(task.ActualEndAt.Value);
            LoadSelectedTaskEntry();
        });
        CaptureEditorBaseline();
        HasUnsavedTaskChanges = false;
        OnPropertyChanged(nameof(EditorModeLabel));
        OnPropertyChanged(nameof(CanEditExistingTask));
    }

    private void ClearEditor()
    {
        _editorContextVersion++;
        SetSelectedTaskWithoutPrompt(null);
        _activeEditorTask = null;
        UpdateEditorWithoutDirtyTracking(() =>
        {
            IsEditorEnabled = false;
            IsNewTask = false;
            EditorParentId = null;
            EditorParentTitle = string.Empty;
            EditorTitle = string.Empty;
            EditorDisplayTitle = string.Empty;
            EditorContent = string.Empty;
            EditorPerformedContent = string.Empty;
            EditorDailyRetrospective = string.Empty;
            EditorPriority = 0;
            EditorActualEndDate = null;
        });
        CaptureEditorBaseline();
        HasUnsavedTaskChanges = false;
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
            Priority = (int)Math.Clamp(EditorPriority, 0, 255),
            StartAt = start.ToUniversalTime(),
            DueAt = due.ToUniversalTime(),
            ActualEndAt = EditorActualEndDate is null
                ? null
                : ToRequestDateTimeOffset(EditorActualEndDate, DateTimeOffset.Now).ToUniversalTime()
        };
    }

    private WorkLogApiClient CreateAuthenticatedClient(string? apiBaseUrl = null)
    {
        if (_apiClientFactory is not null) return _apiClientFactory(apiBaseUrl ?? ApiBaseUrl, AccessToken);
        return new WorkLogApiClient(
            apiBaseUrl ?? ApiBaseUrl,
            AccessToken,
            () => (AccessToken, RefreshToken),
            UpdateTokensAsync);
    }

    private Task UpdateTokensAsync(TokenResponse tokens)
    {
        AccessToken = tokens.AccessToken;
        RefreshToken = tokens.RefreshToken;
        return Task.CompletedTask;
    }

    private void MarkEditorDirty()
    {
        if (!_suppressEditorDirtyTracking && IsEditorEnabled)
        {
            RecomputeEditorDirtyState();
        }
    }

    private void RecomputeEditorDirtyState()
    {
        HasUnsavedTaskChanges = IsEditorEnabled &&
            (EditorTitle != _editorTitleBaseline ||
             EditorContent != _editorContentBaseline ||
             EditorPerformedContent != _editorPerformedContentBaseline ||
             EditorDailyRetrospective != _editorDailyRetrospectiveBaseline ||
             EditorPriority != _editorPriorityBaseline ||
             EditorParentId != _editorParentIdBaseline ||
             EditorStartDate?.Date != _editorStartDateBaseline?.Date ||
             EditorDueDate?.Date != _editorDueDateBaseline?.Date ||
             EditorActualEndDate?.Date != _editorActualEndDateBaseline?.Date);
    }

    private void CaptureEditorBaseline()
    {
        _editorTitleBaseline = EditorTitle;
        _editorContentBaseline = EditorContent;
        _editorPerformedContentBaseline = EditorPerformedContent;
        _editorDailyRetrospectiveBaseline = EditorDailyRetrospective;
        _editorPriorityBaseline = EditorPriority;
        _editorParentIdBaseline = EditorParentId;
        _editorStartDateBaseline = EditorStartDate;
        _editorDueDateBaseline = EditorDueDate;
        _editorActualEndDateBaseline = EditorActualEndDate;
    }

    private void SetDailyNoteContentFromServer(string content)
    {
        _suppressDailyNoteDirtyTracking = true;
        try
        {
            DailyNoteContent = content;
            _dailyNoteBaseline = content;
            HasUnsavedDailyNoteChanges = false;
        }
        finally
        {
            _suppressDailyNoteDirtyTracking = false;
        }
    }

    private void UpdateEditorWithoutDirtyTracking(Action update)
    {
        bool wasSuppressed = _suppressEditorDirtyTracking;
        _suppressEditorDirtyTracking = true;
        try
        {
            update();
        }
        finally
        {
            _suppressEditorDirtyTracking = wasSuppressed;
        }
    }

    private void SetSelectedTaskWithoutPrompt(TaskDto? task)
    {
        bool wasApplyingTaskSelection = _isApplyingTaskSelection;
        _isApplyingTaskSelection = true;
        try
        {
            SelectedTask = task;
        }
        finally
        {
            _isApplyingTaskSelection = wasApplyingTaskSelection;
        }
    }

    private void RestoreActiveTaskSelection()
    {
        SetSelectedTaskWithoutPrompt(IsNewTask ? null : _activeEditorTask);
    }

    private void ApplyTaskSelection(TaskDto? task)
    {
        SetSelectedTaskWithoutPrompt(task);
        if (task is null)
        {
            ClearEditor();
            return;
        }

        BeginEdit(task);
    }

    private void PreparePendingEditorAction(PendingEditorAction action)
    {
        _pendingEditorAction = action;
        if (action != PendingEditorAction.SelectTask)
        {
            _pendingTaskSelection = null;
        }
        if (action != PendingEditorAction.SelectDate)
        {
            _pendingSelectedDate = null;
        }
        IsUnsavedChangesDialogOpen = true;
    }

    private bool PendingActionIncludesDailyNote()
    {
        return _pendingEditorAction is PendingEditorAction.SelectDate
            or PendingEditorAction.Logout
            or PendingEditorAction.CloseWindow;
    }

    private void ClearPendingEditorAction()
    {
        _pendingEditorAction = PendingEditorAction.None;
        _pendingTaskSelection = null;
        _pendingSelectedDate = null;
        _pendingParentTaskId = null;
        _pendingParentTaskTitle = string.Empty;
    }

    private void ExecutePendingEditorAction()
    {
        PendingEditorAction action = _pendingEditorAction;
        TaskDto? task = _pendingTaskSelection;
        DateTime? selectedDate = _pendingSelectedDate;
        int? parentId = _pendingParentTaskId;
        string parentTitle = _pendingParentTaskTitle;
        ClearPendingEditorAction();

        switch (action)
        {
            case PendingEditorAction.SelectTask:
                ApplyTaskSelection(task is null ? null : FindTaskById(Tasks, task.Id) ?? task);
                break;
            case PendingEditorAction.SelectDate:
                SelectedDate = selectedDate;
                break;
            case PendingEditorAction.NewRootTask:
                BeginNewTaskCore(null, string.Empty);
                break;
            case PendingEditorAction.NewChildTask:
                BeginNewTaskCore(parentId, parentTitle);
                break;
            case PendingEditorAction.Logout:
                _ = CompleteLogoutAsync();
                break;
            case PendingEditorAction.CloseWindow:
                CloseWindowRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void BeginSaving()
    {
        _activeSaveOperationCount++;
        LastSaveElapsedLabel = "저장 중...";
    }

    private void MarkSaved()
    {
        _lastSavedAt = DateTimeOffset.Now;
        CompleteSaveOperation();
    }

    private void MarkSaveFailed()
    {
        CompleteSaveOperation();
    }

    private void CompleteSaveOperation()
    {
        _activeSaveOperationCount = Math.Max(0, _activeSaveOperationCount - 1);
        UpdateLastSaveElapsedLabel();
    }

    private void OnLastSaveTimerTick(object? sender, EventArgs eventArgs)
    {
        UpdateLastSaveElapsedLabel();
    }

    private void UpdateLastSaveElapsedLabel()
    {
        if (_activeSaveOperationCount > 0)
        {
            LastSaveElapsedLabel = "저장 중...";
            return;
        }

        if (_lastSavedAt is null)
        {
            LastSaveElapsedLabel = "마지막 저장 없음";
            return;
        }

        long elapsedSeconds = Math.Max(0, (long)(DateTimeOffset.Now - _lastSavedAt.Value).TotalSeconds);
        LastSaveElapsedLabel = $"마지막 저장 {elapsedSeconds}초 전";
    }

    private AppSettingsResult CreateSettingsResult()
    {
        return new AppSettingsResult(ApiBaseUrl, ThemeMode, CurrentUsername);
    }

    private async void OnHeartbeatTimerTick(object? sender, EventArgs eventArgs)
    {
        if (_isHeartbeatCheckRunning ||
            IsConnectionBusy ||
            (!IsBackendConnected && !_heartbeatFailureNotified))
        {
            return;
        }

        _isHeartbeatCheckRunning = true;
        try
        {
            using WorkLogApiClient client = new(
                ApiBaseUrl,
                requestTimeout: HeartbeatRequestTimeout);
            await client.CheckConnectionAsync(CancellationToken.None);
            bool connectionRecovered = _heartbeatFailureNotified;
            IsBackendConnected = true;
            if (connectionRecovered)
            {
                StatusMessage = "서버 연결이 복구되었습니다.";
            }
            _heartbeatFailureNotified = false;
        }
        catch (Exception error)
        {
            StatusMessage = $"서버 연결 확인 실패: {CreateServerCommunicationErrorMessage(error)}";
            if (!_heartbeatFailureNotified)
            {
                _heartbeatFailureNotified = true;
                IsBackendConnected = false;
                ShowServerCommunicationFailure(error, "서버 연결 끊김", suppressIfDialogOpen: true);
            }
        }
        finally
        {
            _isHeartbeatCheckRunning = false;
        }
    }

    private void ShowServerCommunicationFailure(
        Exception error,
        string title,
        bool suppressIfDialogOpen = false)
    {
        if (suppressIfDialogOpen && IsConnectionErrorDialogOpen)
        {
            return;
        }

        ShowConnectionErrorDialog(error, title);
    }

    private void ShowConnectionErrorDialog(Exception error, string title)
    {
        ConnectionErrorDialogTitle = title;
        ConnectionErrorMessage = CreateServerCommunicationErrorMessage(error);
        ConnectionErrorHtml = string.Empty;
        IsConnectionErrorHtml = false;

        if (error is ApiRequestException { IsHtmlResponse: true } apiError)
        {
            ConnectionErrorMessage = $"{(int)apiError.StatusCode} {apiError.ReasonPhrase}";
            ConnectionErrorHtml = apiError.ResponseBody;
            IsConnectionErrorHtml = true;
        }

        IsConnectionErrorDialogOpen = true;
    }

    private static string CreateServerCommunicationErrorMessage(Exception error)
    {
        if (IsTimeoutError(error))
        {
            return "서버 응답 제한 시간을 초과했습니다. 서버 상태와 네트워크 연결을 확인하세요.";
        }

        return error.Message;
    }

    private static bool IsTimeoutError(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException || current is OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    private static string CreateConnectionGateErrorMessage(Exception error, string title)
    {
        if (error is ApiRequestException { IsHtmlResponse: true })
        {
            return $"{title}: 서버가 HTML 오류 페이지를 반환했습니다. 연결 실패 다이얼로그를 확인하세요.";
        }

        return $"{title}: {CreateServerCommunicationErrorMessage(error)}";
    }

    private void ClearSettingsAccountFields()
    {
        ChangePasswordErrorMessage = string.Empty;
        SettingsCurrentPassword = string.Empty;
        SettingsNewPassword = string.Empty;
        SettingsNewPasswordConfirm = string.Empty;
        RegisterUserErrorMessage = string.Empty;
        NewUserUsername = string.Empty;
        NewUserDisplayName = string.Empty;
        NewUserPassword = string.Empty;
        NewUserPasswordConfirm = string.Empty;
    }

    private async Task LoadStoredCredentialsAsync()
    {
        if (!_credentialStore.IsSaveSupported)
        {
            RememberCredentials = false;
            return;
        }

        try
        {
            StoredCredentials? credentials = await _credentialStore.LoadAsync(CancellationToken.None);
            if (credentials is null)
            {
                return;
            }

            ApiBaseUrl = credentials.ApiBaseUrl;
            ConnectionGateApiBaseUrl = credentials.ApiBaseUrl;
            LoginUsername = credentials.Username;
            LoginPassword = credentials.Password;
            RememberCredentials = true;
        }
        catch
        {
            RememberCredentials = false;
        }
    }

    private async Task SaveOrClearCredentialsAsync(string apiBaseUrl, string username, string password)
    {
        if (!_credentialStore.IsSaveSupported)
        {
            return;
        }

        if (!RememberCredentials)
        {
            await _credentialStore.ClearAsync(CancellationToken.None);
            return;
        }

        await _credentialStore.SaveAsync(
            new StoredCredentials(apiBaseUrl, username, password),
            CancellationToken.None);
    }

    private void LoadSelectedTaskEntry()
    {
        UpdateEditorWithoutDirtyTracking(LoadSelectedTaskEntryCore);
        _editorPerformedContentBaseline = EditorPerformedContent;
        _editorDailyRetrospectiveBaseline = EditorDailyRetrospective;
        RecomputeEditorDirtyState();
    }

    private void LoadSelectedTaskEntryCore()
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

    private static IReadOnlyList<TaskDto> SortTasks(IEnumerable<TaskDto> tasks)
    {
        return tasks
            .OrderBy(task => task.Priority <= 0 ? 1 : 0)
            .ThenBy(task => task.DueAt)
            .ThenBy(task => task.Priority)
            .Select(task =>
            {
                task.Children = new ObservableCollection<TaskDto>(SortTasks(task.Children));
                return task;
            })
            .ToArray();
    }

    private void CaptureExpansionState(IEnumerable<TaskDto> tasks)
    {
        foreach (TaskDto task in tasks)
        {
            if (task.IsExpanded)
            {
                _expandedTaskIds.Add(task.Id);
            }
            else
            {
                _expandedTaskIds.Remove(task.Id);
            }

            CaptureExpansionState(task.Children);
        }
    }

    private void ApplyTaskDisplayState(IEnumerable<TaskDto> tasks, DateTime selectedDate)
    {
        DateOnly entryDate = DateOnly.FromDateTime(selectedDate);
        foreach (TaskDto task in tasks)
        {
            task.IsExpanded = _expandedTaskIds.Contains(task.Id);
            WorkEntryDto? entry = task.Entries.FirstOrDefault(item => item.EntryDate == entryDate);
            task.HasPerformedContentForSelectedDate = !string.IsNullOrWhiteSpace(entry?.PerformedContent);
            task.HasRetrospectiveForSelectedDate = !string.IsNullOrWhiteSpace(entry?.Retrospective);
            ApplyTaskDisplayState(task.Children, selectedDate);
        }
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
            return new DateTimeOffset(9999, 12, 31, 23, 59, 59, BackendTimezoneOffset);
        }

        return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Local));
    }
}
