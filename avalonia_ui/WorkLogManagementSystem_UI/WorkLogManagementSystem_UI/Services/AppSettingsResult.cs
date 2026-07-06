namespace WorkLogManagementSystem_UI.Services;

public static class AppThemeMode
{
    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";
}

public sealed record AppSettingsResult(
    string ApiBaseUrl,
    string ThemeMode,
    string AccessToken = "",
    string Username = "");
