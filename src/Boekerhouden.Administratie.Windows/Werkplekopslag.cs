using System.Security.Cryptography;
using Boekerhouden.Administratie.Diensten;

namespace Boekerhouden.Administratie.Windows;

/// <summary>
/// De sleutel van deze werkplek: één keer gemaakt en in de gegevensmap bewaard, versleuteld met het Windows-account
/// (DPAPI). Alleen wie als deze Windows-gebruiker op deze pc is aangemeld, kan ermee inloggen bij de kantoorserver.
/// </summary>
internal static class Werkplekopslag
{
    private const string Bestand = "werkplek.sleutel";
    private static readonly byte[] Doel = "Sifer werkplek"u8.ToArray();

    public static Werkpleksleutel Sleutel(string map)
    {
        var pad = Path.Combine(map, Bestand);
        if (File.Exists(pad))
        {
            try
            {
                return Werkpleksleutel.Van(ProtectedData.Unprotect(File.ReadAllBytes(pad), Doel, DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException)
            {
                // Niet te lezen (bijv. de map is van een andere pc gekopieerd): een nieuwe sleutel; opnieuw koppelen.
            }
        }

        var nieuw = Werkpleksleutel.Nieuw();
        Directory.CreateDirectory(map);
        File.WriteAllBytes(pad, ProtectedData.Protect(nieuw.Geheim, Doel, DataProtectionScope.CurrentUser));
        return nieuw;
    }
}
