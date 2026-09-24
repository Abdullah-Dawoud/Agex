// Signs or verifies SHA256SUMS.txt for AGEX releases (ECDSA P-256, SHA-256).
//
//   dotnet run tools/sign-release.cs -- new-key <private.pem>          create a key pair; prints the public key
//   dotnet run tools/sign-release.cs -- sign <SHA256SUMS.txt>          key from AGEX_RELEASE_SIGNING_KEY (PEM text)
//   dotnet run tools/sign-release.cs -- verify <SHA256SUMS.txt> <public.pem>
//
// Paste the printed public key into UpdateService.ReleasePublicKeyPem so AGEX
// accepts only releases signed with this key. Keep the private key in a
// secret store (for example the GitHub secret AGEX_RELEASE_SIGNING_KEY), never in the repository.
using System.Security.Cryptography;

if (args.Length < 2) { Console.Error.WriteLine("usage: new-key <file> | sign <sums> | verify <sums> <public.pem>"); return 2; }
switch (args[0])
{
    case "new-key":
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(args[1], key.ExportPkcs8PrivateKeyPem());
        Console.WriteLine(key.ExportSubjectPublicKeyInfoPem());
        Console.Error.WriteLine($"Private key written to {args[1]}. Store it as a secret and delete the file.");
        return 0;
    }
    case "sign":
    {
        var pem = Environment.GetEnvironmentVariable("AGEX_RELEASE_SIGNING_KEY");
        if (string.IsNullOrWhiteSpace(pem)) { Console.Error.WriteLine("AGEX_RELEASE_SIGNING_KEY is not set; release left unsigned."); return 3; }
        using var key = ECDsa.Create();
        key.ImportFromPem(pem);
        var signature = key.SignData(File.ReadAllBytes(args[1]), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        File.WriteAllBytes(args[1] + ".sig", signature);
        Console.WriteLine($"Signed {args[1]}");
        return 0;
    }
    case "verify" when args.Length >= 3:
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(args[2]));
        var ok = key.VerifyData(File.ReadAllBytes(args[1]), File.ReadAllBytes(args[1] + ".sig"), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Console.WriteLine(ok ? "Signature valid." : "Signature INVALID.");
        return ok ? 0 : 1;
    }
    default:
        Console.Error.WriteLine("unknown command");
        return 2;
}
