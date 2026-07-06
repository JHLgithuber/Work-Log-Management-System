using System.Threading;
using System.Threading.Tasks;

namespace WorkLogManagementSystem_UI.Services;

public interface ICredentialStore
{
    bool IsSaveSupported { get; }
    string UnsupportedReason { get; }
    Task<StoredCredentials?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(StoredCredentials credentials, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}

