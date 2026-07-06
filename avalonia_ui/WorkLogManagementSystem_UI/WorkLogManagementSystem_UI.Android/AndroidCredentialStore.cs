using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using WorkLogManagementSystem_UI.Services;

namespace WorkLogManagementSystem_UI.Android;

public sealed class AndroidCredentialStore : ICredentialStore
{
    private const string KeyAlias = "WorkLogManagementSystem.UI.Credentials";
    private const string Transformation = "AES/GCM/NoPadding";
    private const int GcmTagBits = 128;
    private readonly Context _context;
    private readonly JsonSerializerOptions _jsonOptions = new();

    public AndroidCredentialStore(Context context)
    {
        _context = context.ApplicationContext ?? context;
    }

    public bool IsSaveSupported => OperatingSystem.IsAndroidVersionAtLeast(23);
    public string UnsupportedReason => IsSaveSupported
        ? string.Empty
        : "Android 계정정보 저장에는 Android 6.0(API 23) 이상의 Android Keystore가 필요합니다.";

    public async Task<StoredCredentials?> LoadAsync(CancellationToken cancellationToken)
    {
        string path = ResolveCredentialPath();
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] payload = await File.ReadAllBytesAsync(path, cancellationToken);
        if (payload.Length < 13)
        {
            return null;
        }

        int ivLength = payload[0];
        if (ivLength <= 0 || payload.Length <= 1 + ivLength)
        {
            return null;
        }

        byte[] iv = payload[1..(1 + ivLength)];
        byte[] encrypted = payload[(1 + ivLength)..];
        if (!OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            return null;
        }

        Cipher cipher = Cipher.GetInstance(Transformation)!;
        cipher.Init(Javax.Crypto.CipherMode.DecryptMode, GetOrCreateSecretKey(), new GCMParameterSpec(GcmTagBits, iv));
        byte[] decrypted = cipher.DoFinal(encrypted) ?? [];
        string json = Encoding.UTF8.GetString(decrypted);
        return JsonSerializer.Deserialize<StoredCredentials>(json, _jsonOptions);
    }

    public async Task SaveAsync(StoredCredentials credentials, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            return;
        }

        string json = JsonSerializer.Serialize(credentials, _jsonOptions);
        byte[] plainText = Encoding.UTF8.GetBytes(json);
        Cipher cipher = Cipher.GetInstance(Transformation)!;
        cipher.Init(Javax.Crypto.CipherMode.EncryptMode, GetOrCreateSecretKey());

        byte[] encrypted = cipher.DoFinal(plainText) ?? [];
        byte[] iv = cipher.GetIV() ?? [];
        if (iv.Length > byte.MaxValue)
        {
            throw new CryptographicException("Android Keystore IV length is invalid.");
        }

        byte[] payload = new byte[1 + iv.Length + encrypted.Length];
        payload[0] = (byte)iv.Length;
        Buffer.BlockCopy(iv, 0, payload, 1, iv.Length);
        Buffer.BlockCopy(encrypted, 0, payload, 1 + iv.Length, encrypted.Length);
        Directory.CreateDirectory(Path.GetDirectoryName(ResolveCredentialPath())!);
        await File.WriteAllBytesAsync(ResolveCredentialPath(), payload, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        string path = ResolveCredentialPath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    [SupportedOSPlatform("android23.0")]
    private IKey GetOrCreateSecretKey()
    {
        KeyStore keyStore = KeyStore.GetInstance("AndroidKeyStore")!;
        keyStore.Load(null);
        if (!keyStore.ContainsAlias(KeyAlias))
        {
            KeyGenerator keyGenerator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
            KeyGenParameterSpec keySpec = new KeyGenParameterSpec.Builder(
                    KeyAlias,
                    KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes(KeyProperties.BlockModeGcm)
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                .SetRandomizedEncryptionRequired(true)
                .Build()!;
            keyGenerator.Init(keySpec);
            keyGenerator.GenerateKey();
        }

        return keyStore.GetKey(KeyAlias, null)!;
    }

    private string ResolveCredentialPath()
    {
        return Path.Combine(_context.FilesDir!.AbsolutePath, "secure_credentials.bin");
    }
}
