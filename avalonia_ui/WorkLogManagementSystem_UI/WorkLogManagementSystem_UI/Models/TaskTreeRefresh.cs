using System.Collections.Generic;
namespace WorkLogManagementSystem_UI.Models;

// Only skip a refresh when the complete server snapshot is unchanged.
// Any field, entry, order or hierarchy change keeps the existing replacement path.
internal static class TaskTreeRefresh
{
    internal static bool IsUnchanged(IReadOnlyList<TaskDto> current, IReadOnlyList<TaskDto> incoming)
    {
        if (current.Count != incoming.Count) return false;
        for (int i = 0; i < current.Count; i++)
            if (!SameRow(current[i], incoming[i]) || !IsUnchanged(current[i].Children, incoming[i].Children))
                return false;
        return true;
    }

    private static bool SameRow(TaskDto a, TaskDto b)
    {
        if (a.Id != b.Id || a.ParentId != b.ParentId || a.Title != b.Title || a.Content != b.Content ||
            a.Priority != b.Priority || a.StartAt != b.StartAt || a.DueAt != b.DueAt ||
            a.ActualEndAt != b.ActualEndAt || a.CreatedAt != b.CreatedAt || a.UpdatedAt != b.UpdatedAt ||
            a.IsExpanded != b.IsExpanded ||
            a.HasPerformedContentForSelectedDate != b.HasPerformedContentForSelectedDate ||
            a.HasRetrospectiveForSelectedDate != b.HasRetrospectiveForSelectedDate || a.Entries.Count != b.Entries.Count)
            return false;
        for (int i = 0; i < a.Entries.Count; i++)
        {
            var x = a.Entries[i]; var y = b.Entries[i];
            if (x.Id != y.Id || x.TaskId != y.TaskId || x.EntryDate != y.EntryDate ||
                x.PerformedContent != y.PerformedContent || x.Retrospective != y.Retrospective ||
                x.CreatedAt != y.CreatedAt || x.UpdatedAt != y.UpdatedAt) return false;
        }
        return true;
    }
}
