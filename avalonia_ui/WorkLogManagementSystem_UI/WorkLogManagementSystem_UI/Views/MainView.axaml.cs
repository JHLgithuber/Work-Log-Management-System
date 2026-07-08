using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using WorkLogManagementSystem_UI.Services;
using WorkLogManagementSystem_UI.ViewModels;

namespace WorkLogManagementSystem_UI.Views;

public partial class MainView : UserControl
{
    private const double NarrowWidthThreshold = 660;
    private const double CompactWidthThreshold = 420;
    private const double ThreeColumnDateThreshold = 720;
    private const double TwoColumnDateThreshold = 390;
    private const double MinimumTaskPaneWidth = 220;
    private const double PreferredTaskPaneWidth = 320;
    private const double PreferredSettingsModalWidth = 560;
    private const double ModalViewportPadding = 24;
    private const int TaskPaneAnimationDurationMilliseconds = 180;
    private bool _isNarrowLayout;
    private bool _isTaskPaneOpen = true;
    private bool _isTaskPaneExpanded;
    private MainViewModel? _subscribedViewModel;

    public MainView()
    {
        InitializeComponent();
        EditorPanel.SetValue(Panel.ZIndexProperty, 0);
        TaskPaneOverlay.SetValue(Panel.ZIndexProperty, 1);
        TaskListPanel.SetValue(Panel.ZIndexProperty, 2);
        ExpandTaskPaneButton.Click += OnExpandTaskPaneClicked;
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
        AppSettingsService.ThemeResourcesChanged += OnThemeResourcesChanged;
        UpdateResponsiveLayout(Bounds.Width);
    }

    private void OnThemeResourcesChanged(object? sender, EventArgs eventArgs)
    {
        InvalidateMeasure();
        InvalidateVisualTree(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _subscribedViewModel = viewModel;
            ApplyRuntimeThemeVariant(viewModel.ActiveThemeVariant);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(MainViewModel.ActiveThemeVariant) &&
            sender is MainViewModel viewModel)
        {
            ApplyRuntimeThemeVariant(viewModel.ActiveThemeVariant);
        }

    }

    private void ApplyRuntimeThemeVariant(ThemeVariant themeVariant)
    {
        RootThemeScope.RequestedThemeVariant = themeVariant;
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not null)
        {
            topLevel.RequestedThemeVariant = themeVariant;
        }

        InvalidateMeasure();
        InvalidateVisualTree(this);
    }

    private static void InvalidateVisualTree(Visual visual)
    {
        visual.InvalidateVisual();

        foreach (Visual child in visual.GetVisualChildren())
        {
            InvalidateVisualTree(child);
        }
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

        UpdateModalWidths(width);

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
            Grid.SetColumnSpan(TaskPaneOverlay, 1);
            Grid.SetColumn(TaskListPanel, 0);
            Grid.SetRow(TaskListPanel, 0);
            Grid.SetColumnSpan(TaskListPanel, 1);
            Grid.SetColumn(EditorPanel, 0);
            Grid.SetRow(EditorPanel, 0);
            TaskListPanel.Width = _isTaskPaneExpanded
                ? double.NaN
                : Math.Max(200, Math.Min(PreferredTaskPaneWidth, width - 12));
            TaskListPanel.Margin = new Avalonia.Thickness(0);
            TaskListPanel.CornerRadius = _isTaskPaneExpanded ? new CornerRadius(0) : new CornerRadius(0, 8, 8, 0);
            TaskListPanel.HorizontalAlignment = _isTaskPaneExpanded
                ? Avalonia.Layout.HorizontalAlignment.Stretch
                : Avalonia.Layout.HorizontalAlignment.Left;
            TaskListPanel.IsVisible = _isTaskPaneOpen;
            TaskListPanel.RenderTransform = _isTaskPaneExpanded
                ? new TranslateTransform(0, 0)
                : new TranslateTransform(_isTaskPaneOpen ? 0 : -TaskListPanel.Width, 0);
            TaskPaneOverlay.IsVisible = _isTaskPaneOpen && !_isTaskPaneExpanded;
            EditorPanel.IsVisible = !_isTaskPaneExpanded;
            EditorPanel.Margin = new Avalonia.Thickness(shellMargin);
            TaskPaneToggleButton.IsVisible = true;
            TaskPaneCloseButton.IsVisible = true;
            TaskPaneActionStrip.IsVisible = true;
            ApplyCompactToolbar(width);
            ApplyCompactEditor(width - (shellMargin * 2) - 32);
        }
        else
        {
            double paneWidth = Math.Max(MinimumTaskPaneWidth, Math.Min(PreferredTaskPaneWidth, width * 0.32));
            double editorContentWidth = width - 32 - 16 - paneWidth - 32;
            ResponsiveShell.ColumnDefinitions = _isTaskPaneExpanded
                ? new ColumnDefinitions("*")
                : new ColumnDefinitions($"{paneWidth.ToString(CultureInfo.InvariantCulture)},*");
            ResponsiveShell.RowDefinitions = new RowDefinitions("*");
            ResponsiveShell.Margin = new Avalonia.Thickness(16);
            Grid.SetColumnSpan(TaskPaneOverlay, _isTaskPaneExpanded ? 1 : 2);
            Grid.SetColumn(TaskListPanel, 0);
            Grid.SetRow(TaskListPanel, 0);
            Grid.SetColumnSpan(TaskListPanel, 1);
            Grid.SetColumn(EditorPanel, _isTaskPaneExpanded ? 0 : 1);
            Grid.SetRow(EditorPanel, 0);
            TaskListPanel.Width = double.NaN;
            TaskListPanel.Margin = new Avalonia.Thickness(0);
            TaskListPanel.CornerRadius = new CornerRadius(8);
            TaskListPanel.RenderTransform = new TranslateTransform(0, 0);
            TaskListPanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            TaskListPanel.IsVisible = true;
            TaskPaneOverlay.IsVisible = false;
            EditorPanel.IsVisible = !_isTaskPaneExpanded;
            EditorPanel.Margin = new Avalonia.Thickness(0);
            TaskPaneToggleButton.IsVisible = false;
            TaskPaneCloseButton.IsVisible = false;
            TaskPaneActionStrip.IsVisible = false;
            ApplyRegularToolbar();
            TaskListDatePicker.MinWidth = 170;
            ApplyEditorDateLayout(editorContentWidth);
        }
    }

    private void OnExpandTaskPaneClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        _isTaskPaneExpanded = !_isTaskPaneExpanded;
        if (_isTaskPaneExpanded)
        {
            _isTaskPaneOpen = true;
        }

        UpdateResponsiveLayout(Bounds.Width);
    }

    private void ApplyRegularToolbar()
    {
        MoveTopBarActionsToTopBar();
        TopBarShell.ColumnDefinitions = new ColumnDefinitions("*,Auto");
        TopBarShell.RowDefinitions = new RowDefinitions("Auto");
        Grid.SetColumn(TopBarInfoGroup, 0);
        Grid.SetRow(TopBarInfoGroup, 0);
        Grid.SetColumn(TopBarActionGroup, 1);
        Grid.SetRow(TopBarActionGroup, 0);
        TopBarActionGroup.IsVisible = true;
        TopBarActionGroup.Margin = new Avalonia.Thickness(0);
        TopBar.Padding = new Avalonia.Thickness(10, 8);
        AppTitleText.FontSize = 22;
        CurrentScopeText.IsVisible = true;
    }

    private void ApplyCompactToolbar(double width)
    {
        MoveTopBarActionsToTaskPane();
        bool phoneWidth = width < CompactWidthThreshold;
        TopBarShell.ColumnDefinitions = new ColumnDefinitions("*");
        TopBarShell.RowDefinitions = new RowDefinitions("Auto");
        Grid.SetColumn(TopBarInfoGroup, 0);
        Grid.SetRow(TopBarInfoGroup, 0);
        TopBarActionGroup.Margin = new Avalonia.Thickness(0);

        TopBar.Padding = phoneWidth ? new Avalonia.Thickness(8, 6) : new Avalonia.Thickness(10, 8);
        AppTitleText.FontSize = phoneWidth ? 18 : 20;
        CurrentScopeText.IsVisible = !phoneWidth;
    }

    private void MoveTopBarActionsToTopBar()
    {
        if (TaskPaneActionHost.Content == TopBarActionGroup)
        {
            TaskPaneActionHost.Content = null;
        }

        if (!TopBarShell.Children.Contains(TopBarActionGroup))
        {
            TopBarShell.Children.Add(TopBarActionGroup);
        }

        Grid.SetColumn(TopBarActionGroup, 1);
        Grid.SetRow(TopBarActionGroup, 0);
        TopBarActionGroup.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        TopBarActionGroup.IsVisible = true;
    }

    private void MoveTopBarActionsToTaskPane()
    {
        if (TaskPaneActionHost.Content == TopBarActionGroup)
        {
            TopBarActionGroup.IsVisible = true;
            return;
        }

        TopBarShell.Children.Remove(TopBarActionGroup);
        TopBarActionGroup.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        TopBarActionGroup.IsVisible = true;
        TaskPaneActionHost.Content = TopBarActionGroup;
    }

    private void ApplyCompactEditor(double editorContentWidth)
    {
        TaskListDatePicker.MinWidth = 170;
        ApplyEditorDateLayout(editorContentWidth);
    }

    private void UpdateModalWidths(double width)
    {
        SettingsModalCard.Width = Math.Min(PreferredSettingsModalWidth, Math.Max(0, width - ModalViewportPadding));
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

    private async void OnTaskPaneMenuActionClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        await CloseTaskPaneAsync();
    }

    private async void OnOpenConnectionErrorHtmlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (DataContext is not MainViewModel viewModel ||
            string.IsNullOrWhiteSpace(viewModel.ConnectionErrorHtml))
        {
            return;
        }

        try
        {
            string dataUri = "data:text/html;charset=utf-8," + Uri.EscapeDataString(viewModel.ConnectionErrorHtml);
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Launcher is not null)
            {
                await topLevel.Launcher.LaunchUriAsync(new Uri(dataUri));
            }
        }
        catch (Exception error)
        {
            viewModel.ConnectionErrorMessage = $"HTML 오류 화면을 브라우저로 열지 못했습니다.\n{error.Message}\n\n{viewModel.ConnectionErrorHtml}";
            viewModel.IsConnectionErrorHtml = false;
        }
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
        if (_isTaskPaneExpanded)
        {
            _isTaskPaneExpanded = false;
        }

        if (!_isNarrowLayout || !_isTaskPaneOpen)
        {
            UpdateResponsiveLayout(Bounds.Width);
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
