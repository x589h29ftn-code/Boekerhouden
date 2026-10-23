using Boekerhouden.Domain.Rgs;

namespace Boekerhouden.Domain.Rubricering;

/// <summary>Een eigen post die het kantoor onder een RGS-code heeft toegevoegd (bijv. "Overige vorderingen 1").</summary>
public sealed record EigenPost(Guid Id, string OuderCode, string Naam);

/// <summary>Waar een grootboekrekening aan gekoppeld is: een RGS-code, eventueel via een eigen post onder die code.</summary>
public sealed record KoppelDoel(string Code, Guid? EigenPostId = null);

/// <summary>Het saldo van een grootboekrekening in het boekjaar en het vorige jaar (null = niet ingelezen).</summary>
public sealed record RekeningSaldo(Guid RekeningId, string Nummer, string Omschrijving, decimal? Huidig, decimal? Vorig);

public readonly record struct PostTotaal(decimal Huidig, decimal Vorig, int Aantal);

public enum SignaalSoort
{
    /// <summary>Rekening met saldo maar zonder koppeling.</summary>
    Ongekoppeld,

    /// <summary>
    /// Balanssaldo aan de andere kant dan de RGS-grootboekcode (niveau 4), en de code heeft geen omslagcode. Op een
    /// subpost (niveau 3) staan aanschafwaarde en afschrijving vaak samen; daar is beide kanten normaal.
    /// </summary>
    VerkeerdeKant,

    /// <summary>Gekoppeld aan een code die niet (meer) in de actieve RGS-versie staat, of inactief is.</summary>
    OnbekendeCode,
}

public sealed record Signaal(SignaalSoort Soort, Guid RekeningId, string Tekst);

/// <summary>
/// De proefbalans per post: elk saldo telt mee bij zijn koppeling en bij alle bovenliggende posten. Staat een saldo
/// aan de andere kant dan de RGS-code en heeft die code een omslagcode, dan telt het (per jaar apart) bij de omslagcode.
/// </summary>
public sealed class PostBalans
{
    public const string EigenVoorvoegsel = "eigen:";

    private readonly Dictionary<string, Teller> totalen = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> plaatsing = [];
    private readonly List<Signaal> signalen = [];

    private PostBalans()
    {
    }

    /// <summary>Waar elke gekoppelde rekening in het boekjaar staat: de sleutel van de post, na omslag.</summary>
    public IReadOnlyDictionary<Guid, string> Plaatsing => plaatsing;

    public IReadOnlyList<Signaal> Signalen => signalen;

    public PostTotaal Balans => Totaal("B");

    public PostTotaal WinstEnVerlies => Totaal("W");

    public PostTotaal Ongekoppeld { get; private set; }

    /// <summary>Resultaat: het saldo van de W&amp;V met omgekeerd teken (credit is winst).</summary>
    public decimal ResultaatHuidig => -WinstEnVerlies.Huidig;

    public decimal ResultaatVorig => -WinstEnVerlies.Vorig;

    /// <summary>De sleutel van een post: de RGS-code, of "eigen:&lt;id&gt;" voor een eigen post.</summary>
    public static string Sleutel(KoppelDoel doel)
    {
        ArgumentNullException.ThrowIfNull(doel);
        return doel.EigenPostId is { } id ? EigenSleutel(id) : doel.Code;
    }

    public static string EigenSleutel(Guid id) => EigenVoorvoegsel + id.ToString("N");

    public PostTotaal Totaal(string sleutel) =>
        totalen.TryGetValue(sleutel, out var t) ? new PostTotaal(t.Huidig, t.Vorig, t.Rekeningen.Count) : default;

    public static PostBalans Bereken(
        RgsSchema schema,
        IReadOnlyCollection<EigenPost> eigenPosten,
        IReadOnlyDictionary<Guid, KoppelDoel> koppelingen,
        IReadOnlyCollection<RekeningSaldo> saldi)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(eigenPosten);
        ArgumentNullException.ThrowIfNull(koppelingen);
        ArgumentNullException.ThrowIfNull(saldi);

        var balans = new PostBalans();
        var posten = eigenPosten.ToDictionary(e => e.Id);
        var ongekoppeld = new Teller();

        foreach (var saldo in saldi)
        {
            var huidig = saldo.Huidig ?? 0;
            var vorig = saldo.Vorig ?? 0;
            if (!koppelingen.TryGetValue(saldo.RekeningId, out var doel))
            {
                if (huidig != 0 || vorig != 0)
                {
                    ongekoppeld.Tel(saldo.RekeningId, huidig, vorig);
                    balans.signalen.Add(new Signaal(SignaalSoort.Ongekoppeld, saldo.RekeningId,
                        $"{saldo.Nummer} {saldo.Omschrijving} is nog niet gekoppeld."));
                }

                continue;
            }

            var code = schema.Zoek(doel.Code);
            if (code is null || code.Inactief)
            {
                balans.signalen.Add(new Signaal(SignaalSoort.OnbekendeCode, saldo.RekeningId,
                    $"{saldo.Nummer} {saldo.Omschrijving} is gekoppeld aan {doel.Code}, die niet (meer) actief is in RGS {schema.Versie}."));
            }
            else if (code is { IsBalans: true, Omslagcode: null, Niveau: 4 } && !code.IsNormaleKant(huidig))
            {
                balans.signalen.Add(new Signaal(SignaalSoort.VerkeerdeKant, saldo.RekeningId,
                    $"{saldo.Nummer} {saldo.Omschrijving} staat {(huidig > 0 ? "debet" : "credit")}; {code.Omschrijving} ({code.Code}) is normaal {(code.Kant == Saldokant.Debet ? "debet" : "credit")}."));
            }

            balans.plaatsing[saldo.RekeningId] = balans.Plaats(schema, posten, doel, code, saldo.RekeningId, huidig, huidig: true);
            balans.Plaats(schema, posten, doel, code, saldo.RekeningId, vorig, huidig: false);
        }

        balans.Ongekoppeld = new PostTotaal(ongekoppeld.Huidig, ongekoppeld.Vorig, ongekoppeld.Rekeningen.Count);
        return balans;
    }

    private string Plaats(RgsSchema schema, Dictionary<Guid, EigenPost> posten, KoppelDoel doel, RgsCode? code, Guid rekening, decimal bedrag, bool huidig)
    {
        if (code?.Omslagcode is { } omslag && !code.IsNormaleKant(bedrag) && schema.Zoek(omslag) is not null)
        {
            TelMetVoorouders(schema, omslag, rekening, bedrag, huidig);
            return omslag;
        }

        if (doel.EigenPostId is { } id && posten.TryGetValue(id, out var post))
        {
            Tel(EigenSleutel(id), rekening, bedrag, huidig);
            TelMetVoorouders(schema, post.OuderCode, rekening, bedrag, huidig);
            return EigenSleutel(id);
        }

        TelMetVoorouders(schema, doel.Code, rekening, bedrag, huidig);
        return doel.Code;
    }

    private void TelMetVoorouders(RgsSchema schema, string code, Guid rekening, decimal bedrag, bool huidig)
    {
        Tel(code, rekening, bedrag, huidig);
        if (schema.Zoek(code) is null)
        {
            // Onbekende code: tel in elk geval mee bij balans of W&V, zodat de totalen blijven kloppen.
            Tel(code[..1], rekening, bedrag, huidig);
            return;
        }

        foreach (var ouder in schema.Voorouders(code))
        {
            Tel(ouder.Code, rekening, bedrag, huidig);
        }
    }

    private void Tel(string sleutel, Guid rekening, decimal bedrag, bool huidig)
    {
        if (!totalen.TryGetValue(sleutel, out var teller))
        {
            teller = new Teller();
            totalen[sleutel] = teller;
        }

        teller.Tel(rekening, huidig ? bedrag : 0, huidig ? 0 : bedrag);
    }

    private sealed class Teller
    {
        public decimal Huidig { get; private set; }

        public decimal Vorig { get; private set; }

        public HashSet<Guid> Rekeningen { get; } = [];

        public void Tel(Guid rekening, decimal huidig, decimal vorig)
        {
            Huidig += huidig;
            Vorig += vorig;
            Rekeningen.Add(rekening);
        }
    }
}
