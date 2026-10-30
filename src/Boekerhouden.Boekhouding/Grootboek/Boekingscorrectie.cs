namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>
/// Een geboekte journaalpost aanpassen. Het journaal wijzigt nooit: een aanpassing is een tegenboeking (alles terug)
/// plus een nieuwe boeking, in hetzelfde dagboek. Regels op rekeningen die vastzitten aan iets buiten de boeking — de
/// bank (het afschrift), debiteuren en crediteuren (de openstaande posten) — blijven precies gelijk, en dan ook de datum.
/// </summary>
public static class Boekingscorrectie
{
    /// <summary>Rekeningen waarvan de regels bij een aanpassing niet veranderen.</summary>
    public static bool IsVast(Rekening? rekening) =>
        rekening?.Functie is Rekeningfunctie.Bank or Rekeningfunctie.Debiteuren or Rekeningfunctie.Crediteuren;

    /// <summary>Wat er niet klopt aan de aanpassing van <paramref name="oud"/> naar <paramref name="nieuw"/>; leeg = in orde.</summary>
    public static IReadOnlyList<string> Controleer(Journaalpost oud, Journaalpost nieuw, Rekeningschema schema)
    {
        ArgumentNullException.ThrowIfNull(oud);
        ArgumentNullException.ThrowIfNull(nieuw);
        ArgumentNullException.ThrowIfNull(schema);
        var fouten = new List<string>();
        if (nieuw.Dagboek != oud.Dagboek)
        {
            fouten.Add("Een aanpassing blijft in hetzelfde dagboek.");
        }

        var vastOud = Vast(oud, schema);
        var vastNieuw = Vast(nieuw, schema);
        if (!vastOud.SequenceEqual(vastNieuw))
        {
            var rekeningen = vastOud.Concat(vastNieuw).Select(r => r.Rekeningnummer).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            fouten.Add($"De regels op {string.Join(", ", rekeningen)} horen bij het bankafschrift of de openstaande posten en blijven gelijk.");
        }

        if (vastOud.Count > 0 && nieuw.Datum != oud.Datum)
        {
            fouten.Add("De datum blijft gelijk: de boeking hoort bij het bankafschrift of de openstaande posten.");
        }

        if (fouten.Count == 0 && Gelijk(oud, nieuw))
        {
            fouten.Add("Er is niets gewijzigd.");
        }

        return fouten;
    }

    private static List<Journaalregel> Vast(Journaalpost post, Rekeningschema schema) =>
        [.. post.Regels.Where(r => IsVast(schema.Zoek(r.Rekeningnummer)))
            .Select(r => new Journaalregel(r.Rekeningnummer, r.Bedrag, null, null, Leeg(r.Relatie), Leeg(r.Factuurnummer)))
            .OrderBy(r => r.Rekeningnummer, StringComparer.Ordinal).ThenBy(r => r.Bedrag).ThenBy(r => r.Relatie, StringComparer.Ordinal).ThenBy(r => r.Factuurnummer, StringComparer.Ordinal)];

    private static bool Gelijk(Journaalpost a, Journaalpost b) =>
        a.Datum == b.Datum && a.Omschrijving.Trim() == b.Omschrijving.Trim() && a.Regels.Count == b.Regels.Count
        && a.Regels.Zip(b.Regels).All(x => x.First with { Omschrijving = Leeg(x.First.Omschrijving), BtwCode = Leeg(x.First.BtwCode), Relatie = Leeg(x.First.Relatie), Factuurnummer = Leeg(x.First.Factuurnummer) }
            == x.Second with { Omschrijving = Leeg(x.Second.Omschrijving), BtwCode = Leeg(x.Second.BtwCode), Relatie = Leeg(x.Second.Relatie), Factuurnummer = Leeg(x.Second.Factuurnummer) });

    private static string? Leeg(string? tekst) => string.IsNullOrWhiteSpace(tekst) ? null : tekst.Trim();
}
