using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WorkLogManagementSystem_UI.Models;

namespace WorkLogManagementSystem_UI.Services;

public sealed class WorkLogApiClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public WorkLogApiClient(
        string baseUrl,
        string? accessToken = null,
        Func<(string AccessToken, string RefreshToken)>? tokenProvider = null,
        Func<TokenResponse, Task>? tokenUpdated = null)
    {
        Uri baseAddress = new(NormalizeBaseUrl(baseUrl));
        _httpClient = tokenProvider is not null && tokenUpdated is not null
            ? new HttpClient(new RefreshingAuthHandler(baseAddress, tokenProvider, tokenUpdated))
            : new HttpClient();
        _httpClient.BaseAddress = baseAddress;
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        }
    }

    public async Task CheckConnectionAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync("/health", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        throw new HttpRequestException(
            $"The backend health check returned {(int)response.StatusCode} {response.ReasonPhrase}; expected 204 No Content.");
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            "/auth/login",
            request,
            AppJsonSerializerContext.TypeInfo<LoginRequest>(),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        TokenResponse? token = await response.Content.ReadFromJsonAsync(
            AppJsonSerializerContext.TypeInfo<TokenResponse>(),
            cancellationToken);
        return token ?? throw new InvalidOperationException("The backend returned an empty login response.");
    }

    public async Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            "/auth/refresh",
            new RefreshTokenRequest { RefreshToken = refreshToken },
            AppJsonSerializerContext.TypeInfo<RefreshTokenRequest>(),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        TokenResponse? token = await response.Content.ReadFromJsonAsync(
            AppJsonSerializerContext.TypeInfo<TokenResponse>(),
            cancellationToken);
        return token ?? throw new InvalidOperationException("The backend returned an empty refresh response.");
    }

    public async Task<UserDto> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        UserDto? user = await _httpClient.GetFromJsonAsync(
            "/auth/me",
            AppJsonSerializerContext.TypeInfo<UserDto>(),
            cancellationToken);
        return user ?? throw new InvalidOperationException("The backend returned an empty user response.");
    }

    public async Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            "/auth/register",
            request,
            AppJsonSerializerContext.TypeInfo<RegisterRequest>(),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        UserDto? user = await response.Content.ReadFromJsonAsync(
            AppJsonSerializerContext.TypeInfo<UserDto>(),
            cancellationToken);
        return user ?? throw new InvalidOperationException("The backend returned an empty user response.");
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
            "/auth/password",
            request,
            AppJsonSerializerContext.TypeInfo<ChangePasswordRequest>(),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<TaskDto>> GetTasksAsync(DateTime targetDate, CancellationToken cancellationToken)
    {
        string dateValue = Uri.EscapeDataString(targetDate.ToString("yyyy-MM-dd"));
        List<TaskDto>? tasks = await _httpClient.GetFromJsonAsync(
            $"/tasks?target_date={dateValue}",
            AppJsonSerializerContext.TypeInfo<List<TaskDto>>(),
            cancellationToken);
        return tasks ?? new List<TaskDto>();
    }

    public async Task<TaskDto> CreateTaskAsync(TaskWriteRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            "/tasks",
            request,
            AppJsonSerializerContext.TypeInfo<TaskWriteRequest>(),
            cancellationToken);
        return await ReadTaskResponseAsync(response, cancellationToken);
    }

    public async Task<TaskDto> UpdateTaskAsync(int taskId, TaskWriteRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
            $"/tasks/{taskId}",
            request,
            AppJsonSerializerContext.TypeInfo<TaskWriteRequest>(),
            cancellationToken);
        return await ReadTaskResponseAsync(response, cancellationToken);
    }

    public async Task DeleteTaskAsync(int taskId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.DeleteAsync($"/tasks/{taskId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<WorkEntryDto> GetWorkEntryAsync(
        int taskId,
        DateTime entryDate,
        CancellationToken cancellationToken)
    {
        string dateValue = Uri.EscapeDataString(entryDate.ToString("yyyy-MM-dd"));
        WorkEntryDto? entry = await _httpClient.GetFromJsonAsync(
            $"/tasks/{taskId}/entries/{dateValue}",
            AppJsonSerializerContext.TypeInfo<WorkEntryDto>(),
            cancellationToken);
        return entry ?? throw new InvalidOperationException("The backend returned an empty work entry response.");
    }

    public async Task<WorkEntryDto> SaveWorkEntryAsync(
        int taskId,
        DateTime entryDate,
        WorkEntryWriteRequest request,
        CancellationToken cancellationToken)
    {
        string dateValue = Uri.EscapeDataString(entryDate.ToString("yyyy-MM-dd"));
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
            $"/tasks/{taskId}/entries/{dateValue}",
            request,
            AppJsonSerializerContext.TypeInfo<WorkEntryWriteRequest>(),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        WorkEntryDto? entry = await response.Content.ReadFromJsonAsync(
            AppJsonSerializerContext.TypeInfo<WorkEntryDto>(),
            cancellationToken);
        return entry ?? throw new InvalidOperationException("The backend returned an empty work entry response.");
    }

    public async Task<DailyNoteDto> GetDailyNoteAsync(DateTime noteDate, CancellationToken cancellationToken)
    {
        string dateValue = Uri.EscapeDataString(noteDate.ToString("yyyy-MM-dd"));
        DailyNoteDto? note = await _httpClient.GetFromJsonAsync(
            $"/tasks/daily-notes/{dateValue}",
            AppJsonSerializerContext.TypeInfo<DailyNoteDto>(),
            cancellationToken);
        return note ?? throw new InvalidOperationException("The backend returned an empty daily note response.");
    }

    public async Task<DailyNoteDto> SaveDailyNoteAsync(
        DateTime noteDate,
        DailyNoteWriteRequest request,
        CancellationToken cancellationToken)
    {
        string dateValue = Uri.EscapeDataString(noteDate.ToString("yyyy-MM-dd"));
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
            $"/tasks/daily-notes/{dateValue}",
            request,
            AppJsonSerializerContext.TypeInfo<DailyNoteWriteRequest>(),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        DailyNoteDto? note = await response.Content.ReadFromJsonAsync(
            AppJsonSerializerContext.TypeInfo<DailyNoteDto>(),
            cancellationToken);
        return note ?? throw new InvalidOperationException("The backend returned an empty daily note response.");
    }

    public async Task<byte[]> ExportExcelAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        string startDateValue = Uri.EscapeDataString(startDate.ToString("yyyy-MM-dd"));
        string endDateValue = Uri.EscapeDataString(endDate.ToString("yyyy-MM-dd"));
        using HttpResponseMessage response = await _httpClient.GetAsync(
            $"/tasks/export.xlsx?start_date={startDateValue}&end_date={endDateValue}",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "http://localhost:8000";
        }

        return baseUrl.Trim().TrimEnd('/');
    }

    private static async Task<TaskDto> ReadTaskResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        TaskDto? task = await response.Content.ReadFromJsonAsync(
            AppJsonSerializerContext.TypeInfo<TaskDto>(),
            cancellationToken);
        return task ?? throw new InvalidOperationException("The backend returned an empty task response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        string message = TryExtractDetail(body) ?? body;
        throw new ApiRequestException(
            response.StatusCode,
            response.ReasonPhrase,
            body,
            response.Content.Headers.ContentType?.MediaType,
            $"{(int)response.StatusCode} {response.ReasonPhrase}: {message}");
    }

    private static string? TryExtractDetail(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("detail", out JsonElement detail))
            {
                return detail.ValueKind == JsonValueKind.String ? detail.GetString() : detail.ToString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
