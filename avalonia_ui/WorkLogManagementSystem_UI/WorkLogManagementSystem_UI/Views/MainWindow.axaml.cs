using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace WorkLogManagementSystem_UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PropertyChanged += OnWindowPropertyChanged;
        UpdateWindowFrame();
    }

    private void OnResizeHandlePointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (WindowState != WindowState.Normal ||
            !eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            sender is not Control { Tag: { } tag } ||
            !Enum.TryParse(tag.ToString(), out WindowEdge edge))
        {
            return;
        }

        BeginResizeDrag(edge, eventArgs);
        eventArgs.Handled = true;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.Property == WindowStateProperty)
        {
            UpdateWindowFrame();
        }
    }

    private void UpdateWindowFrame()
    {
        bool isWindowed = WindowState == WindowState.Normal;
        ResizeHandles.IsVisible = isWindowed;
        WindowFrame.CornerRadius = isWindowed ? GetPlatformCornerRadius() : new CornerRadius(0);
    }

    private static CornerRadius GetPlatformCornerRadius()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new CornerRadius(12);
        }

        if (OperatingSystem.IsWindows())
        {
            return new CornerRadius(8);
        }

        if (OperatingSystem.IsLinux())
        {
            return new CornerRadius(7);
        }

        return new CornerRadius(8);
    }
}
