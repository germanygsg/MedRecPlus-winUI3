namespace MedRecPlus.Core.Services;

public static class CredentialStore
{
    // Synthetic verification databases use a separate resource, never the clinic key.
    private static string Resource => Path.GetFullPath(AppData.BaseDir).TrimEnd(Path.DirectorySeparatorChar)
        .Equals(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MedRecPlusOffline"), StringComparison.OrdinalIgnoreCase)
        ? "MedRecPlusOffline" : "MedRecPlusOffline.Test." + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(AppData.BaseDir))));
    private const string UserName = "dbkey";

    // Return success so Settings can refuse to disable the lock on vault failure.
    public static bool SaveDbKey(string keyHex)
    {
        try
        {
            if (keyHex.Length != 64 || !keyHex.All(Uri.IsHexDigit)) return false;
#if WINDOWS
            DeleteDbKey();
            new Windows.Security.Credentials.PasswordVault().Add(
                new Windows.Security.Credentials.PasswordCredential(Resource, UserName, keyHex));
            return TryGetDbKey(out var stored) && stored == keyHex;
#else
            return false;
#endif
        }
        catch (Exception) { return false; }
    }

    public static bool TryGetDbKey(out string keyHex)
    {
        keyHex = "";
        try
        {
#if WINDOWS
            var credential = new Windows.Security.Credentials.PasswordVault().Retrieve(Resource, UserName);
            credential.RetrievePassword();
            if (credential.Password.Length != 64 || !credential.Password.All(Uri.IsHexDigit)) return false;
            keyHex = credential.Password;
            return true;
#else
            return false;
#endif
        }
        catch (Exception) { return false; }
    }

    public static void DeleteDbKey()
    {
        try
        {
#if WINDOWS
            var vault = new Windows.Security.Credentials.PasswordVault();
            vault.Remove(vault.Retrieve(Resource, UserName));
#endif
        }
        catch (Exception) { /* Missing/unavailable vault is safe: startup requires unlock. */ }
    }
}
