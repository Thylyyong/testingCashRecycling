using System;

namespace SelfCheckoutKiosk.Core.Licensing;

public static class DevLicenseKeys
{
    public const string PublicKeySpkiBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEq8hekTAc/oTtHgKNZbhqEMYzsOw89DcVqwXEAAyHCuJ0shDB8iSirMX6pX3PbXEbix5Ol1yB5nTniLzOAqESrg==";

    public static byte[] PublicKeyBytes => Convert.FromBase64String(PublicKeySpkiBase64);

    public static byte[]? PublicKeyOrNull => PublicKeyBytes;
}