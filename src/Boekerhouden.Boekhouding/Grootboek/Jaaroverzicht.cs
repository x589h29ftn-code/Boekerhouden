using Boekerhouden.Domain.Rgs;

namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>Een post in de balans of W&amp;V: een RGS-hoofdpost (niveau 2) met zijn rekeningen.</summary>
/// <param name="Code">De RGS-code van de hoofdpost, of leeg voor rekeningen zonder (bekende) RGS-code.</param>
public sealed record Overzichtpost(string? Code, string Naam, IReadOnlyList<Proefbalansregel> Rekeningen)
{
    public decimal Saldo => Rekeningen.Sum(r => r.Saldo);
}

/// <summary>
/// Balans en winst-en-verliesrekening uit een proefbalans, gegroepeerd op RGS-hoofdpost in de officiële volgorde.
/// Activa zijn de posten met een normaal debetsaldo, passiva die met een creditsaldo; het resultaat van de periode staat
/// apart onder de passiva, zodat de balans sluit. Rekeningen zonder RGS-code komen onder "Niet gekoppeld".
/// </summary>
public sealed record Jaaroverzicht(
    Proefbalans Proefbalans,
    IReadOnlyList<Overzichtpost> Activa,
    IReadOnlyList<Overzichtpost> Passiva,
    IReadOnlyList<Overzichtpost> Resultaatposten)
{
    public const string NietGekoppeld = "Niet gekoppeld";

    public decimal TotaalActiva => Activa.Sum(p => p.Saldo);

    /// <summary>Het totaal van de passiva inclusief het resultaat, als positief bedrag.</summary>
    public decimal TotaalPassiva => -Passiva.Sum(p => p.Saldo) + Resultaat;

    public decimal Resultaat => Proefbalans.Resultaat;

    public static Jaaroverzicht Maak(Proefbalans proefbalans, RgsSchema? rgs)
    {
        ArgumentNullException.ThrowIfNull(proefbalans);
        var metSaldo = proefbalans.Regels.Where(r => r.Saldo != 0).ToList();
        var balans = Groepeer(metSaldo.Where(r => r.Rekening.IsBalans), rgs);
        var activa = balans.Where(p => IsActief(p, rgs)).ToList();
        var passiva = balans.Where(p => !IsActief(p, rgs)).ToList();
        var resultaat = Groepeer(metSaldo.Where(r => !r.Rekening.IsBalans), rgs);
        return new Jaaroverzicht(proefbalans, activa, passiva, resultaat);
    }

    private static bool IsActief(Overzichtpost post, RgsSchema? rgs) =>
        rgs?.Zoek(post.Code) is { Kant: { } kant } ? kant == Saldokant.Debet : post.Saldo >= 0;

    private static List<Overzichtpost> Groepeer(IEnumerable<Proefbalansregel> regels, RgsSchema? rgs)
    {
        var groepen = regels
            .GroupBy(r => Hoofdpost(r.Rekening.RgsCode, rgs))
            .Select(g => new
            {
                Hoofdpost = g.Key,
                Post = new Overzichtpost(g.Key?.Code, g.Key?.Omschrijving ?? NietGekoppeld, g.OrderBy(r => r.Rekening.Nummer, Rekeningnummers.Volgorde).ToList()),
            })
            .OrderBy(g => g.Hoofdpost is null ? 1 : 0)
            .ThenBy(g => g.Hoofdpost?.Volgorde ?? 0)
            .Select(g => g.Post)
            .ToList();

        // Niet gekoppelde balansrekeningen: activa en passiva apart, op de kant van het saldo.
        var niet = groepen.FirstOrDefault(p => p.Code is null);
        if (niet is not null && niet.Rekeningen.Any(r => r.Saldo > 0) && niet.Rekeningen.Any(r => r.Saldo < 0) && niet.Rekeningen[0].Rekening.IsBalans)
        {
            groepen.Remove(niet);
            groepen.Add(niet with { Rekeningen = niet.Rekeningen.Where(r => r.Saldo > 0).ToList() });
            groepen.Add(niet with { Rekeningen = niet.Rekeningen.Where(r => r.Saldo < 0).ToList() });
        }

        return groepen;
    }

    private static RgsCode? Hoofdpost(string? rgsCode, RgsSchema? rgs)
    {
        // Zonder de code zelf in het schema (alleen de hoofdposten bekend): de hoofdpost is het begin van de code.
        var code = rgs?.Zoek(rgsCode) ?? (rgsCode is { Length: >= 4 } ? rgs?.Zoek(rgsCode[..4]) : null);
        if (code is null)
        {
            return null;
        }

        return code.Niveau == 2 ? code : rgs!.Voorouders(code.Code).FirstOrDefault(c => c.Niveau == 2);
    }
}
