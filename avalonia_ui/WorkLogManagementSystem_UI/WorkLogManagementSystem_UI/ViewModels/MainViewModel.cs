using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Styling;
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
    private readonly ICredentialStore _credentialStore = PlatformCredentialStore.Create();
    private CancellationTokenSource? _loadCancellationTokenSource;

    [ObservableProperty] private string _apiBaseUrl = AppDefaultsService.Load().ApiBaseUrl;
    [ObservableProperty] private string _accessToken = string.Empty;
    [ObservableProperty] private string _currentUsername = string.Empty;
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
    [ObservableProperty] private bool _isConnectionErrorDialogOpen;
    [ObservableProperty] private string _connectionErrorMessage = string.Empty;
    [ObservableProperty] private bool _isBackendConnected;
    [ObservableProperty] private bool _isConnectionBusy;
    [ObservableProperty] private string _connectionGateApiBaseUrl = string.Empty;
    [ObservableProperty] private string _connectionGateErrorMessage = string.Empty;
    [ObservableProperty] private bool _isExportRangeDialogOpen;
    [ObservableProperty] private DateTime? _exportStartDate = DateTime.Today;
    [ObservableProperty] private DateTime? _exportEndDate = DateTime.Today;
    [ObservableProperty] private string _exportRangeErrorMessage = string.Empty;
    [ObservableProperty] private bool _isAuthenticated;

    public MainViewModel()
    {
        LoadPersistedSettings();
        ConnectionGateApiBaseUrl = ApiBaseUrl;
        _ = InitializeConnectionAsync();
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
    public bool HasSettingsErrorMessage => !string.IsNullOrWhiteSpace(SettingsErrorMessage);
    public bool IsConnectionGateVisible => !IsBackendConnected || !IsAuthenticated;
    public bool IsLoggedInStatusVisible => IsBackendConnected && IsAuthenticated;
    public bool HasConnectionGateErrorMessage => !string.IsNullOrWhiteSpace(ConnectionGateErrorMessage);
    public bool HasExportRangeErrorMessage => !string.IsNullOrWhiteSpace(ExportRangeErrorMessage);
    public bool CanEditExistingTask => IsEditorEnabled && !IsNewTask && SelectedTask is not null;
    public bool IsCredentialSaveSupported => _credentialStore.IsSaveSupported;
    public string CredentialSaveUnsupportedReason => _credentialStore.UnsupportedReason;

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

    partial void OnSettingsErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasSettingsErrorMessage));
    }

    partial void OnIsBackendConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsConnectionGateVisible));
        OnPropertyChanged(nameof(IsLoggedInStatusVisible));
    }

    partial void OnIsAuthenticatedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsConnectionGateVisible));
        OnPropertyChanged(nameof(IsLoggedInStatusVisible));
    }

    partial void OnConnectionGateErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasConnectionGateErrorMessage));
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
    private async Task LoadTasksAsync()
    {
        if (SelectedDate is null || !IsBackendConnected || !IsAuthenticated)
        {
            return;
        }

        _loadCancellationTokenSource?.Cancel();
        _loadCancellationTokenSource = new CancellationTokenSource();
        CancellationToken cancellationToken = _loadCancellationTokenSource.Token;

        try
        {
            using WorkLogApiClient client = CreateAuthenticatedClient();
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
            using WorkLogApiClient client = CreateAuthenticatedClient();
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
            using WorkLogApiClient client = CreateAuthenticatedClient();
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
    private void OpenSettings()
    {
        SettingsApiBaseUrl = ApiBaseUrl;
        SettingsErrorMessage = string.Empty;
        SetSettingsThemeMode(ThemeMode);
        IsSettingsDialogOpen = true;
    }

    [RelayCommand]
    private void CancelSettings()
    {
        IsSettingsDialogOpen = false;
        SettingsErrorMessage = string.Empty;
        ApplyThemePreview(ThemeMode);
    }

    [RelayCommand]
    private void DismissConnectionError()
    {
        IsConnectionErrorDialogOpen = false;
        ConnectionErrorMessage = string.Empty;
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
            return;
        }

        if (string.IsNullOrWhiteSpace(LoginUsername) || string.IsNullOrWhiteSpace(LoginPassword))
        {
            ConnectionGateErrorMessage = "아이디와 비밀번호를 입력하세요.";
            return;
        }

        try
        {
            IsConnectionBusy = true;
            ConnectionGateErrorMessage = string.Empty;
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
            CurrentUsername = LoginUsername.Trim();
            IsBackendConnected = true;
            IsAuthenticated = true;
            await SaveOrClearCredentialsAsync(apiBaseUrl, CurrentUsername, LoginPassword);
            LoginPassword = string.Empty;
            StatusMessage = $"{CurrentUsername} 로그인됨.";
            await _settingsService.SaveSettingsAsync(CreateSettingsResult(), CancellationToken.None);
            await LoadTasksAsync();
        }
        catch (Exception error)
        {
            IsAuthenticated = false;
            ConnectionGateErrorMessage = $"로그인 실패: {error.Message}";
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
        AccessToken = string.Empty;
        CurrentUsername = string.Empty;
        IsAuthenticated = false;
        Tasks = new ObservableCollection<TaskDto>();
        SelectedTask = null;
        ClearEditor();
        StatusMessage = "로그아웃됨.";
        await _settingsService.SaveSettingsAsync(CreateSettingsResult(), CancellationToken.None);
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
            ConnectionErrorMessage = $"백엔드 연결 확인에 실패했습니다.\n{error.Message}";
            IsConnectionErrorDialogOpen = true;
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
            IsBackendConnected = false;
            return;
        }

        try
        {
            IsConnectionBusy = true;
            ConnectionGateErrorMessage = string.Empty;
            using WorkLogApiClient client = new(apiBaseUrl);
            await client.CheckConnectionAsync(CancellationToken.None);

            ApiBaseUrl = apiBaseUrl;
            ConnectionGateApiBaseUrl = apiBaseUrl;
            IsBackendConnected = true;
            StatusMessage = "백엔드 연결됨.";

            if (!string.IsNullOrWhiteSpace(AccessToken))
            {
                try
                {
                    using WorkLogApiClient authenticatedClient = new(apiBaseUrl, AccessToken);
                    UserDto currentUser = await authenticatedClient.GetCurrentUserAsync(CancellationToken.None);
                    CurrentUsername = currentUser.Username;
                    LoginUsername = currentUser.Username;
                    IsAuthenticated = true;
                }
                catch
                {
                    AccessToken = string.Empty;
                    CurrentUsername = string.Empty;
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
            ConnectionGateErrorMessage = $"연결 확인 실패: {error.Message}";
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
            StatusMessage = path is null ? "엑셀 저장을 취소했습니다." : string.Empty;
        }
        catch (Exception error)
        {
            StatusMessage = $"엑셀 내보내기 실패: {error.Message}";
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

        ApiBaseUrl = settings.ApiBaseUrl;
        ConnectionGateApiBaseUrl = settings.ApiBaseUrl;
        AccessToken = settings.AccessToken;
        LoginUsername = settings.Username;
        CurrentUsername = settings.Username;
        ThemeMode = settings.ThemeMode;
        ApplyThemePreview(ThemeMode);
        _ = LoadStoredCredentialsAsync();
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
        EditorPriority = 0;
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
        EditorPriority = task.Priority;
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
        EditorPriority = 0;
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
            Priority = (int)Math.Clamp(EditorPriority, 0, 255),
            StartAt = start.ToUniversalTime(),
            DueAt = due.ToUniversalTime(),
            ActualEndAt = EditorActualEndDate is null
                ? null
                : ToRequestDateTimeOffset(EditorActualEndDate, DateTimeOffset.Now).ToUniversalTime()
        };
    }

    private WorkLogApiClient CreateAuthenticatedClient()
    {
        return new WorkLogApiClient(ApiBaseUrl, AccessToken);
    }

    private AppSettingsResult CreateSettingsResult()
    {
        return new AppSettingsResult(ApiBaseUrl, ThemeMode, AccessToken, CurrentUsername);
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
