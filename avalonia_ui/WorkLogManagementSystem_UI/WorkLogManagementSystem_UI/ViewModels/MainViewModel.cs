using CommunityToolkit.Mvvm.ComponentModel;

namespace WorkLogManagementSystem_UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty] private string _greeting = "Welcome to Avalonia!";
}