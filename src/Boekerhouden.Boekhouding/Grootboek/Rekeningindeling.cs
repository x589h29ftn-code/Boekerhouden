using Boekerhouden.Domain.Rgs;

namespace Boekerhouden.Boekhouding.Grootboek;

/// <summary>
/// Waar een rekening staat in de uitgebreide presentatie: een sectie (activa, passiva, winst-en-verliesrekening), soms
/// een tussenkop (vaste of vlottende activa) en een groep, de RGS-hoofdpost (bijv. materiële vaste activa).
/// </summary>
/// <param name="Volgorde">Om de groepen in de volgorde van balans en W&amp;V te zetten.</param>
public sealed record Indelingsgroep(int Volgorde, string Sectie, string? Tussenkop, string Code, string Naam);

/// <summary>
/// De uitgebreide presentatie (kolommenbalans e.d.) op basis van de RGS-code van de rekening: de hoofdpost waarmee de
/// code begint (<see cref="RgsHoofdposten"/>; <c>BMvaBedMvb</c> → materiële vaste activa). Een rekening zonder RGS-code,
/// of met een hoofdpost die hier niet staat, komt onder "Niet ingedeeld" in de balans of de W&amp;V.
/// </summary>
public static class Rekeningindeling
{
    public const string Activa = "Activa";
    public const string Passiva = "Passiva";
    public const string WinstEnVerlies = "Winst- en verliesrekening";

    private static readonly Dictionary<string, string> Tussenkoppen = new(StringComparer.Ordinal)
    {
        ["BIva"] = "Vaste activa",
        ["BMva"] = "Vaste activa",
        ["BVrd"] = "Vlottende activa",
        ["BVor"] = "Vlottende activa",
        ["BLim"] = "Vlottende activa",
    };

    /// <summary>Kortere namen waar de RGS-naam lang is.</summary>
    private static readonly Dictionary<string, string> Namen = new(StringComparer.Ordinal)
    {
        ["BEiv"] = "Eigen vermogen",
        ["WPer"] = "Personeelskosten",
        ["WAfs"] = "Afschrijvingen",
    };

    /// <summary>
    /// De overige bedrijfskosten apart per soort (RGS niveau 3), onder de tussenkop "Overige bedrijfskosten"; wat daar
    /// niet onder valt, komt bij "Andere bedrijfskosten".
    /// </summary>
    private static readonly (string Code, string Naam)[] Bedrijfskosten =
    [
        ("WBedHui", "Huisvestingskosten"),
        ("WBedEem", "Exploitatie- en machinekosten"),
        ("WBedVkk", "Verkoopkosten"),
        ("WBedAut", "Autokosten"),
        ("WBedKan", "Kantoorkosten"),
        ("WBedAss", "Assurantiekosten"),
        ("WBedAea", "Accountants- en advieskosten"),
        ("WBedAlk", "Algemene kosten"),
    ];

    private const string OverigeBedrijfskosten = "WBed";

    private static readonly List<Indelingsgroep> Groepen =
    [
        .. RgsHoofdposten.Posten
            .SelectMany(p => p.Code == OverigeBedrijfskosten
                ? [.. Bedrijfskosten.Select(b => (b.Code, b.Naam, p.Kant)), (p.Code, "Andere bedrijfskosten", p.Kant)]
                : new[] { (p.Code, Naam: Namen.GetValueOrDefault(p.Code) ?? p.Naam, p.Kant) })
            .Select((p, i) => new Indelingsgroep(
                i,
                p.Code[0] == 'W' ? WinstEnVerlies : p.Kant == Saldokant.Debet ? Activa : Passiva,
                p.Code.StartsWith(OverigeBedrijfskosten, StringComparison.Ordinal) ? "Overige bedrijfskosten" : Tussenkoppen.GetValueOrDefault(p.Code),
                p.Code,
                p.Naam)),
    ];

    private static readonly Indelingsgroep NietIngedeeldBalans = new(Groepen.Count(g => g.Sectie != WinstEnVerlies), Passiva, null, "B?", "Niet ingedeeld (balans)");

    private static readonly Indelingsgroep NietIngedeeldWinstEnVerlies = new(Groepen.Count + 1, WinstEnVerlies, null, "W?", "Niet ingedeeld (W&V)");

    /// <summary>De groep van een rekening.</summary>
    public static Indelingsgroep Groep(Rekening rekening)
    {
        ArgumentNullException.ThrowIfNull(rekening);
        if (rekening.RgsCode is { Length: >= 4 } rgs && Groepen.FirstOrDefault(g => rgs.StartsWith(g.Code, StringComparison.Ordinal)) is { } groep)
        {
            return groep;
        }

        return rekening.IsBalans ? NietIngedeeldBalans : NietIngedeeldWinstEnVerlies;
    }

    /// <summary>
    /// Regels in de volgorde van de presentatie: per groep (op <see cref="Indelingsgroep.Volgorde"/>), daarbinnen op
    /// rekeningnummer.
    /// </summary>
    public static IReadOnlyList<(Indelingsgroep Groep, IReadOnlyList<T> Regels)> Deel<T>(IEnumerable<T> regels, Func<T, Rekening> rekening)
    {
        ArgumentNullException.ThrowIfNull(regels);
        ArgumentNullException.ThrowIfNull(rekening);
        return
        [
            .. regels.GroupBy(r => Groep(rekening(r)))
                .OrderBy(g => g.Key.Volgorde)
                .Select(g => (g.Key, (IReadOnlyList<T>)[.. g.OrderBy(r => rekening(r).Nummer, Rekeningnummers.Volgorde)])),
        ];
    }
}
