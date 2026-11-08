using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;

namespace FsocietyNet.Services;

/// <summary>
/// VPN binaries embedded in the exe (see csproj), extracted to %ProgramData%\FsocietyNet\bin (admin-only ACL).
/// Every file is hash-checked against the pinned value and then kept open with FileShare.Read for the lifetime
/// of the app, so nobody can replace it between the check and the start.
/// </summary>
public static class Binaries
{
    public sealed record Bin(string File, string Resource, string? ZipEntry, string Sha256);

    // Official builds: AmneziaWG 2.0.0 (signed by Privacy Technologies OU), sing-box 1.14.4 (SagerNet),
    // byedpi 17.3 (hufrea). Updating a binary = new file in Resources + new hash here + tools/fetch-binaries.ps1.
    public static readonly Bin AmneziaWG = new("amneziawg.exe", "bin.amneziawg.exe", null,
        "5B00905ED02619FE149CEAFC898E79993D4455A0CDFA92072B3BB9AEE7B2D537");
    public static readonly Bin Wintun = new("wintun.dll", "bin.wintun.dll", null,
        "E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE");
    public static readonly Bin SingBox = new("sing-box.exe", "bin.sing-box.zip", "sing-box.exe",
        "8F30682EE198855440D2A7FE070D5D3653793B2D01BB8C85EEA88F93BBE7C8B9");
    public static readonly Bin CiaDpi = new("ciadpi.exe", "bin.byedpi.zip", "ciadpi.exe",
        "EB53CEEEB981CC6735AC24BB1E51E725280B86630E80FDF19DDC4EE4A5B54EF4");

    static readonly Dictionary<string, FileStream> Locks = [];
    static readonly object Gate = new();

    public static bool Embedded(Bin b) => Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains(b.Resource);

    /// <summary>Full path of a verified binary, or null when this build does not contain it.</summary>
    public static string? Get(Bin b)
    {
        lock (Gate)
        {
            var path = Path.Combine(Paths.BinDir, b.File);
            if (Locks.ContainsKey(b.File)) return path;
            if (!Embedded(b)) return null;

            if (!File.Exists(path) || !HashOk(path, b.Sha256))
            {
                Extract(b, path);
                if (!HashOk(path, b.Sha256)) throw new InvalidDataException($"{b.File}: hash mismatch");
            }
            Locks[b.File] = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Log.I($"bin {b.File} ok");
            return path;
        }
    }

    static bool HashOk(string path, string sha)
    {
        try
        {
            using var f = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(f)).Equals(sha, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    static void Extract(Bin b, string path)
    {
        Directory.CreateDirectory(Paths.BinDir);
        using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream(b.Resource)!;
        var tmp = path + ".new";
        if (b.ZipEntry == null)
        {
            using var f = File.Create(tmp);
            res.CopyTo(f);
        }
        else
        {
            using var zip = new ZipArchive(res, ZipArchiveMode.Read);
            var entry = zip.Entries.First(e => e.Name.Equals(b.ZipEntry, StringComparison.OrdinalIgnoreCase));
            using var src = entry.Open();
            using var f = File.Create(tmp);
            src.CopyTo(f);
        }
        File.Move(tmp, path, true);
    }
}

/// <summary>
/// X25519 (RFC 7748) for the AmneziaWG key pair: the private key is created here and never leaves the PC,
/// the server only gets the public one (as the Android app since 0.0.3.9). BigInteger ladder — not constant
/// time, which is fine for a one-off local key generation.
/// </summary>
public static class X25519
{
    static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    const int A24 = 121665;

    public static (string priv, string pub) NewKeyPair()
    {
        var k = RandomNumberGenerator.GetBytes(32);
        k[0] &= 248; k[31] &= 127; k[31] |= 64;
        var basePoint = new byte[32]; basePoint[0] = 9;
        return (Convert.ToBase64String(k), Convert.ToBase64String(ScalarMult(k, basePoint)));
    }

    public static byte[] ScalarMult(byte[] scalar, byte[] u)
    {
        var k = (byte[])scalar.Clone();
        k[0] &= 248; k[31] &= 127; k[31] |= 64;
        var ub = (byte[])u.Clone(); ub[31] &= 127;
        BigInteger x1 = new(ub, isUnsigned: true), x2 = 1, z2 = 0, x3 = x1, z3 = 1;
        int swap = 0;
        for (int t = 254; t >= 0; t--)
        {
            int kt = (k[t >> 3] >> (t & 7)) & 1;
            swap ^= kt;
            if (swap == 1) { (x2, x3) = (x3, x2); (z2, z3) = (z3, z2); }
            swap = kt;
            var a = M(x2 + z2); var aa = M(a * a);
            var b = M(x2 - z2); var bb = M(b * b);
            var e = M(aa - bb);
            var c = M(x3 + z3); var d = M(x3 - z3);
            var da = M(d * a); var cb = M(c * b);
            x3 = M((da + cb) * (da + cb));
            z3 = M(x1 * M((da - cb) * (da - cb)));
            x2 = M(aa * bb);
            z2 = M(e * (aa + A24 * e));
        }
        if (swap == 1) { (x2, x3) = (x3, x2); (z2, z3) = (z3, z2); }
        var r = M(x2 * BigInteger.ModPow(z2, P - 2, P));
        var bytes = r.ToByteArray(isUnsigned: true);
        var outp = new byte[32];
        Array.Copy(bytes, outp, Math.Min(32, bytes.Length));
        return outp;
    }

    static BigInteger M(BigInteger v) { var r = v % P; return r.Sign < 0 ? r + P : r; }

    /// <summary>RFC 7748 §6.1 test vector (Alice).</summary>
    public static bool SelfTest()
    {
        var a = Convert.FromHexString("77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a");
        var basePoint = new byte[32]; basePoint[0] = 9;
        return Convert.ToHexString(ScalarMult(a, basePoint)).Equals(
            "8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a", StringComparison.OrdinalIgnoreCase);
    }
}
