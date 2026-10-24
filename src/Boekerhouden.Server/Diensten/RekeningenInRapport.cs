using Boekerhouden.Domain.Rapport;
using Boekerhouden.Domain.Rgs;
using Boekerhouden.Domain.Rubricering;

namespace Boekerhouden.Server.Diensten;

/// <param name="Saldo">Zoals in het rapport: positief aan de normale kant van de post (passiva en opbrengsten credit); null als de
/// rekening dat jaar elders staat (omslag).</param>
/// <param name="Plek">Waar de rekening nu staat (RGS-code of eigen post), voor de tooltip.</param>
public sealed record RekeningInPost(Guid Id, string Nummer, string Omschrijving, decimal? Saldo, decimal? SaldoVorig, string Plek);

/// <param name="Sleutel">De post (RGS-code of eigen post) waarvan de rekeningen getoond worden.</param>
public sealed record PostRekeningen(string Sleutel, string Titel, IReadOnlyList<RekeningInPost> Rekeningen);

/// <summary>
/// Rubriceren vanuit het rapport: welke grootboekrekeningen onder een regel van het rapport vallen, en aan welke
/// post een rekening wordt gekoppeld als je hem op een regel neerzet.
/// </summary>
public static class RekeningenInRapport
{
    /// <summary>
    /// De rekeningen onder de post van een regel: bij een (sub)post alles wat daaronder staat (ook via een eigen post),
    /// bij een eigen post die rekeningen, bij een rekening de rekeningen van zijn subpost (of eigen post). Dit jaar of
    /// vorig jaar (na omslag), met saldo.
    /// </summary>
    public static PostRekeningen? Van(RubriceerOverzicht r, Bron bron)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(bron);
        if (r.Schema is null || r.Balans is null)
        {
            return null;
        }

        var sleutel = bron.Soort switch
        {
            Onderwerp.Post or Onderwerp.EigenPost => bron.Sleutel,
            Onderwerp.Rekening or Onderwerp.Uitsplitsing when bron.Id is { } id => PostVanRekening(r, id),
            _ => null,
        };
        if (sleutel is null)
        {
            return null;
        }

        var code = sleutel.StartsWith(PostBalans.EigenVoorvoegsel, StringComparison.Ordinal)
            ? r.EigenPosten.FirstOrDefault(e => PostBalans.EigenSleutel(e.Id) == sleutel)?.OuderCode
            : sleutel;
        var teken = code is not null && r.Schema.Zoek(code)?.Kant == Saldokant.Credit ? -1 : 1;
        var rekeningen = r.Rekeningen
            .Where(x => x.HeeftSaldo)
            .Select(x => (Rekening: x, Hier: Onder(r, r.Balans.Plaatsing.GetValueOrDefault(x.Id), sleutel), HierVorig: Onder(r, r.Balans.PlaatsingVorig.GetValueOrDefault(x.Id), sleutel)))
            .Where(x => x.Hier || x.HierVorig)
            .OrderBy(x => x.Rekening.Nummer, StringComparer.Ordinal)
            .Select(x => new RekeningInPost(x.Rekening.Id, x.Rekening.Nummer, x.Rekening.Omschrijving,
                x.Hier ? teken * (x.Rekening.Saldo ?? 0) : null, x.HierVorig ? teken * (x.Rekening.SaldoVorig ?? 0) : null,
                x.Rekening.Koppeling is { } k ? $"{k.Code} {r.Naam(k.Sleutel)}" : "niet gekoppeld"))
            .ToList();
        return new PostRekeningen(sleutel, r.Naam(sleutel), rekeningen);
    }

    /// <summary>
    /// Waar een rekening heen gaat als je hem op deze regel neerzet: een subpost of grootboekcode (niveau 3 of 4),
    /// een eigen post, of bij een rekening dezelfde koppeling als die rekening. Null = hier kan niets heen (bijv. een
    /// hoofdpost of een totaal).
    /// </summary>
    public static string? Doel(RubriceerOverzicht r, Bron? bron)
    {
        ArgumentNullException.ThrowIfNull(r);
        return bron switch
        {
            { Soort: Onderwerp.Post } b when r.Schema?.Zoek(b.Sleutel) is { Niveau: 3 or 4, Inactief: false } => b.Sleutel,
            { Soort: Onderwerp.EigenPost } b => b.Sleutel,
            { Soort: Onderwerp.Rekening or Onderwerp.Uitsplitsing, Id: { } id } => r.Rekeningen.FirstOrDefault(x => x.Id == id)?.Koppeling?.Sleutel,
            _ => null,
        };
    }

    /// <summary>De plekken in het rapport waar een rekening heen kan, met de naam zoals in het rapport (voor kiezen zonder slepen).</summary>
    public static IReadOnlyList<(string Doel, string Naam)> Doelen(RubriceerOverzicht r, Rapport rapport)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(rapport);
        return [.. rapport.Hoofdstukken.SelectMany(h => h.Blokken).OfType<TabelBlok>().SelectMany(t => t.Tabel.Regels)
            .Where(x => x.Bron?.Soort is Onderwerp.Post or Onderwerp.EigenPost)
            .Select(x => (Doel: Doel(r, x.Bron), x.Omschrijving))
            .Where(x => x.Doel is not null)
            .DistinctBy(x => x.Doel)
            .Select(x => (x.Doel!, x.Omschrijving))
            .OrderBy(x => x.Omschrijving, StringComparer.CurrentCulture)];
    }

    /// <summary>De subpost (of eigen post) waar een rekening staat; een grootboekcode (niveau 4) wordt zijn subpost.</summary>
    private static string? PostVanRekening(RubriceerOverzicht r, Guid id)
    {
        var sleutel = r.Balans!.Plaatsing.GetValueOrDefault(id) ?? r.Balans.PlaatsingVorig.GetValueOrDefault(id);
        return sleutel is not null && r.Schema!.Zoek(sleutel) is { Niveau: 4, Ouder: { } ouder } ? ouder : sleutel;
    }

    /// <summary>Of een plaatsing (RGS-code of eigen post) gelijk is aan de post of eronder valt.</summary>
    private static bool Onder(RubriceerOverzicht r, string? plaatsing, string post)
    {
        if (plaatsing is null)
        {
            return false;
        }

        if (plaatsing == post)
        {
            return true;
        }

        var code = plaatsing.StartsWith(PostBalans.EigenVoorvoegsel, StringComparison.Ordinal)
            ? r.EigenPosten.FirstOrDefault(e => PostBalans.EigenSleutel(e.Id) == plaatsing)?.OuderCode
            : plaatsing;
        var schema = r.Schema!;
        for (var c = code is null ? null : schema.Zoek(code); c is not null; c = c.Ouder is { } o ? schema.Zoek(o) : null)
        {
            if (c.Code == post)
            {
                return true;
            }
        }

        return false;
    }
}
