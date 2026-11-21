using Boekerhouden.Administratie.Diensten;
using Boekerhouden.Boekhouding.Auditfiles;
using Boekerhouden.Boekhouding.Grootboek;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>
/// De interne samenhang van de app: hetzelfde getal moet langs elke weg hetzelfde zijn. Per boekjaar: de proefbalans,
/// de kolommenbalans (over het jaar en over twee halve jaren), het overzicht per maand, de grootboekkaarten, het
/// journaal, balans en W&amp;V, het verkoop- en inkoopboek, de auditfile, de fiscale positie; en vandaag het dashboard
/// tegen de openstaande posten en het grootboek. Geeft de afwijkingen (leeg = alles hangt samen).
/// </summary>
internal static class Samenhang
{
    public static async Task<List<string>> Controleer(AdministratieFixture app, Uitvoerder wie, Guid id, string wanneer)
    {
        var fouten = new List<string>();
        var grootboek = app.Dienst<GrootboekDienst>();
        var schema = await grootboek.Schema(wie, id);
        var dagboeken = await grootboek.Dagboeken(wie, id);
        void Vergelijk(string wat, decimal a, decimal b)
        {
            if (a != b)
            {
                fouten.Add($"{wanneer}: {wat}: {a} tegen {b}.");
            }
        }

        foreach (var jaar in await grootboek.Boekjaren(wie, id))
        {
            var (van, tot) = (new DateOnly(jaar, 1, 1), new DateOnly(jaar, 12, 31));
            var kb = await grootboek.Kolommenbalans(wie, id, van, tot);
            var saldo = kb.Regels.ToDictionary(r => r.Rekening.Nummer, r => r.Saldo);
            decimal Saldo(string rekening) => saldo.GetValueOrDefault(rekening);
            var vorig = (await grootboek.Kolommenbalans(wie, id, van.AddYears(-1), tot.AddYears(-1))).Regels.ToDictionary(r => r.Rekening.Nummer, r => r.Saldo);

            // Kolommenbalans per boekjaar en per periode, proefbalans.
            var perJaar = await grootboek.Kolommenbalans(wie, id, jaar);
            var proef = await grootboek.Proefbalans(wie, id, jaar);
            foreach (var r in schema.Alle)
            {
                Vergelijk($"{jaar} rekening {r.Nummer} kolommenbalans per jaar en per periode", perJaar.Regels.FirstOrDefault(x => x.Rekening.Nummer == r.Nummer)?.Saldo ?? 0m, Saldo(r.Nummer));
                Vergelijk($"{jaar} rekening {r.Nummer} proefbalans en kolommenbalans", proef.Regels.FirstOrDefault(x => x.Rekening.Nummer == r.Nummer)?.Saldo ?? 0m, Saldo(r.Nummer));
            }

            Vergelijk($"{jaar} kolommenbalans in evenwicht (balans en W&V)", kb.BalansDebet - kb.BalansCredit, kb.Resultaat);

            // Twee halve jaren samen zijn het jaar: resultaatrekeningen tellen op, balansrekeningen staan aan het eind gelijk.
            var eerste = await grootboek.Kolommenbalans(wie, id, van, new DateOnly(jaar, 6, 30));
            var tweede = await grootboek.Kolommenbalans(wie, id, new DateOnly(jaar, 7, 1), tot);
            Vergelijk($"{jaar} resultaat eerste + tweede halfjaar", eerste.Resultaat + tweede.Resultaat, kb.Resultaat);
            foreach (var r in schema.Alle.Where(r => r.IsBalans && r.Functie != Rekeningfunctie.OnverdeeldResultaat))
            {
                Vergelijk($"{jaar} rekening {r.Nummer} stand na het tweede halfjaar", tweede.Regels.FirstOrDefault(x => x.Rekening.Nummer == r.Nummer)?.Saldo ?? 0m, Saldo(r.Nummer));
            }

            // Per maand: de eindstand is de kolommenbalans, het resultaat per maand telt op tot het jaar.
            var maanden = await grootboek.Maandoverzicht(wie, id, van, tot);
            foreach (var r in maanden.Regels)
            {
                Vergelijk($"{jaar} rekening {r.Rekening.Nummer} eindstand per maand", r.Eind, Saldo(r.Rekening.Nummer));
            }

            Vergelijk($"{jaar} resultaat per maand opgeteld", maanden.ResultaatPerMaand.Sum(), kb.Resultaat);

            // Grootboekkaarten: begin is de stand van vorig jaar (balans), eind de kolommenbalans.
            var kaarten = await grootboek.Kaarten(wie, id, van, tot);
            foreach (var k in kaarten)
            {
                Vergelijk($"{jaar} grootboekkaart {k.Rekening.Nummer} eindsaldo", k.Eindsaldo, Saldo(k.Rekening.Nummer));
                if (k.Rekening.IsBalans && k.Rekening.Functie != Rekeningfunctie.OnverdeeldResultaat)
                {
                    Vergelijk($"{jaar} grootboekkaart {k.Rekening.Nummer} beginsaldo", k.Beginsaldo, vorig.GetValueOrDefault(k.Rekening.Nummer));
                }
            }

            // Het journaal: elke boeking in evenwicht, en per rekening gelijk aan de mutaties op de kaart.
            var posten = await grootboek.Posten(wie, id, null, van, tot);
            foreach (var p in posten.Where(p => p.Post.Regels.Sum(r => r.Bedrag) != 0))
            {
                fouten.Add($"{wanneer}: boeking {p.Post.Dagboek} {p.Post.Boekstuk} niet in evenwicht.");
            }

            var perRekening = posten.SelectMany(p => p.Post.Regels).GroupBy(r => r.Rekeningnummer).ToDictionary(g => g.Key, g => g.Sum(r => r.Bedrag));
            foreach (var k in kaarten)
            {
                Vergelijk($"{jaar} rekening {k.Rekening.Nummer} journaal en grootboekkaart", perRekening.GetValueOrDefault(k.Rekening.Nummer), k.Mutaties.Sum(m => m.Bedrag));
            }

            // Balans en W&V.
            var overzicht = await grootboek.Jaaroverzicht(wie, id, jaar);
            Vergelijk($"{jaar} balans: activa en passiva", overzicht.TotaalActiva, overzicht.TotaalPassiva);
            Vergelijk($"{jaar} resultaat balans/W&V en kolommenbalans", overzicht.Resultaat, kb.Resultaat);

            // Verkoop- en inkoopboek tegen de boekingen in die dagboeken.
            var debiteuren = schema.MetFunctie(Rekeningfunctie.Debiteuren).Select(r => r.Nummer).ToHashSet(StringComparer.Ordinal);
            var crediteuren = schema.MetFunctie(Rekeningfunctie.Crediteuren).Select(r => r.Nummer).ToHashSet(StringComparer.Ordinal);
            var btw = schema.MetFunctie(Rekeningfunctie.Voorbelasting).Concat(schema.MetFunctie(Rekeningfunctie.AfTeDragenBtw)).Select(r => r.Nummer).ToHashSet(StringComparer.Ordinal);
            void Boek(string naam, Dagboeksoort soort, IReadOnlyList<Factuurboekregel> alles, HashSet<string> tegen, int teken)
            {
                var codes = dagboeken.Where(d => d.Soort == soort).Select(d => d.Code).ToHashSet(StringComparer.Ordinal);

                // Een factuur die in het memoriaal is geboekt (bijv. van vorig jaar) staat ook in het boek; vergelijk de
                // facturen uit de verkoop- of inkoopdagboeken met die dagboeken.
                var boek = alles.Where(b => codes.Contains(b.Dagboek)).ToList();
                var regels = posten.Where(p => codes.Contains(p.Post.Dagboek)).SelectMany(p => p.Post.Regels).ToList();
                // Een teruggedraaide factuur blijft met zijn tegenboeking in het dagboek staan, maar niet in het boek.
                var inDagboek = posten.Where(p => codes.Contains(p.Post.Dagboek)).ToList();
                var teruggedraaid = inDagboek.Count(p => p.Post.Omschrijving.StartsWith("Terugdraaien ", StringComparison.Ordinal));
                Vergelijk($"{jaar} {naam}: aantal facturen en boekingen", boek.Count, inDagboek.Count - (2 * teruggedraaid));
                Vergelijk($"{jaar} {naam}: totaal", boek.Sum(b => b.Totaal), -teken * regels.Where(r => tegen.Contains(r.Rekeningnummer)).Sum(r => r.Bedrag));
                Vergelijk($"{jaar} {naam}: btw", boek.Sum(b => b.Btw), teken * regels.Where(r => btw.Contains(r.Rekeningnummer)).Sum(r => r.Bedrag));
                Vergelijk($"{jaar} {naam}: exclusief", boek.Sum(b => b.Exclusief), teken * regels.Where(r => !tegen.Contains(r.Rekeningnummer) && !btw.Contains(r.Rekeningnummer)).Sum(r => r.Bedrag));
            }

            Boek("verkoopboek", Dagboeksoort.Verkoop, await app.Dienst<VerkoopDienst>().Verkoopboek(wie, id, van, tot), debiteuren, -1);
            Boek("inkoopboek", Dagboeksoort.Inkoop, await app.Dienst<InkoopDienst>().Inkoopboek(wie, id, van, tot), crediteuren, 1);

            // De auditfile van het jaar: leesbaar, in evenwicht, en met dezelfde standen.
            var bestand = await app.Dienst<AuditfileDienst>().Maak(wie, id, jaar);
            var (xaf, leesfouten) = AuditfileLezer.Lees(new MemoryStream(bestand.Inhoud));
            fouten.AddRange(leesfouten.Concat(xaf.Controleer()).Select(f => $"{wanneer}: auditfile {jaar}: {f}"));
            Vergelijk($"{jaar} auditfile: aantal boekingen", xaf.AantalBoekingen, posten.Count);
            var uitXaf = xaf.Beginbalans.Concat(xaf.Dagboeken.SelectMany(d => d.Boekingen).SelectMany(b => b.Regels))
                .GroupBy(r => r.Rekening).ToDictionary(g => g.Key, g => g.Sum(r => r.Bedrag));
            foreach (var r in schema.Alle.Where(r => r.Functie != Rekeningfunctie.OnverdeeldResultaat))
            {
                Vergelijk($"{jaar} auditfile rekening {r.Nummer}", uitXaf.GetValueOrDefault(r.Nummer), Saldo(r.Nummer));
            }

            // De btw: de aangifte over het jaar is de som van de perioden, per rubriek.
            var btwjaar = (await app.Dienst<BtwaangifteDienst>().Jaar(wie, id, jaar)).Jaar;
            foreach (var rubriek in Boekhouding.Btw.Btwaangifte.Volgorde)
            {
                Vergelijk($"{jaar} btw {rubriek} grondslag: jaar en som van de perioden", btwjaar.Jaaraangifte.Rubriek(rubriek).Grondslag, btwjaar.Perioden.Sum(p => p.Berekend.Rubriek(rubriek).Grondslag));
                Vergelijk($"{jaar} btw {rubriek} btw: jaar en som van de perioden", btwjaar.Jaaraangifte.Rubriek(rubriek).Btw, btwjaar.Perioden.Sum(p => p.Berekend.Rubriek(rubriek).Btw));
            }

            Vergelijk($"{jaar} btw voorbelasting: jaar en som van de perioden", btwjaar.Jaaraangifte.Voorbelasting, btwjaar.Perioden.Sum(p => p.Berekend.Voorbelasting));

            // De fiscale positie begint bij het resultaat (zonder vennootschapsbelasting in de W&V).
            if (await app.Dienst<VpbDienst>().Positie(wie, id, jaar) is { } vpb && !schema.Alle.Any(r => !r.IsBalans && Boekhouding.Vpb.Fiscaleindeling.Van(r) == Boekhouding.Vpb.Fiscaalsoort.Last && Saldo(r.Nummer) != 0))
            {
                Vergelijk($"{jaar} fiscale positie: resultaat voor belastingen", vpb.Positie.Regels.First(r => r.Omschrijving == "Resultaat voor belastingen").Totaal?.Bedrag ?? 0m, kb.Resultaat);
            }
        }

        // De saldo-aansluitingen van debiteuren en crediteuren sluiten altijd (die van de vaste activa meldt terecht een
        // aanschaf die nog niet als actief is opgenomen: werk dat nog moet gebeuren, geen fout).
        foreach (var a in (await app.Dienst<AansluitingDienst>().Aansluitingen(wie, id))
                     .Where(a => a.Soort is Aansluitingsoort.Debiteuren or Aansluitingsoort.Crediteuren && !a.Sluit))
        {
            fouten.Add($"{wanneer}: aansluiting {a.Onderwerp} sluit niet (grootboek {a.Grootboek}, bron {a.Bron}).");
        }

        // Vandaag: het dashboard tegen het grootboek en de openstaande posten.
        var dashboarddienst = app.Dienst<DashboardDienst>();
        var dashboard = await dashboarddienst.Laad(wie, id, [new(Widget.Liquide), new(Widget.Liquiditeit), new(Widget.Debiteuren), new(Widget.Crediteuren), new(Widget.Kerncijfers)]);
        var vandaag = dashboard.Vandaag;
        var nu = (await grootboek.Kolommenbalans(wie, id, new DateOnly(vandaag.Year, 1, 1), vandaag)).Regels.ToDictionary(r => r.Rekening.Nummer, r => r.Saldo);
        foreach (var l in dashboard.Liquide ?? [])
        {
            Vergelijk($"dashboard liquide {l.Rekening}", l.Saldo, nu.GetValueOrDefault(l.Rekening));
        }

        // Bank en kas en de grafiek liquiditeit (de laatste maand loopt tot vandaag) tellen dezelfde rekeningen.
        Vergelijk("dashboard bank en kas tegen liquiditeit", (dashboard.Liquide ?? []).Sum(l => l.Saldo), dashboard.Liquiditeit?[^1].Saldo ?? 0m);

        var openposten = app.Dienst<OpenpostenDienst>();
        Vergelijk("dashboard debiteuren", dashboard.Debiteuren?.Totaal ?? 0m, (await openposten.Debiteuren(wie, id, vandaag)).Relaties.Sum(r => r.Openstaand));
        Vergelijk("dashboard crediteuren", dashboard.Crediteuren?.Totaal ?? 0m, (await openposten.Crediteuren(wie, id, vandaag)).Relaties.Sum(r => r.Openstaand));
        if (dashboard.Kerncijfers is { } kern)
        {
            foreach (var p in new[] { kern.Dit, kern.Vorig })
            {
                var resultaat = (await grootboek.Kolommenbalans(wie, id, p.Van, p.Tot)).Resultaat;
                Vergelijk($"dashboard kerncijfers resultaat {p.Van:dd-MM-yyyy} t/m {p.Tot:dd-MM-yyyy}", p.Resultaat, resultaat);
                Vergelijk($"dashboard kerncijfers omzet - kosten {p.Van:yyyy}", p.Omzet - p.Kosten, p.Resultaat);
            }
        }

        return fouten;
    }
}
