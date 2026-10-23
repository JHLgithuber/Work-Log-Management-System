using System;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WorkLogManagementSystem_UI.Services;

namespace WorkLogManagementSystem_UI.Desktop;

public sealed class DesktopCredentialStore : ICredentialStore
{
    private const string ServiceName = "WorkLogManagementSystem_UI";
    private const string AccountName = "login";
    private const string WindowsTargetName = ServiceName + ":" + AccountName;
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private static readonly JsonSerializerOptions JsonOptions = new();

    public bool IsSaveSupported => OperatingSystem.IsWindows() ||
        OperatingSystem.IsLinux() && CommandExists("secret-tool");

    public string UnsupportedReason => OperatingSystem.IsLinux()
        ? "계정정보 저장에는 Secret Service가 필요합니다. libsecret-tools(secret-tool)를 설치하세요."
        : OperatingSystem.IsMacOS()
            ? "macOS 키체인 연동은 아직 지원하지 않습니다."
            : string.Empty;

    public async Task<StoredCredentials?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!IsSaveSupported)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return LoadFromWindowsCredentialManager();
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
        if (OperatingSystem.IsWindows())
        {
            SaveToWindowsCredentialManager(json);
            return;
        }

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

        if (OperatingSystem.IsWindows())
        {
            ClearWindowsCredentialManager();
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

    private static StoredCredentials? LoadFromWindowsCredentialManager()
    {
        if (!CredRead(WindowsTargetName, CredentialTypeGeneric, 0, out nint credentialPointer))
        {
            int errorCode = Marshal.GetLastWin32Error();
            if (errorCode == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(errorCode, "Windows 자격 증명 관리자에서 계정정보를 읽지 못했습니다.");
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == nint.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            byte[] jsonBytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, jsonBytes, 0, jsonBytes.Length);
            return JsonSerializer.Deserialize<StoredCredentials>(Encoding.UTF8.GetString(jsonBytes), JsonOptions);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    private static void SaveToWindowsCredentialManager(string json)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        nint blobPointer = Marshal.AllocCoTaskMem(jsonBytes.Length);
        try
        {
            Marshal.Copy(jsonBytes, 0, blobPointer, jsonBytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = WindowsTargetName,
                CredentialBlobSize = (uint)jsonBytes.Length,
                CredentialBlob = blobPointer,
                Persist = CredentialPersistLocalMachine,
                UserName = AccountName,
            };

            if (!CredWrite(ref credential, 0))
            {
                int errorCode = Marshal.GetLastWin32Error();
                throw new Win32Exception(errorCode, "Windows 자격 증명 관리자에 계정정보를 저장하지 못했습니다.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(jsonBytes);
            Marshal.Copy(jsonBytes, 0, blobPointer, jsonBytes.Length);
            Marshal.FreeCoTaskMem(blobPointer);
        }
    }

    private static void ClearWindowsCredentialManager()
    {
        if (CredDelete(WindowsTargetName, CredentialTypeGeneric, 0))
        {
            return;
        }

        int errorCode = Marshal.GetLastWin32Error();
        if (errorCode != ErrorNotFound)
        {
            throw new Win32Exception(errorCode, "Windows 자격 증명 관리자에서 계정정보를 삭제하지 못했습니다.");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, int reservedFlag, out nint credentialPointer);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(nint buffer);

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
