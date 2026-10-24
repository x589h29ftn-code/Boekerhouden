namespace Boekerhouden.Domain.Rapport;

/// <summary>Wat er in het rapport bij een hoofdpost staat, voor de uitwerking per post.</summary>
/// <param name="Titel">De titel van de specificatie (of van de toelichting als de specificatie uit staat); null = niet in het rapport.</param>
/// <param name="Regels">Het aantal regels in de specificatie (rekeningen, eigen posten, uitsplitsingen).</param>
/// <param name="Verlopen">De titels van de verloopoverzichten onder de post.</param>
/// <param name="Grondslagen">De titels van de opgenomen grondslagen die bij de post horen.</param>
public sealed record PostInRapport(string Code, string? Titel, int Regels, string? Toelichting, IReadOnlyList<string> Verlopen, IReadOnlyList<string> Grondslagen);

public static class RapportPerPost
{
    /// <summary>
    /// Zoekt in het rapport de specificatie, toelichting en verloopoverzichten van de hoofdpost (een verloop staat
    /// direct onder de specificatie van zijn post) en de opgenomen grondslagen met een code onder de post.
    /// </summary>
    public static PostInRapport Bij(Rapport rapport, string hoofdpost)
    {
        ArgumentNullException.ThrowIfNull(rapport);
        ArgumentNullException.ThrowIfNull(hoofdpost);
        string? titel = null;
        string? toelichting = null;
        var regels = 0;
        var verlopen = new List<string>();
        var grondslagen = new List<string>();
        string? vorige = null;
        foreach (var blok in rapport.Delen.SelectMany(d => d.Hoofdstukken).SelectMany(h => h.Blokken))
        {
            switch (blok)
            {
                case TabelBlok { Tabel: { Code: { } code } tabel }:
                    vorige = code;
                    if (code == hoofdpost)
                    {
                        titel = tabel.Titel;
                        regels = tabel.Regels.Count(r => r.Soort == RegelSoort.Detail);
                    }

                    break;
                case TekstBlok { Toelichting: { } code } tekst:
                    vorige = code;
                    if (code == hoofdpost)
                    {
                        titel ??= tekst.Titel;
                        toelichting = tekst.Tekst;
                    }

                    break;
                case VerloopBlok { Verloop: var verloop } when verloop.Sleutel == hoofdpost
                    || (vorige == hoofdpost && verloop.Sleutel.StartsWith(RapportOpbouw.RekeningVoorvoegsel, StringComparison.Ordinal)):
                    verlopen.Add(verloop.Titel);
                    break;
                case GrondslagBlok { Opgenomen: true } grondslag when grondslag.Codes.Any(c => c.StartsWith(hoofdpost, StringComparison.Ordinal)):
                    grondslagen.Add(grondslag.Titel);
                    break;
            }
        }

        return new PostInRapport(hoofdpost, titel, regels, toelichting, verlopen, grondslagen);
    }
}
