using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace WorkLogManagementSystem_UI.Models;

public sealed class TaskDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("parent_id")]
    public int? ParentId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("start_at")]
    public DateTimeOffset StartAt { get; set; }

    [JsonPropertyName("due_at")]
    public DateTimeOffset DueAt { get; set; }

    [JsonPropertyName("actual_end_at")]
    public DateTimeOffset? ActualEndAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("entries")]
    public ObservableCollection<WorkEntryDto> Entries { get; set; } = new();

    [JsonPropertyName("children")]
    public ObservableCollection<TaskDto> Children { get; set; } = new();

    [JsonIgnore]
    public string DateSummary => $"{FormatDate(StartAt)} - {FormatDate(ActualEndAt ?? DueAt)}";

    private static string FormatDate(DateTimeOffset value)
    {
        if (value.Year <= 1)
        {
            return "시작 무제한";
        }

        if (value.Year >= 9999)
        {
            return "종료 무제한";
        }

        return value.ToLocalTime().ToString("yyyy-MM-dd");
    }
}

public sealed class WorkEntryDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("task_id")]
    public int TaskId { get; set; }

    [JsonPropertyName("entry_date")]
    public DateOnly EntryDate { get; set; }

    [JsonPropertyName("performed_content")]
    public string PerformedContent { get; set; } = string.Empty;

    [JsonPropertyName("retrospective")]
    public string Retrospective { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TaskWriteRequest
{
    [JsonPropertyName("parent_id")]
    public int? ParentId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("start_at")]
    public DateTimeOffset StartAt { get; set; }

    [JsonPropertyName("due_at")]
    public DateTimeOffset DueAt { get; set; }

    [JsonPropertyName("actual_end_at")]
    public DateTimeOffset? ActualEndAt { get; set; }
}

public sealed class WorkEntryWriteRequest
{
    [JsonPropertyName("performed_content")]
    public string PerformedContent { get; set; } = string.Empty;

    [JsonPropertyName("retrospective")]
    public string Retrospective { get; set; } = string.Empty;
}
