using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>Een geboekte journaalpost met zijn plek in de keten.</summary>
public sealed record Ketenschakel(long Volgnummer, Journaalpost Post, string Hash, string VorigeHash);

/// <summary>
/// De hash-keten over het journaal van één administratie. Elke journaalpost krijgt een volgnummer en een SHA-256
/// over zijn inhoud plus de hash van de vorige post. Wie buiten de app een bedrag, rekening of datum wijzigt, of een
/// post verwijdert of tussenvoegt, breekt de keten; <see cref="Controleer"/> wijst aan waar.
/// </summary>
public static class Journaalketen
{
    /// <summary>De "vorige hash" van de eerste post.</summary>
    public static string Begin { get; } = new('0', 64);

    public static string Hash(Journaalpost post, long volgnummer, string vorigeHash)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(vorigeHash);

        // Een vaste, ondubbelzinnige vorm: JSON met de velden in een vaste volgorde en bedragen met twee decimalen.
        var inhoud = JsonSerializer.Serialize(new object?[]
        {
            volgnummer,
            vorigeHash,
            post.Dagboek,
            post.Boekstuk,
            post.Datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            post.Omschrijving,
            post.Regels.Select(r => new object?[]
            {
                r.Rekeningnummer,
                r.Bedrag.ToString("0.00", CultureInfo.InvariantCulture),
                r.Omschrijving,
                r.BtwCode,
                r.Relatie,
                r.Factuurnummer,
            }).ToArray(),
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inhoud)));
    }

    /// <summary>
    /// Controleert de keten op volgorde van volgnummer. Geeft de volgnummers waar het misgaat: een hash die niet bij de
    /// inhoud past, een verwijzing naar een andere vorige post, of een ontbrekend volgnummer. Leeg = de keten is heel.
    /// </summary>
    public static IReadOnlyList<long> Controleer(IEnumerable<Ketenschakel> schakels)
    {
        ArgumentNullException.ThrowIfNull(schakels);
        var fout = new List<long>();
        var vorige = Begin;
        var verwacht = 1L;
        foreach (var schakel in schakels.OrderBy(s => s.Volgnummer))
        {
            if (schakel.Volgnummer != verwacht || schakel.VorigeHash != vorige || schakel.Hash != Hash(schakel.Post, schakel.Volgnummer, schakel.VorigeHash))
            {
                fout.Add(schakel.Volgnummer);
            }

            vorige = schakel.Hash;
            verwacht = schakel.Volgnummer + 1;
        }

        return fout;
    }
}
