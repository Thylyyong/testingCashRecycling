using SelfCheckoutKiosk.Core.Licensing;
using SelfCheckoutKiosk.Infrastructure.Security;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace SelfCheckoutKiosk.Infrastructure.Tests.Security;

public sealed class HardwareIdProviderTests
{
    // =========================================================
    // DETERMINISTIC
    // =========================================================

    [Fact]
    public void
        GetHardwareId_SameMaterial_ReturnsSameId()
    {
        var source =
            new FakeHardwareIdentityMaterialSource(
                "TPM-MATERIAL-001"
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
    }

    // =========================================================
    // DIFFERENT MACHINES
    // =========================================================

    [Fact]
    public void
        GetHardwareId_DifferentMaterial_ReturnsDifferentIds()
    {
        var firstProvider =
            new HardwareIdProvider(
                new FakeHardwareIdentityMaterialSource(
                    "TPM-MATERIAL-001"
                )
            );

        var secondProvider =
            new HardwareIdProvider(
                new FakeHardwareIdentityMaterialSource(
                    "TPM-MATERIAL-002"
                )
            );

        var first =
            firstProvider
                .GetHardwareId();

        var second =
            secondProvider
                .GetHardwareId();

        Assert.NotEqual(
            first,
            second
        );
    }

    // =========================================================
    // EXPECTED HASH
    // =========================================================

    [Fact]
    public void
        GetHardwareId_ValidMaterial_ReturnsExpectedSha256Id()
    {
        const string material =
            "TPM-MATERIAL-001";

        var provider =
            new HardwareIdProvider(
                new FakeHardwareIdentityMaterialSource(
                    material
                )
            );

        var expectedHash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    material
                )
            );

        var expected =
            "KIOSK-" +
            Convert.ToHexString(
                expectedHash
            );

        var result =
            provider
                .GetHardwareId();

        Assert.Equal(
            expected,
            result
        );
    }

    // =========================================================
    // WHITESPACE
    // =========================================================

    [Fact]
    public void
        GetHardwareId_OuterWhitespace_IsNormalized()
    {
        var cleanProvider =
            new HardwareIdProvider(
                new FakeHardwareIdentityMaterialSource(
                    "TPM-MATERIAL-001"
                )
            );

        var paddedProvider =
            new HardwareIdProvider(
                new FakeHardwareIdentityMaterialSource(
                    "   TPM-MATERIAL-001   "
                )
            );

        Assert.Equal(
            cleanProvider.GetHardwareId(),
            paddedProvider.GetHardwareId()
        );
    }

    // =========================================================
    // INVALID MATERIAL
    // =========================================================

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("     ")]
    public void
        GetHardwareId_EmptyMaterial_Throws(
            string material)
    {
        var provider =
            new HardwareIdProvider(
                new FakeHardwareIdentityMaterialSource(
                    material
                )
            );

        var exception =
            Assert.Throws<
                HardwareIdentityException
            >(
                provider.GetHardwareId
            );

        Assert.Contains(
            "empty",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    // =========================================================
    // SOURCE FAILURE
    // =========================================================

    [Fact]
    public void
        GetHardwareId_SourceThrows_WrapsFailure()
    {
        var provider =
            new HardwareIdProvider(
                new ThrowingHardwareIdentityMaterialSource(
                    new InvalidOperationException(
                        "TPM unavailable."
                    )
                )
            );

        var exception =
            Assert.Throws<
                HardwareIdentityException
            >(
                provider.GetHardwareId
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
    // CONSTRUCTOR
    // =========================================================

    [Fact]
    public void
        Constructor_NullSource_Throws()
    {
        Assert.Throws<
            ArgumentNullException
        >(
            () =>
                new HardwareIdProvider(
                    null!
                )
        );
    }

    // =========================================================
    // DEFAULT / FAIL CLOSED
    // =========================================================

    [Fact]
    public void
        GetHardwareId_DefaultConstructor_FailsClosed()
    {
        var provider =
            new HardwareIdProvider();

        Assert.Throws<
            HardwareIdentityException
        >(
            provider.GetHardwareId
        );
    }

    // =========================================================
    // FAKES
    // =========================================================

    private sealed class
        FakeHardwareIdentityMaterialSource
        : IHardwareIdentityMaterialSource
    {
        private readonly string
            _material;

        public FakeHardwareIdentityMaterialSource(
            string material)
        {
            _material =
                material;
        }

        public string GetIdentityMaterial()
        {
            return _material;
        }
    }

    private sealed class
        ThrowingHardwareIdentityMaterialSource
        : IHardwareIdentityMaterialSource
    {
        private readonly Exception
            _exception;

        public ThrowingHardwareIdentityMaterialSource(
            Exception exception)
        {
            _exception =
                exception;
        }

        public string GetIdentityMaterial()
        {
            throw _exception;
        }
    }
}