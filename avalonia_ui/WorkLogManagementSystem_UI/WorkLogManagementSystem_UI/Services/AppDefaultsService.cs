using System;
using System.Collections.Generic;
using System.IO;

namespace WorkLogManagementSystem_UI.Services;

public static class AppDefaultsService
{
    private const string ApiBaseUrlKey = "WORKLOG_API_BASE_URL";
    private const string UsernameKey = "WORKLOG_DEFAULT_USERNAME";
    private const string ThemeModeKey = "WORKLOG_THEME_MODE";

    public static AppDefaults Load()
    {
        Dictionary<string, string> values = LoadDotEnvValues();
        string apiBaseUrl = ResolveValue(values, ApiBaseUrlKey, "http://localhost:8000").Trim().TrimEnd('/');
        string username = ResolveValue(values, UsernameKey, string.Empty).Trim();
        string themeMode = ResolveValue(values, ThemeModeKey, AppThemeMode.System).Trim();

        if (themeMode is not (AppThemeMode.System or AppThemeMode.Light or AppThemeMode.Dark))
        {
            themeMode = AppThemeMode.System;
        }

        return new AppDefaults(apiBaseUrl, username, themeMode);
    }

    private static string ResolveValue(IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        string? environmentValue = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        return values.TryGetValue(key, out string? fileValue) && !string.IsNullOrWhiteSpace(fileValue)
            ? fileValue
            : fallback;
    }

    private static Dictionary<string, string> LoadDotEnvValues()
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        string? path = FindDotEnvFile();
        if (path is null)
        {
            return values;
        }

        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim().Trim('"').Trim('\'');
            values[key] = value;
        }

        return values;
    }

    private static string? FindDotEnvFile()
    {
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(directory); i++)
        {
            string candidate = Path.Combine(directory, ".env");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        string currentDirectoryCandidate = Path.Combine(Environment.CurrentDirectory, ".env");
        return File.Exists(currentDirectoryCandidate) ? currentDirectoryCandidate : null;
    }
}

