using System;

namespace WorkLogManagementSystem_UI.Services;

public static class PlatformCredentialStore
{
    private static Func<ICredentialStore> _factory = () =>
        new DisabledCredentialStore("이 플랫폼에서는 계정정보 저장을 지원하지 않습니다.");

    public static void Configure(Func<ICredentialStore> factory)
    {
        _factory = factory;
    }

    public static ICredentialStore Create()
    {
        return _factory();
    }
}

