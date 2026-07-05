using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace WorkLogManagementSystem_UI.Views;

public partial class MainView : UserControl
{
    private const double NarrowWidthThreshold = 760;
    private const double CompactWidthThreshold = 420;
    private const double CompactToolbarThreshold = 660;
    private const double ThreeColumnDateThreshold = 720;
    private const double TwoColumnDateThreshold = 390;
    private const double MinimumTaskPaneWidth = 220;
    private const double PreferredTaskPaneWidth = 320;
    private const int TaskPaneAnimationDurationMilliseconds = 180;
    private bool _isNarrowLayout;
    private bool _isTaskPaneOpen = true;

    public MainView()
    {
        InitializeComponent();
        EditorPanel.SetValue(Panel.ZIndexProperty, 0);
        TaskPaneOverlay.SetValue(Panel.ZIndexProperty, 1);
        TaskListPanel.SetValue(Panel.ZIndexProperty, 2);
        SizeChanged += OnSizeChanged;
        UpdateResponsiveLayout(Bounds.Width);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        UpdateResponsiveLayout(eventArgs.NewSize.Width);
    }

    private void UpdateResponsiveLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        bool shouldUseNarrowLayout = width < NarrowWidthThreshold;
        if (shouldUseNarrowLayout != _isNarrowLayout)
        {
            _isNarrowLayout = shouldUseNarrowLayout;
            if (_isNarrowLayout)
            {
                _isTaskPaneOpen = false;
            }
        }

        if (_isNarrowLayout)
        {
            double shellMargin = width < CompactWidthThreshold ? 6 : 10;
            ResponsiveShell.ColumnDefinitions = new ColumnDefinitions("*");
            ResponsiveShell.RowDefinitions = new RowDefinitions("*");
            ResponsiveShell.Margin = new Avalonia.Thickness(0);
            Grid.SetColumn(TaskListPanel, 0);
            Grid.SetRow(TaskListPanel, 0);
            Grid.SetColumn(EditorPanel, 0);
            Grid.SetRow(EditorPanel, 0);
            TaskListPanel.Width = Math.Max(200, Math.Min(PreferredTaskPaneWidth, width - 12));
            TaskListPanel.Margin = new Avalonia.Thickness(0);
            TaskListPanel.CornerRadius = new CornerRadius(0, 8, 8, 0);
            TaskListPanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            TaskListPanel.IsVisible = _isTaskPaneOpen;
            TaskListPanel.RenderTransform = new TranslateTransform(_isTaskPaneOpen ? 0 : -TaskListPanel.Width, 0);
            TaskPaneOverlay.IsVisible = _isTaskPaneOpen;
            EditorPanel.Margin = new Avalonia.Thickness(shellMargin);
            TaskPaneToggleButton.IsVisible = true;
            TaskPaneCloseButton.IsVisible = true;
            ApplyCompactToolbar(width);
            ApplyCompactEditor(width - (shellMargin * 2) - 32);
        }
        else
        {
            double paneWidth = Math.Max(MinimumTaskPaneWidth, Math.Min(PreferredTaskPaneWidth, width * 0.32));
            double editorContentWidth = width - 32 - 16 - paneWidth - 32;
            ResponsiveShell.ColumnDefinitions = new ColumnDefinitions(
                $"{paneWidth.ToString(CultureInfo.InvariantCulture)},*");
            ResponsiveShell.RowDefinitions = new RowDefinitions("*");
            ResponsiveShell.Margin = new Avalonia.Thickness(16);
            Grid.SetColumn(TaskListPanel, 0);
            Grid.SetRow(TaskListPanel, 0);
            Grid.SetColumn(EditorPanel, 1);
            Grid.SetRow(EditorPanel, 0);
            TaskListPanel.Width = double.NaN;
            TaskListPanel.Margin = new Avalonia.Thickness(0);
            TaskListPanel.CornerRadius = new CornerRadius(8);
            TaskListPanel.RenderTransform = new TranslateTransform(0, 0);
            TaskListPanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            TaskListPanel.IsVisible = true;
            TaskPaneOverlay.IsVisible = false;
            EditorPanel.Margin = new Avalonia.Thickness(0);
            TaskPaneToggleButton.IsVisible = false;
            TaskPaneCloseButton.IsVisible = false;
            ApplyRegularToolbar();
            TaskListDatePicker.MinWidth = 170;
            ApplyEditorDateLayout(editorContentWidth);
        }
    }

    private void ApplyRegularToolbar()
    {
        TopBarShell.ColumnDefinitions = new ColumnDefinitions("*,Auto");
        TopBarShell.RowDefinitions = new RowDefinitions("Auto");
        Grid.SetColumn(TopBarInfoGroup, 0);
        Grid.SetRow(TopBarInfoGroup, 0);
        Grid.SetColumn(TopBarActionGroup, 1);
        Grid.SetRow(TopBarActionGroup, 0);
        TopBarActionGroup.Margin = new Avalonia.Thickness(0);
        TopBar.Padding = new Avalonia.Thickness(10, 8);
        AppTitleText.FontSize = 22;
        CurrentScopeText.IsVisible = true;
    }

    private void ApplyCompactToolbar(double width)
    {
        bool phoneWidth = width < CompactWidthThreshold;
        bool stackedToolbar = width < CompactToolbarThreshold;
        TopBarShell.ColumnDefinitions = stackedToolbar ? new ColumnDefinitions("*") : new ColumnDefinitions("*,Auto");
        TopBarShell.RowDefinitions = stackedToolbar ? new RowDefinitions("Auto,Auto") : new RowDefinitions("Auto");
        Grid.SetColumn(TopBarInfoGroup, 0);
        Grid.SetRow(TopBarInfoGroup, 0);
        Grid.SetColumn(TopBarActionGroup, stackedToolbar ? 0 : 1);
        Grid.SetRow(TopBarActionGroup, stackedToolbar ? 1 : 0);
        TopBarActionGroup.Margin = stackedToolbar ? new Avalonia.Thickness(0, 8, 0, 0) : new Avalonia.Thickness(0);

        TopBar.Padding = phoneWidth ? new Avalonia.Thickness(8, 6) : new Avalonia.Thickness(10, 8);
        AppTitleText.FontSize = phoneWidth ? 18 : 20;
        CurrentScopeText.IsVisible = !phoneWidth;
    }

    private void ApplyCompactEditor(double editorContentWidth)
    {
        TaskListDatePicker.MinWidth = 170;
        ApplyEditorDateLayout(editorContentWidth);
    }

    private void ApplyEditorDateLayout(double editorContentWidth)
    {
        StartDatePanel.Width = double.NaN;
        DueDatePanel.Width = double.NaN;
        ActualEndDatePanel.Width = double.NaN;

        if (editorContentWidth < TwoColumnDateThreshold)
        {
            EditorDateGrid.ColumnDefinitions = new ColumnDefinitions("*");
            EditorDateGrid.RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
            EditorDateGrid.ColumnSpacing = 0;
            EditorDateGrid.RowSpacing = 10;
            Grid.SetColumn(StartDatePanel, 0);
            Grid.SetRow(StartDatePanel, 0);
            Grid.SetColumn(DueDatePanel, 0);
            Grid.SetRow(DueDatePanel, 1);
            Grid.SetColumn(ActualEndDatePanel, 0);
            Grid.SetRow(ActualEndDatePanel, 2);
            Grid.SetColumnSpan(StartDatePanel, 1);
            Grid.SetColumnSpan(DueDatePanel, 1);
            Grid.SetColumnSpan(ActualEndDatePanel, 1);
            return;
        }

        if (editorContentWidth < ThreeColumnDateThreshold)
        {
            EditorDateGrid.ColumnDefinitions = new ColumnDefinitions("*,*");
            EditorDateGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            EditorDateGrid.ColumnSpacing = 10;
            EditorDateGrid.RowSpacing = 10;
            Grid.SetColumn(StartDatePanel, 0);
            Grid.SetRow(StartDatePanel, 0);
            Grid.SetColumn(DueDatePanel, 1);
            Grid.SetRow(DueDatePanel, 0);
            Grid.SetColumn(ActualEndDatePanel, 0);
            Grid.SetRow(ActualEndDatePanel, 1);
            Grid.SetColumnSpan(StartDatePanel, 1);
            Grid.SetColumnSpan(DueDatePanel, 1);
            Grid.SetColumnSpan(ActualEndDatePanel, 2);
            return;
        }

        EditorDateGrid.ColumnDefinitions = new ColumnDefinitions("*,*,*");
        EditorDateGrid.RowDefinitions = new RowDefinitions("Auto");
        EditorDateGrid.ColumnSpacing = 10;
        EditorDateGrid.RowSpacing = 0;
        Grid.SetColumn(StartDatePanel, 0);
        Grid.SetRow(StartDatePanel, 0);
        Grid.SetColumn(DueDatePanel, 1);
        Grid.SetRow(DueDatePanel, 0);
        Grid.SetColumn(ActualEndDatePanel, 2);
        Grid.SetRow(ActualEndDatePanel, 0);
        Grid.SetColumnSpan(StartDatePanel, 1);
        Grid.SetColumnSpan(DueDatePanel, 1);
        Grid.SetColumnSpan(ActualEndDatePanel, 1);
    }

    private async void OnTaskPaneToggleClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (!_isNarrowLayout)
        {
            return;
        }

        if (_isTaskPaneOpen)
        {
            await CloseTaskPaneAsync();
            return;
        }

        await OpenTaskPaneAsync();
    }

    private async void OnTaskPaneCloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        await CloseTaskPaneAsync();
    }

    private async void OnTaskPaneOverlayPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        await CloseTaskPaneAsync();
    }

    private async Task OpenTaskPaneAsync()
    {
        if (!_isNarrowLayout)
        {
            return;
        }

        _isTaskPaneOpen = true;
        UpdateResponsiveLayout(Bounds.Width);
        await AnimateTaskPaneAsync(opening: true);
    }

    private async Task CloseTaskPaneAsync()
    {
        if (!_isNarrowLayout || !_isTaskPaneOpen)
        {
            return;
        }

        await AnimateTaskPaneAsync(opening: false);
        _isTaskPaneOpen = false;
        UpdateResponsiveLayout(Bounds.Width);
    }

    private async Task AnimateTaskPaneAsync(bool opening)
    {
        double paneWidth = TaskListPanel.Bounds.Width > 0 ? TaskListPanel.Bounds.Width : TaskListPanel.Width;
        if (paneWidth <= 0 || double.IsNaN(paneWidth))
        {
            paneWidth = PreferredTaskPaneWidth;
        }

        TranslateTransform transform = TaskListPanel.RenderTransform as TranslateTransform ?? new TranslateTransform();
        TaskListPanel.RenderTransform = transform;

        double start = opening ? -paneWidth : 0;
        double end = opening ? 0 : -paneWidth;
        int frameCount = Math.Max(1, TaskPaneAnimationDurationMilliseconds / 16);
        for (int frame = 0; frame <= frameCount; frame++)
        {
            double progress = frame / (double)frameCount;
            double easedProgress = 1 - Math.Pow(1 - progress, 3);
            transform.X = start + ((end - start) * easedProgress);
            await Task.Delay(16);
        }

        transform.X = end;
    }
}
