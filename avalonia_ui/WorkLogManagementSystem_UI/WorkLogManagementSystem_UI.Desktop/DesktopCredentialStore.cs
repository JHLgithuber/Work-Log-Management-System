using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WorkLogManagementSystem_UI.Services;

namespace WorkLogManagementSystem_UI.Desktop;

public sealed class DesktopCredentialStore : ICredentialStore
{
    private const string ServiceName = "WorkLogManagementSystem_UI";
    private const string AccountName = "login";
    private static readonly JsonSerializerOptions JsonOptions = new();

    public bool IsSaveSupported => OperatingSystem.IsLinux() && CommandExists("secret-tool");

    public string UnsupportedReason => OperatingSystem.IsLinux()
        ? "계정정보 저장에는 Secret Service가 필요합니다. libsecret-tools(secret-tool)를 설치하세요."
        : "이 데스크탑 OS의 계정정보 저장소는 아직 구현되지 않았습니다.";

    public async Task<StoredCredentials?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!IsSaveSupported)
        {
            return null;
        }

        string json = await RunSecretToolAsync(
            null,
            cancellationToken,
            "lookup",
            "service",
            ServiceName,
            "account",
            AccountName);

        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<StoredCredentials>(json, JsonOptions);
    }

    public async Task SaveAsync(StoredCredentials credentials, CancellationToken cancellationToken)
    {
        if (!IsSaveSupported)
        {
            return;
        }

        string json = JsonSerializer.Serialize(credentials, JsonOptions);
        await RunSecretToolAsync(
            json,
            cancellationToken,
            "store",
            "--label",
            "WorkLogManagementSystem_UI login",
            "service",
            ServiceName,
            "account",
            AccountName);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        if (!IsSaveSupported)
        {
            return;
        }

        await RunSecretToolAsync(
            null,
            cancellationToken,
            "clear",
            "service",
            ServiceName,
            "account",
            AccountName);
    }

    private static bool CommandExists(string command)
    {
        try
        {
            using Process process = Process.Start(new ProcessStartInfo
            {
                FileName = "sh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "-c", $"command -v {command}" }
            })!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> RunSecretToolAsync(
        string? standardInput,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "secret-tool",
                RedirectStandardInput = standardInput is not null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }

        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 && arguments.Length > 0 && arguments[0] != "lookup")
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "Secret Service 저장소 호출에 실패했습니다."
                : error.Trim());
        }

        return output.Trim();
    }
}

