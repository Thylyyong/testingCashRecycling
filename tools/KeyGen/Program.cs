using System.Security.Cryptography;
using var rsa = RSA.Create(2048);
Console.WriteLine(rsa.ExportSubjectPublicKeyInfoPem());
Console.WriteLine("---SPLIT---");
Console.WriteLine(rsa.ExportPkcs8PrivateKeyPem());
