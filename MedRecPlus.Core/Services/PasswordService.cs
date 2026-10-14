// Password hashing (lock screen) + SQLCipher key derivation, both Argon2id.
// Mirrors the old app's SecurityService policy + encryption.rs KDF semantics.
//
// Stored password format: $argon2id$v=19$m=65536,t=3,p=4$<saltB64>$<hashB64>
// DB key: 32 bytes from Argon2id(password, per-db salt), hex-encoded for
//         PRAGMA key = "x'...'".

using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace MedRecPlus.Core.Services;

public static class PasswordService
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int MemoryKb = 65536;   // 64 MB
    private const int Iterations = 3;
    private const int Parallelism = 4;

    public static void ValidatePolicy(string password)
    {
        var errors = new List<string>();
        if (password.Length < 8) errors.Add("at least 8 characters");
        if (!password.Any(char.IsUpper)) errors.Add("an uppercase letter");
        if (!password.Any(char.IsLower)) errors.Add("a lowercase letter");
        if (!password.Any(char.IsDigit)) errors.Add("a digit");
        if (!password.Any(c => !char.IsLetterOrDigit(c))) errors.Add("a special character");
        if (errors.Count > 0)
            throw new ArgumentException("Password must contain " + string.Join(", ", errors) + ".");
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt);
        return $"$argon2id$v=19$m={MemoryKb},t={Iterations},p={Parallelism}" +
               $"${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string encoded)
    {
        try
        {
            var parts = encoded.Split('$');
            // ["", "argon2id", "v=19", "m=65536,t=3,p=4", salt, hash]
            if (parts.Length != 6 || parts[1] != "argon2id") return false;
            var salt = Convert.FromBase64String(parts[4]);
            var expected = Convert.FromBase64String(parts[5]);
            var actual = Derive(password, salt);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Derives the 32-byte SQLCipher key from the password + per-DB salt.</summary>
    public static byte[] DeriveDbKey(string password, byte[] salt)
    {
        if (salt.Length < 16) throw new ArgumentException("salt too short", nameof(salt));
        return Derive(password, salt);
    }

    public static string DbKeyHex(string password, byte[] salt) =>
        Convert.ToHexString(DeriveDbKey(password, salt)).ToLowerInvariant();

    private static byte[] Derive(string password, byte[] salt)
    {
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = Parallelism,
            Iterations = Iterations,
            MemorySize = MemoryKb,
        };
        return argon2.GetBytes(HashBytes);
    }
}
