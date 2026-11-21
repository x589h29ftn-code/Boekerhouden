using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// Wat in een administratie altijd moet kloppen, wat er ook gebeurd is: elke proefbalans in evenwicht, debiteuren en
/// crediteuren gelijk aan de openstaande posten, de bank in het grootboek gelijk aan het afschrift, de keten intact, de
/// btw sluit aan en de activastaat sluit op het grootboek. Geeft de afwijkingen (leeg = alles klopt).
/// </summary>
internal static class Invarianten
{
    public static async Task<List<string>> Controleer(AdministratieFixture app, Uitvoerder wie, Guid id, string wanneer)
    {
        var fouten = new List<string>();
        var grootboek = app.Dienst<GrootboekDienst>();
        var jaren = await grootboek.Boekjaren(wie, id);
        if (jaren.Count == 0)
        {
            return fouten;
        }

        var schema = await grootboek.Schema(wie, id);
        foreach (var jaar in jaren)
        {
            var proef = await grootboek.Proefbalans(wie, id, jaar);
            if (proef.Regels.Sum(r => r.Saldo) is var som && som != 0)
            {
                fouten.Add($"{wanneer}: proefbalans {jaar} niet in evenwicht ({som}).");
            }

            // Debiteuren en crediteuren per jaareinde gelijk aan de openstaande posten op die dag.
            var peil = new DateOnly(jaar, 12, 31);
            decimal Saldo(Rekeningfunctie functie) => schema.MetFunctie(functie).Sum(r => proef.Regels.FirstOrDefault(x => x.Rekening.Nummer == r.Nummer)?.Saldo ?? 0m);
            var openposten = app.Dienst<OpenpostenDienst>();
            var debiteuren = (await openposten.Debiteuren(wie, id, peil)).Relaties.SelectMany(r => r.Posten).Sum(p => p.Openstaand);
            var crediteuren = (await openposten.Crediteuren(wie, id, peil)).Relaties.SelectMany(r => r.Posten).Sum(p => p.Openstaand);
            if (Saldo(Rekeningfunctie.Debiteuren) != debiteuren)
            {
                fouten.Add($"{wanneer}: debiteuren {jaar} grootboek {Saldo(Rekeningfunctie.Debiteuren)}, openstaande posten {debiteuren}.");
            }

            if (-Saldo(Rekeningfunctie.Crediteuren) != crediteuren)
            {
                fouten.Add($"{wanneer}: crediteuren {jaar} grootboek {-Saldo(Rekeningfunctie.Crediteuren)}, openstaande posten {crediteuren}.");
            }

            foreach (var a in (await app.Dienst<BtwaangifteDienst>().Jaar(wie, id, jaar)).Aansluitingen.Where(a => a.Verschil != 0))
            {
                fouten.Add($"{wanneer}: btw-aansluiting {a.Van:dd-MM-yyyy} t/m {a.Tot:dd-MM-yyyy} verschil {a.Verschil}.");
            }

            foreach (var groep in (await app.Dienst<ActivaDienst>().Activastaat(wie, id, jaar)).Where(g => !g.Sluit))
            {
                fouten.Add($"{wanneer}: activastaat {jaar} {groep.Activarekening} sluit niet (aanschaf {groep.GrootboekAanschaf}/{groep.Totaal.AanschafEind}, afschrijving {groep.GrootboekAfschrijving}/{groep.Totaal.AfschrijvingEind}).");
            }
        }

        foreach (var b in (await app.Dienst<BankDienst>().Stand(wie, id)).Where(b => b.Saldo is not null && b.TeVerwerken == 0 && b.Grootboek != b.Saldo))
        {
            fouten.Add($"{wanneer}: bank {b.Dagboek} grootboek {b.Grootboek}, afschrift {b.Saldo}.");
        }

        if (await grootboek.ControleerKeten(wie, id) is { Count: > 0 } keten)
        {
            fouten.Add($"{wanneer}: keten gebroken bij {string.Join(", ", keten)}.");
        }

        return fouten;
    }
}
