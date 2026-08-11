using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace SelfCheckoutKiosk.Infrastructure.Security;

/// <summary>
/// Manages the kiosk's persistent TPM-backed machine identity
/// key through Windows Cryptography Next Generation (CNG).
///
/// The key is created in the Microsoft Platform Crypto Provider.
///
/// The private key is configured as non-exportable.
/// Only the public-key blob is returned to the caller.
/// </summary>
public sealed class WindowsTpmIdentityKeyStore
    : ITpmIdentityKeyStore
{
    private const string IdentityKeyName =
        "SelfCheckoutKiosk.NodeIdentity.v1";

    private static readonly object CreationGate =
        new();

    /// <summary>
    /// Opens the existing TPM-backed machine identity key or
    /// creates it when it does not yet exist.
    ///
    /// Only the public-key blob is returned.
    /// </summary>
    public byte[] GetOrCreatePublicKeyBlob()
    {
        /*
         * Explicit platform guard.
         *
         * Infrastructure still targets net10.0 rather than a
         * Windows-specific TFM, so this implementation must
         * fail closed on unsupported platforms.
         */
        if (!OperatingSystem.IsWindows())
        {
            throw new HardwareIdentityException(
                "TPM-backed kiosk identity requires Windows."
            );
        }

        try
        {
            lock (CreationGate)
            {
                /*
                 * OpenOrCreateIdentityKey is explicitly marked
                 * as Windows-only.
                 *
                 * The OperatingSystem.IsWindows() guard above
                 * establishes the required platform context.
                 */
                using var key =
                    OpenOrCreateIdentityKey();

                ValidateIdentityKey(
                    key
                );

                var publicKeyBlob =
                    key.Export(
                        CngKeyBlobFormat.GenericPublicBlob
                    );

                if (
                    publicKeyBlob is null ||
                    publicKeyBlob.Length == 0
                )
                {
                    throw new HardwareIdentityException(
                        "The TPM identity key did not expose a " +
                        "usable public-key blob."
                    );
                }

                return publicKeyBlob;
            }
        }
        catch (HardwareIdentityException)
        {
            throw;
        }
        catch (
            PlatformNotSupportedException exception
        )
        {
            throw new HardwareIdentityException(
                "Windows CNG is not available for TPM-backed " +
                "kiosk identity.",
                exception
            );
        }
        catch (
            CryptographicException exception
        )
        {
            throw new HardwareIdentityException(
                "The TPM-backed kiosk identity key could not " +
                "be opened or created.",
                exception
            );
        }
    }

    // =========================================================
    // OPEN / CREATE
    // =========================================================

    /// <summary>
    /// Opens the existing persistent machine TPM key or creates
    /// it during first provisioning.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static CngKey OpenOrCreateIdentityKey()
    {
        var provider =
            CngProvider
                .MicrosoftPlatformCryptoProvider;

        /*
         * MachineKey:
         *     open from machine-wide key storage.
         *
         * Silent:
         *     do not display interactive crypto UI.
         */
        var openOptions =
            CngKeyOpenOptions.MachineKey |
            CngKeyOpenOptions.Silent;

        /*
         * =====================================================
         * EXISTING KEY
         * =====================================================
         */

        if (
            CngKey.Exists(
                IdentityKeyName,
                provider,
                openOptions
            )
        )
        {
            return CngKey.Open(
                IdentityKeyName,
                provider,
                openOptions
            );
        }

        /*
         * =====================================================
         * FIRST-RUN KEY CREATION
         * =====================================================
         */

        var creationParameters =
            new CngKeyCreationParameters
            {
                Provider =
                    provider,

                /*
                 * Persist this key for the machine rather than
                 * only for the current Windows user.
                 */
                KeyCreationOptions =
                    CngKeyCreationOptions.MachineKey,

                /*
                 * The private identity key must not be
                 * exportable.
                 */
                ExportPolicy =
                    CngExportPolicies.None,

                /*
                 * Signing capability makes this key suitable
                 * for later proof-of-possession extensions.
                 */
                KeyUsage =
                    CngKeyUsages.Signing
            };

        try
        {
            return CngKey.Create(
                CngAlgorithm.Rsa,
                IdentityKeyName,
                creationParameters
            );
        }
        catch (CryptographicException)
        {
            /*
             * Race-safe recovery:
             *
             * Another process may have created the named key
             * between Exists() and Create().
             */
            if (
                CngKey.Exists(
                    IdentityKeyName,
                    provider,
                    openOptions
                )
            )
            {
                return CngKey.Open(
                    IdentityKeyName,
                    provider,
                    openOptions
                );
            }

            throw;
        }
    }

    // =========================================================
    // KEY VALIDATION
    // =========================================================

    /// <summary>
    /// Confirms that the opened key has the expected persistence,
    /// machine scope, provider, and signing capability.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void ValidateIdentityKey(
        CngKey key)
    {
        ArgumentNullException.ThrowIfNull(
            key
        );

        /*
         * The node identity must survive application restarts.
         */
        if (
            key.IsEphemeral
        )
        {
            throw new HardwareIdentityException(
                "The TPM identity key is ephemeral. A persisted " +
                "machine identity key is required."
            );
        }

        /*
         * The kiosk identity belongs to the machine, not one
         * Windows account.
         */
        if (
            !key.IsMachineKey
        )
        {
            throw new HardwareIdentityException(
                "The TPM identity key is not stored as a " +
                "machine-wide key."
            );
        }

        /*
         * CngKey.Provider is nullable in the .NET API.
         *
         * Check it before reading its provider name.
         */
        var actualProvider =
            key.Provider;

        if (
            actualProvider is null
        )
        {
            throw new HardwareIdentityException(
                "The TPM identity key does not report a key " +
                "storage provider."
            );
        }

        var expectedProvider =
            CngProvider
                .MicrosoftPlatformCryptoProvider;

        if (
            !actualProvider.Equals(
                expectedProvider
            )
        )
        {
            throw new HardwareIdentityException(
                "The kiosk identity key is not managed by the " +
                "Microsoft Platform Crypto Provider."
            );
        }

        /*
         * CngKey.KeyUsage describes the actual operations allowed
         * for the persisted key.
         */
        var keyUsage =
            key.KeyUsage;

        if (
            (keyUsage &
             CngKeyUsages.Signing) ==
            0
        )
        {
            throw new HardwareIdentityException(
                "The TPM identity key does not have the expected " +
                "signing capability."
            );
        }
    }
}