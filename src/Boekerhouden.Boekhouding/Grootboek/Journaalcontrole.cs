using Boekerhouden.Domain;

namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>De controles op een journaalpost voordat hij geboekt wordt.</summary>
public static class Journaalcontrole
{
    public const int MaximumRegels = 500;

    public static IReadOnlyList<string> Controleer(Journaalpost post, Rekeningschema schema, IEnumerable<Dagboek> dagboeken)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(dagboeken);
        var fouten = new List<string>();
        if (!dagboeken.Any(d => d.Code == post.Dagboek))
        {
            fouten.Add($"Dagboek {post.Dagboek} bestaat niet.");
        }

        if (string.IsNullOrWhiteSpace(post.Boekstuk))
        {
            fouten.Add("Geef de boeking een boekstuknummer.");
        }

        if (string.IsNullOrWhiteSpace(post.Omschrijving))
        {
            fouten.Add("Geef de boeking een omschrijving.");
        }

        var regels = post.Regels;
        if (regels.Count < 2)
        {
            fouten.Add("Een boeking heeft minstens twee regels nodig.");
        }

        if (regels.Count > MaximumRegels)
        {
            fouten.Add($"Een boeking heeft hoogstens {MaximumRegels} regels.");
        }

        Regelnummers(regels, r => schema.Zoek(r.Rekeningnummer) is null, "heeft een onbekende rekening", fouten);
        Regelnummers(regels, r => r.Bedrag == 0, "heeft geen bedrag", fouten);
        Regelnummers(regels, r => decimal.Round(r.Bedrag, 2) != r.Bedrag, "heeft meer dan twee decimalen", fouten);

        var debet = regels.Where(r => r.Bedrag > 0).Sum(r => r.Bedrag);
        var credit = -regels.Where(r => r.Bedrag < 0).Sum(r => r.Bedrag);
        if (debet != credit)
        {
            fouten.Add($"De boeking is niet in evenwicht: debet {Bedragen.Euro(debet)}, credit {Bedragen.Euro(credit)}, verschil {Bedragen.Euro(debet - credit)}.");
        }

        return fouten;
    }

    private static void Regelnummers(IReadOnlyList<Journaalregel> regels, Func<Journaalregel, bool> fout, string melding, List<string> fouten)
    {
        var nummers = regels.Select((r, i) => (r, i)).Where(x => fout(x.r)).Select(x => x.i + 1).ToList();
        if (nummers.Count > 0)
        {
            fouten.Add($"Regel {string.Join(", ", nummers)} {melding}.");
        }
    }
}
