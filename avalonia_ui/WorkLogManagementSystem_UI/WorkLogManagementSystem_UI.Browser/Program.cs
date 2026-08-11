using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using WorkLogManagementSystem_UI;
using WorkLogManagementSystem_UI.Services;

[assembly: SupportedOSPlatform("browser")]

internal sealed partial class Program
{
    private static Task Main(string[] args)
    {
        ConfigureApiBaseUrl(args);
        PlatformCredentialStore.Configure(() => new DisabledCredentialStore("웹에서는 계정정보 저장 기능을 사용할 수 없습니다."));
        return BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();

    private static void ConfigureApiBaseUrl(string[] args)
    {
        if (args.Length == 0 ||
            !Uri.TryCreate(args[0], UriKind.Absolute, out Uri? origin) ||
            (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        Environment.SetEnvironmentVariable(
            "WORKLOG_API_BASE_URL",
            origin.GetLeftPart(UriPartial.Authority));
    }
}
