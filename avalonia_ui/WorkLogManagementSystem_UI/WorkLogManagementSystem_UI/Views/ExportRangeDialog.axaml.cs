using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WorkLogManagementSystem_UI.Services;

namespace WorkLogManagementSystem_UI.Views;

public partial class ExportRangeDialog : Window
{
    public ExportRangeDialog()
    {
        InitializeComponent();
    }

    public ExportRangeDialog(DateTime? startDate, DateTime? endDate)
        : this()
    {
        DateTime fallbackDate = DateTime.Today;
        StartDatePicker.SelectedDate = (startDate ?? fallbackDate).Date;
        EndDatePicker.SelectedDate = (endDate ?? startDate ?? fallbackDate).Date;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs eventArgs)
    {
        Close(null);
    }

    private void OnExportClicked(object? sender, RoutedEventArgs eventArgs)
    {
        DateTime? startDate = StartDatePicker.SelectedDate?.Date;
        DateTime? endDate = EndDatePicker.SelectedDate?.Date;
        if (startDate is null || endDate is null)
        {
            ShowError("시작일과 종료일을 모두 선택하세요.");
            return;
        }

        if (startDate > endDate)
        {
            ShowError("시작일은 종료일보다 늦을 수 없습니다.");
            return;
        }

        Close(new ExportDateRange(startDate.Value, endDate.Value));
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
