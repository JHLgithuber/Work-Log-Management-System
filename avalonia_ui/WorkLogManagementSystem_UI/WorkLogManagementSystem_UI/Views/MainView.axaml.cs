using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WorkLogManagementSystem_UI.Controls;
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
    private const double PreferredRegisterUserModalWidth = 520;
    private const double PreferredDeleteConfirmationModalWidth = 520;
    private const double PreferredUnsavedChangesModalWidth = 520;
    private const double TaskListDatePickerMinWidth = 200;
    private const double ModalViewportPadding = 24;
    private const int TaskPaneAnimationDurationMilliseconds = 180;
    private const int TaskPaneModeAnimationDurationMilliseconds = 220;
    private const string ExpandTaskPaneIconData =
        "M4 5H10V7H7.41L11.71 11.29L10.29 12.71L6 8.41V11H4V5ZM20 19H14V17H16.59L12.29 12.71L13.71 11.29L18 15.59V13H20V19Z";
    private const string RestoreTaskPaneIconData =
        "M5 16H8V19H10V14H5V16ZM8 8H5V10H10V5H8V8ZM14 19H16V16H19V14H14V19ZM16 8V5H14V10H19V8H16Z";
    private bool _isNarrowLayout;
    private bool _isTaskPaneOpen = true;
    private bool _isTaskPaneExpanded;
    private bool _editorBringIntoViewQueued;
    private TextBox? _focusedTaskEditor;
    private IInputPane? _inputPane;
    private CancellationTokenSource? _imeAnimationCancellation;
    private CancellationTokenSource? _taskPaneModeAnimationCancellation;
    private Thickness _imeBasePadding;
    private bool _imeInsetsAttached;
    private MainViewModel? _subscribedViewModel;
    private Window? _hostWindow;
    private bool _allowHostWindowClose;

    public MainView()
    {
        InitializeComponent();
        EditorPanel.SetValue(Panel.ZIndexProperty, 0);
        TaskPaneOverlay.SetValue(Panel.ZIndexProperty, 1);
        TaskListPanel.SetValue(Panel.ZIndexProperty, 2);
        ExpandTaskPaneButton.Click += OnExpandTaskPaneClicked;
        AddHandler(InputElement.GotFocusEvent, OnTaskEditorGotFocus);
        AddHandler(InputElement.KeyDownEvent, OnSaveShortcutKeyDown, RoutingStrategies.Tunnel);
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
        AppSettingsService.ThemeResourcesChanged += OnThemeResourcesChanged;
        UpdateExpandTaskPaneButton();
        UpdateResponsiveLayout(Bounds.Width);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is Window window && !ReferenceEquals(_hostWindow, window))
        {
            if (_hostWindow is not null)
            {
                _hostWindow.Closing -= OnHostWindowClosing;
            }

            _hostWindow = window;
            _hostWindow.Closing += OnHostWindowClosing;
        }

        if (!OperatingSystem.IsAndroid() || topLevel is null)
        {
            return;
        }

        if (topLevel.InsetsManager is { } insetsManager)
        {
            // Use the platform edge-to-edge path on every supported Android version.
            // API 35+ enforces this, while older versions honor the preference here.
            insetsManager.DisplayEdgeToEdgePreference = true;
        }

        _imeBasePadding = RootThemeScope.Padding;
        _imeInsetsAttached = true;
        _inputPane = topLevel.InputPane;

        if (_inputPane is null)
        {
            return;
        }

        _inputPane.StateChanged += OnInputPaneStateChanged;
        SetImeOcclusionHeight(_inputPane.OccludedRect.Height);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        _taskPaneModeAnimationCancellation?.Cancel();

        if (_hostWindow is not null)
        {
            _hostWindow.Closing -= OnHostWindowClosing;
            _hostWindow = null;
        }

        if (_inputPane is not null)
        {
            _inputPane.StateChanged -= OnInputPaneStateChanged;
            _inputPane = null;
        }

        if (_imeInsetsAttached)
        {
            CancelImeAnimation();
            RootThemeScope.Padding = _imeBasePadding;
            _imeInsetsAttached = false;
        }

        base.OnDetachedFromVisualTree(eventArgs);
    }

    private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs eventArgs)
    {
        double currentHeight = Math.Max(0, RootThemeScope.Padding.Bottom - _imeBasePadding.Bottom);
        double targetHeight = Math.Max(0, eventArgs.EndRect.Height);

        CancelImeAnimation();

        // Keep the final value as the local value. The animation temporarily has
        // higher priority and moves from the current inset to the Android IME target.
        RootThemeScope.Padding = CreateImePadding(targetHeight);

        if (eventArgs.AnimationDuration <= TimeSpan.Zero || Math.Abs(currentHeight - targetHeight) < 0.5)
        {
            QueueFocusedTaskEditorIntoView();
            return;
        }

        var animation = new Animation
        {
            Duration = eventArgs.AnimationDuration,
            Easing = eventArgs.Easing as Easing ?? new LinearEasing(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter
                        {
                            Property = Decorator.PaddingProperty,
                            Value = CreateImePadding(currentHeight),
                        },
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter
                        {
                            Property = Decorator.PaddingProperty,
                            Value = CreateImePadding(targetHeight),
                        },
                    },
                },
            },
        };

        var cancellation = new CancellationTokenSource();
        _imeAnimationCancellation = cancellation;
        _ = RunImeAnimationAsync(animation, cancellation);
    }

    private async Task RunImeAnimationAsync(Animation animation, CancellationTokenSource cancellation)
    {
        try
        {
            await animation.RunAsync(RootThemeScope, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A reversing or replacement IME transition continues from the current padding.
        }
        finally
        {
            if (ReferenceEquals(_imeAnimationCancellation, cancellation))
            {
                _imeAnimationCancellation = null;
                QueueFocusedTaskEditorIntoView();
            }

            cancellation.Dispose();
        }
    }

    private void SetImeOcclusionHeight(double height)
    {
        RootThemeScope.Padding = CreateImePadding(Math.Max(0, height));
        QueueFocusedTaskEditorIntoView();
    }

    private Thickness CreateImePadding(double imeHeight)
    {
        return new Thickness(
            _imeBasePadding.Left,
            _imeBasePadding.Top,
            _imeBasePadding.Right,
            _imeBasePadding.Bottom + imeHeight);
    }

    private void CancelImeAnimation()
    {
        CancellationTokenSource? cancellation = _imeAnimationCancellation;
        _imeAnimationCancellation = null;
        cancellation?.Cancel();
    }

    private void OnTaskEditorGotFocus(object? sender, GotFocusEventArgs eventArgs)
    {
        if (eventArgs.Source is not TextBox textBox ||
            !textBox.Classes.Contains("editor") ||
            textBox.FindAncestorOfType<ScrollViewer>() != EditorScrollViewer)
        {
            return;
        }

        _focusedTaskEditor = textBox;
        QueueFocusedTaskEditorIntoView();
    }

    private async void OnSaveShortcutKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.S ||
            (eventArgs.KeyModifiers & KeyModifiers.Control) != KeyModifiers.Control)
        {
            return;
        }

        eventArgs.Handled = true;
        await CommitFocusedCompositionAsync();

        if (DataContext is MainViewModel viewModel && viewModel.SaveTaskCommand.CanExecute(null))
        {
            viewModel.SaveTaskCommand.Execute(null);
        }
    }

    private async Task CommitFocusedCompositionAsync()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        IFocusManager? focusManager = topLevel?.FocusManager;
        if (focusManager?.GetFocusedElement() is not ImeSafeTextBox textBox ||
            !textBox.CommitPendingComposition())
        {
            return;
        }

        focusManager.ClearFocus();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);

        if (textBox.IsAttachedToVisualTree())
        {
            textBox.Focus(NavigationMethod.Unspecified);
        }

        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private void OnEditorScrollChanged(object? sender, ScrollChangedEventArgs eventArgs)
    {
        if (Math.Abs(eventArgs.ViewportDelta.Y) > 0.5)
        {
            QueueFocusedTaskEditorIntoView();
        }
    }

    private void QueueFocusedTaskEditorIntoView()
    {
        if (_editorBringIntoViewQueued)
        {
            return;
        }

        _editorBringIntoViewQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _editorBringIntoViewQueued = false;

            if (_focusedTaskEditor is not { IsFocused: true } textBox ||
                textBox.FindAncestorOfType<ScrollViewer>() != EditorScrollViewer)
            {
                return;
            }

            (textBox.Parent as Control ?? textBox).BringIntoView();
        }, DispatcherPriority.Loaded);
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
            _subscribedViewModel.CloseWindowRequested -= OnCloseWindowRequested;
            _subscribedViewModel = null;
        }

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.CloseWindowRequested += OnCloseWindowRequested;
            _subscribedViewModel = viewModel;
            ApplyRuntimeThemeVariant(viewModel.ActiveThemeVariant);
        }
    }

    private void OnHostWindowClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is ImeSafeTextBox textBox)
        {
            textBox.CommitPendingComposition();
        }

        if (_allowHostWindowClose ||
            DataContext is not MainViewModel { HasUnsavedTaskChanges: true } viewModel)
        {
            return;
        }

        eventArgs.Cancel = true;
        viewModel.RequestCloseWithUnsavedChanges();
    }

    private void OnCloseWindowRequested(object? sender, EventArgs eventArgs)
    {
        if (_hostWindow is null)
        {
            return;
        }

        _allowHostWindowClose = true;
        _hostWindow.Close();
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
            TopBarSessionInfo.IsVisible = false;
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
            TopBarSessionInfo.IsVisible = true;
            ApplyRegularToolbar();
            ApplyRegularEditor(editorContentWidth);
        }
    }

    private async void OnExpandTaskPaneClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        if (_taskPaneModeAnimationCancellation is not null)
        {
            return;
        }

        bool expanding = !_isTaskPaneExpanded;
        var cancellation = new CancellationTokenSource();
        _taskPaneModeAnimationCancellation = cancellation;
        ExpandTaskPaneButton.IsEnabled = false;

        try
        {
            await AnimateTaskPaneModeChangeAsync(expanding, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_taskPaneModeAnimationCancellation, cancellation))
            {
                _taskPaneModeAnimationCancellation = null;
                TaskListPanel.Opacity = 1;
                EditorPanel.Opacity = 1;
                ExpandTaskPaneButton.IsEnabled = true;
            }

            cancellation.Dispose();
        }
    }

    private async Task AnimateTaskPaneModeChangeAsync(bool expanding, CancellationToken cancellationToken)
    {
        int fadeOutFrames = Math.Max(1, TaskPaneModeAnimationDurationMilliseconds / 3 / 16);
        for (int frame = 0; frame <= fadeOutFrames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double progress = frame / (double)fadeOutFrames;
            double easedProgress = progress * progress;
            TaskListPanel.Opacity = 1 - (0.28 * easedProgress);
            if (EditorPanel.IsVisible)
            {
                EditorPanel.Opacity = 1 - easedProgress;
            }

            await Task.Delay(16, cancellationToken);
        }

        _isTaskPaneExpanded = expanding;
        if (expanding)
        {
            _isTaskPaneOpen = true;
        }

        UpdateResponsiveLayout(Bounds.Width);
        UpdateExpandTaskPaneButton();

        TaskListPanel.Opacity = 0.72;
        if (!expanding)
        {
            EditorPanel.Opacity = 0;
        }

        int fadeInFrames = Math.Max(1, (TaskPaneModeAnimationDurationMilliseconds * 2 / 3) / 16);
        for (int frame = 0; frame <= fadeInFrames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double progress = frame / (double)fadeInFrames;
            double easedProgress = 1 - Math.Pow(1 - progress, 3);
            TaskListPanel.Opacity = 0.72 + (0.28 * easedProgress);
            if (!expanding)
            {
                EditorPanel.Opacity = easedProgress;
            }

            await Task.Delay(16, cancellationToken);
        }
    }

    private void UpdateExpandTaskPaneButton()
    {
        ExpandTaskPaneIcon.Data = Geometry.Parse(
            _isTaskPaneExpanded ? RestoreTaskPaneIconData : ExpandTaskPaneIconData);
        ToolTip.SetTip(
            ExpandTaskPaneButton,
            _isTaskPaneExpanded ? "과업 패널 원래 크기로 복원" : "과업 패널 최대화");
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
        EditorHeaderGrid.ColumnDefinitions = new ColumnDefinitions("*");
        EditorHeaderGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
        EditorHeaderGrid.ColumnSpacing = 0;
        EditorHeaderGrid.RowSpacing = 10;
        Grid.SetColumn(EditorHeaderTitle, 0);
        Grid.SetRow(EditorHeaderTitle, 0);
        Grid.SetColumn(EditorHeaderActions, 0);
        Grid.SetRow(EditorHeaderActions, 1);
        EditorHeaderActions.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        EditorHeaderActions.Margin = new Avalonia.Thickness(0);

        TaskListDatePicker.MinWidth = TaskListDatePickerMinWidth;
        ApplyEditorDateLayout(editorContentWidth);
    }

    private void ApplyRegularEditor(double editorContentWidth)
    {
        EditorHeaderGrid.ColumnDefinitions = new ColumnDefinitions("*,Auto");
        EditorHeaderGrid.RowDefinitions = new RowDefinitions("Auto");
        EditorHeaderGrid.ColumnSpacing = 12;
        EditorHeaderGrid.RowSpacing = 0;
        Grid.SetColumn(EditorHeaderTitle, 0);
        Grid.SetRow(EditorHeaderTitle, 0);
        Grid.SetColumn(EditorHeaderActions, 1);
        Grid.SetRow(EditorHeaderActions, 0);
        EditorHeaderActions.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        EditorHeaderActions.Margin = new Avalonia.Thickness(0);

        TaskListDatePicker.MinWidth = TaskListDatePickerMinWidth;
        ApplyEditorDateLayout(editorContentWidth);
    }

    private void UpdateModalWidths(double width)
    {
        SettingsModalCard.Width = Math.Min(PreferredSettingsModalWidth, Math.Max(0, width - ModalViewportPadding));
        RegisterUserModalCard.Width = Math.Min(PreferredRegisterUserModalWidth, Math.Max(0, width - ModalViewportPadding));
        DeleteTaskConfirmationModalCard.Width = Math.Min(
            PreferredDeleteConfirmationModalWidth,
            Math.Max(0, width - ModalViewportPadding));
        UnsavedChangesModalCard.Width = Math.Min(
            PreferredUnsavedChangesModalWidth,
            Math.Max(0, width - ModalViewportPadding));
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
        _taskPaneModeAnimationCancellation?.Cancel();

        if (_isTaskPaneExpanded)
        {
            _isTaskPaneExpanded = false;
            UpdateExpandTaskPaneButton();
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
