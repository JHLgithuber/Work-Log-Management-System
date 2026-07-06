using System.Threading;
using System.Threading.Tasks;

namespace WorkLogManagementSystem_UI.Services;

public sealed class DisabledCredentialStore : ICredentialStore
{
    public DisabledCredentialStore(string unsupportedReason)
    {
        UnsupportedReason = unsupportedReason;
    }

    public bool IsSaveSupported => false;
    public string UnsupportedReason { get; }

    public Task<StoredCredentials?> LoadAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<StoredCredentials?>(null);
    }

    public Task SaveAsync(StoredCredentials credentials, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

