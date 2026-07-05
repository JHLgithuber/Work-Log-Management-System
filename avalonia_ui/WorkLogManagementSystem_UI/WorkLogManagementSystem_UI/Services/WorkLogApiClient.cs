using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WorkLogManagementSystem_UI.Models;

namespace WorkLogManagementSystem_UI.Services;

public sealed class WorkLogApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public WorkLogApiClient(string baseUrl)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(NormalizeBaseUrl(baseUrl)) };
    }

    public async Task<IReadOnlyList<TaskDto>> GetTasksAsync(DateTime targetDate, CancellationToken cancellationToken)
    {
        string dateValue = Uri.EscapeDataString(targetDate.ToString("yyyy-MM-dd"));
        IReadOnlyList<TaskDto>? tasks = await _httpClient.GetFromJsonAsync<IReadOnlyList<TaskDto>>(
            $"/tasks?target_date={dateValue}",
            JsonOptions,
            cancellationToken);
        return tasks ?? Array.Empty<TaskDto>();
    }

    public async Task<TaskDto> CreateTaskAsync(TaskWriteRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/tasks", request, JsonOptions, cancellationToken);
        return await ReadTaskResponseAsync(response, cancellationToken);
    }

    public async Task<TaskDto> UpdateTaskAsync(int taskId, TaskWriteRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"/tasks/{taskId}", request, JsonOptions, cancellationToken);
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
        WorkEntryDto? entry = await _httpClient.GetFromJsonAsync<WorkEntryDto>(
            $"/tasks/{taskId}/entries/{dateValue}",
            JsonOptions,
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
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        WorkEntryDto? entry = await response.Content.ReadFromJsonAsync<WorkEntryDto>(JsonOptions, cancellationToken);
        return entry ?? throw new InvalidOperationException("The backend returned an empty work entry response.");
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
        TaskDto? task = await response.Content.ReadFromJsonAsync<TaskDto>(JsonOptions, cancellationToken);
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
        throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {message}");
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
