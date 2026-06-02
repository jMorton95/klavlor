using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlavLor.Infrastructure.Security;

/// <summary>
/// Derives the symmetric key used to encrypt the Data Protection key ring at rest.
/// The key material comes from the <c>AuthKey</c> configuration value (top-level), injected
/// as an environment variable in production and therefore lives outside the database — so a
/// leaked DB dump or backup cannot decrypt the key ring (and thus cannot forge auth cookies).
/// </summary>
internal static class AuthKeyDerivation
{
    public const string ConfigKey = "AuthKey";

    // Static, app-specific salt/info so the same AUTH_KEY always derives the same wrapping key.
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("KlavLor.DataProtection.v1");
    private static readonly byte[] Info = Encoding.UTF8.GetBytes("dp-keyring-encryption");

    public static byte[] DeriveKey(IConfiguration configuration)
    {
        var authKey = configuration[ConfigKey];

        if (string.IsNullOrWhiteSpace(authKey))
            throw new InvalidOperationException(
                $"'{ConfigKey}' is not configured. It is required to encrypt the Data Protection key ring at rest.");

        // HKDF-SHA256 → 32-byte AES-256 key.
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(authKey), outputLength: 32, Salt, Info);
    }
}

/// <summary>
/// Encrypts Data Protection key XML with AES-256-GCM using the AUTH_KEY-derived key.
/// Set as <c>KeyManagementOptions.XmlEncryptor</c> so every newly generated key is encrypted
/// before being persisted to the database.
/// </summary>
internal sealed class AuthKeyXmlEncryptor(byte[] key) : IXmlEncryptor
{
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));

        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize); // 12 bytes
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];                            // 16 bytes
        var ciphertext = new byte[plaintext.Length];

        using (var aes = new AesGcm(key, tag.Length))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Layout: nonce || tag || ciphertext, base64-encoded.
        var blob = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, blob, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, blob, nonce.Length + tag.Length, ciphertext.Length);

        var encryptedElement = new XElement("encryptedKey",
            new XComment(" Encrypted with AES-256-GCM using a key derived from AuthKey. "),
            new XElement("value", Convert.ToBase64String(blob)));

        return new EncryptedXmlInfo(encryptedElement, typeof(AuthKeyXmlDecryptor));
    }
}

/// <summary>
/// Decrypts key XML produced by <see cref="AuthKeyXmlEncryptor"/>. Activated by the Data Protection
/// runtime, which only supports a parameterless or single-<see cref="IServiceProvider"/> constructor
/// (it does not do full DI ctor injection), so we take the provider and resolve config from it.
/// The fully-qualified type name is recorded in each encrypted key element — do not rename or move
/// this type without a migration plan for existing keys.
/// </summary>
internal sealed class AuthKeyXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    private readonly byte[] _key = AuthKeyDerivation.DeriveKey(services.GetRequiredService<IConfiguration>());

    public XElement Decrypt(XElement encryptedElement)
    {
        var blob = Convert.FromBase64String((string)encryptedElement.Element("value")!);

        var nonceLength = AesGcm.NonceByteSizes.MaxSize;
        var tagLength = AesGcm.TagByteSizes.MaxSize;

        var nonce = blob.AsSpan(0, nonceLength);
        var tag = blob.AsSpan(nonceLength, tagLength);
        var ciphertext = blob.AsSpan(nonceLength + tagLength);
        var plaintext = new byte[ciphertext.Length];

        using (var aes = new AesGcm(_key, tagLength))
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return XElement.Parse(Encoding.UTF8.GetString(plaintext));
    }
}
