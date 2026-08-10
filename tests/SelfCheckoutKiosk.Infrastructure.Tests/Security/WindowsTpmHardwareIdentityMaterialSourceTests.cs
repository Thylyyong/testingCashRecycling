using SelfCheckoutKiosk.Infrastructure.Security;
using Xunit;

namespace SelfCheckoutKiosk.Infrastructure.Tests.Security;

public sealed class
    WindowsTpmHardwareIdentityMaterialSourceTests
{
    // =========================================================
    // VALID PUBLIC MATERIAL
    // =========================================================

    [Fact]
    public void
        GetIdentityMaterial_ValidPublicBlob_ReturnsBase64()
    {
        var publicKeyBlob =
            new byte[]
            {
                0x01,
                0x02,
                0x03,
                0x04,
                0x05,
                0xA0,
                0xB0,
                0xC0
            };

        var keyStore =
            new FakeTpmIdentityKeyStore(
                publicKeyBlob
            );

        var source =
            new WindowsTpmHardwareIdentityMaterialSource(
                keyStore
            );

        var result =
            source
                .GetIdentityMaterial();

        Assert.Equal(
            Convert.ToBase64String(
                publicKeyBlob
            ),
            result
        );
    }

    // =========================================================
    // DETERMINISTIC
    // =========================================================

    [Fact]
    public void
        GetIdentityMaterial_SamePublicBlob_ReturnsSameMaterial()
    {
        var publicKeyBlob =
            new byte[]
            {
                0x11,
                0x22,
                0x33,
                0x44
            };

        var keyStore =
            new FakeTpmIdentityKeyStore(
                publicKeyBlob
            );

        var source =
            new WindowsTpmHardwareIdentityMaterialSource(
                keyStore
            );

        var first =
            source
                .GetIdentityMaterial();

        var second =
            source
                .GetIdentityMaterial();

        Assert.Equal(
            first,
            second
        );
    }

    // =========================================================
    // EMPTY MATERIAL
    // =========================================================

    [Fact]
    public void
        GetIdentityMaterial_EmptyPublicBlob_Throws()
    {
        var source =
            new WindowsTpmHardwareIdentityMaterialSource(
                new FakeTpmIdentityKeyStore(
                    []
                )
            );

        var exception =
            Assert.Throws<
                HardwareIdentityException
            >(
                source.GetIdentityMaterial
            );

        Assert.Contains(
            "public-key",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    // =========================================================
    // KEY STORE FAILURE
    // =========================================================

    [Fact]
    public void
        GetIdentityMaterial_KeyStoreThrows_WrapsFailure()
    {
        var source =
            new WindowsTpmHardwareIdentityMaterialSource(
                new ThrowingTpmIdentityKeyStore(
                    new InvalidOperationException(
                        "TPM provider unavailable."
                    )
                )
            );

        var exception =
            Assert.Throws<
                HardwareIdentityException
            >(
                source.GetIdentityMaterial
            );

        Assert.NotNull(
            exception.InnerException
        );

        Assert.IsType<
            InvalidOperationException
        >(
            exception.InnerException
        );
    }

    // =========================================================
    // EXISTING HARDWARE ID PIPELINE
    // =========================================================

    [Fact]
    public void
        TpmSource_WithHardwareIdProvider_ProducesStableHardwareId()
    {
        var publicKeyBlob =
            new byte[]
            {
                0x90,
                0x91,
                0x92,
                0x93,
                0x94,
                0x95
            };

        var source =
            new WindowsTpmHardwareIdentityMaterialSource(
                new FakeTpmIdentityKeyStore(
                    publicKeyBlob
                )
            );

        var provider =
            new HardwareIdProvider(
                source
            );

        var first =
            provider
                .GetHardwareId();

        var second =
            provider
                .GetHardwareId();

        Assert.Equal(
            first,
            second
        );

        Assert.StartsWith(
            "KIOSK-",
            first,
            StringComparison.Ordinal
        );
    }

    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    [Fact]
    public void
        Constructor_NullKeyStore_Throws()
    {
        Assert.Throws<
            ArgumentNullException
        >(
            () =>
                new WindowsTpmHardwareIdentityMaterialSource(
                    null!
                )
        );
    }

    // =========================================================
    // FAKES
    // =========================================================

    private sealed class
        FakeTpmIdentityKeyStore
        : ITpmIdentityKeyStore
    {
        private readonly byte[]
            _publicKeyBlob;

        public FakeTpmIdentityKeyStore(
            byte[] publicKeyBlob)
        {
            _publicKeyBlob =
                publicKeyBlob;
        }

        public byte[]
            GetOrCreatePublicKeyBlob()
        {
            return
                _publicKeyBlob
                    .ToArray();
        }
    }

    private sealed class
        ThrowingTpmIdentityKeyStore
        : ITpmIdentityKeyStore
    {
        private readonly Exception
            _exception;

        public ThrowingTpmIdentityKeyStore(
            Exception exception)
        {
            _exception =
                exception;
        }

        public byte[]
            GetOrCreatePublicKeyBlob()
        {
            throw _exception;
        }
    }
}