using System.Globalization;
using System.Net;
using System.Text;
using Xunit.Abstractions;

namespace Boekerhouden.Administratie.Tests.Praktijk;

/// <summary>Draait alleen met BOEKERHOUDEN_TESTSET=&lt;map&gt;: schrijft de bestanden en de stappenlijst daarheen.</summary>
internal sealed class TestsetFactAttribute : FactAttribute
{
    public TestsetFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Map))
        {
            Skip = "Zet BOEKERHOUDEN_TESTSET op een map om de testset en de stappenlijst te schrijven.";
        }
    }

    public static string? Map => Environment.GetEnvironmentVariable("BOEKERHOUDEN_TESTSET");
}

/// <summary>De testset voor de handmatige procedure in de app: de bestanden in mappen en de stappenlijst (HTML).</summary>
public sealed class PraktijkprocedureTests(ITestOutputHelper uitvoer)
{
    [TestsetFact]
    public void Schrijf_de_testset_en_de_stappenlijst()
    {
        var map = TestsetFactAttribute.Map!;
        var set = Praktijkset.Maak();
        foreach (var bestand in set.Bestanden())
        {
            var doel = Path.Combine(map, bestand.Map);
            Directory.CreateDirectory(doel);
            File.WriteAllBytes(Path.Combine(doel, bestand.Naam), bestand.Inhoud);
        }

        File.WriteAllText(Path.Combine(map, "Testprocedure.html"), Praktijkprocedure.Html(set), Encoding.UTF8);
        uitvoer.WriteLine($"Geschreven naar {map}.");
    }
}

/// <summary>
/// De stappenlijst bij <see cref="Praktijkset"/>: inrichten, inkoop, verkoop en bank in de volgorde van een kwartaal, met
/// per stap wat het pakket hoort te doen en aan het eind de bedragen die moeten kloppen.
/// </summary>
internal static class Praktijkprocedure
{
    private static readonly CultureInfo Nl = Praktijkset.Nl;

    public static string Html(Praktijkset set)
    {
        var h = new StringBuilder();
        void L(string regel) => h.AppendLine(regel);
        string E(string tekst) => WebUtility.HtmlEncode(tekst);
        string B(decimal bedrag) => bedrag.ToString("N2", Nl);
        string D(DateOnly datum) => datum.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        void Tabel(string[] koppen, IEnumerable<string[]> rijen, int[]? rechts = null)
        {
            L("<table><thead><tr>" + string.Concat(koppen.Select((k, i) => $"<th{(rechts?.Contains(i) == true ? " class=\"r\"" : "")}>{E(k)}</th>")) + "</tr></thead><tbody>");
            foreach (var rij in rijen)
            {
                L("<tr>" + string.Concat(rij.Select((c, i) => $"<td{(rechts?.Contains(i) == true ? " class=\"r\"" : "")}>{c}</td>")) + "</tr>");
            }

            L("</tbody></table>");
        }

        var eigen = Praktijkset.Eigen;
        L("<!doctype html><html lang=\"nl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        L("<title>Testprocedure De Spaak</title><style>");
        L(":root{--tekst:#1d2433;--zacht:#5b6475;--lijn:#d9dde5;--achter:#ffffff;--vlak:#f4f6f9;--accent:#24456b}");
        L("@media (prefers-color-scheme: dark){:root{--tekst:#e6e9ef;--zacht:#a3abba;--lijn:#3a4252;--achter:#151a23;--vlak:#1e2531;--accent:#9db8dc}}");
        L("body{font:14px/1.5 'Segoe UI',system-ui,sans-serif;color:var(--tekst);background:var(--achter);max-width:1100px;margin:0 auto;padding:24px 16px}");
        L("h1{font-size:22px;color:var(--accent)}h2{font-size:17px;margin-top:32px;border-bottom:1px solid var(--lijn);padding-bottom:4px}h3{font-size:15px;margin-top:20px}");
        L("table{border-collapse:collapse;width:100%;margin:8px 0 16px;font-size:13px}th,td{border-bottom:1px solid var(--lijn);padding:4px 8px;text-align:left;vertical-align:top}");
        L("th{background:var(--vlak);font-weight:600}td.r,th.r{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}");
        L("td:first-child{white-space:nowrap}ol li,ul li{margin:4px 0}.zacht{color:var(--zacht)}code{background:var(--vlak);padding:0 4px;border-radius:3px}");
        L("input[type=checkbox]{margin-right:6px}.blok{overflow-x:auto}");
        L("</style></head><body>");
        L($"<h1>Testprocedure {E(eigen.Naam)} – eerste kwartaal 2026</h1>");
        L($"<p>Een verzonnen fietsenhandel: 100 inkoopfacturen ({set.Inkoop.Count(f => f.Formaat == Formaat.Pdf)} PDF, {set.Inkoop.Count(f => f.Formaat == Formaat.Ubl)} e-factuur), " +
          $"100 verkoopfacturen uit een kassapakket ({set.Verkoop.Count(f => f.Formaat == Formaat.Pdf)} PDF, {set.Verkoop.Count(f => f.Formaat == Formaat.Ubl)} e-factuur) en de bank van januari tot en met april: " +
          "een betaalrekening (CAMT.053) en een spaarrekening (MT940). Werk de stappen in volgorde af; bij elke stap staat wat het pakket hoort te doen. " +
          "Alle namen, nummers en bedragen zijn verzonnen.</p>");
        L("<p class=\"zacht\">De mappen <code>inkoop</code>, <code>verkoop</code> en <code>bank</code> staan naast dit bestand. Dezelfde set draait ook als automatische test; de uitkomsten hieronder komen daaruit.</p>");

        // ---- 1. inrichten
        L("<h2>1. Inrichten</h2><ol>");
        L($"<li><input type=checkbox>Nieuwe administratie <b>{E(eigen.Naam)}</b>, rechtsvorm BV, rekeningschema Sifer.</li>");
        L($"<li><input type=checkbox>Instellingen: btw-nummer <code>{eigen.BtwNummer}</code>, KvK <code>{eigen.Kvk}</code>, adres {E(eigen.Straat)}, {eigen.Postcode} {E(eigen.Plaats)}, " +
          $"IBAN's <code>{Iban(eigen.Iban)}</code> en <code>{Iban(Praktijkset.SpaarIban)}</code>.</li>");
        L("<li><input type=checkbox>Instellingen, verwerken: kleine betaalverschillen tot € 0,05 automatisch afboeken op <code>9000</code>.</li>");
        L($"<li><input type=checkbox>Bank: de bankrekening <b>BNK</b> aanpassen (tandwiel): naam <i>Betaalrekening</i>, IBAN <code>{Iban(eigen.Iban)}</code>, rekening <code>1100</code>. " +
          $"Een bankrekening toevoegen: code <b>SPA</b>, naam <i>Spaarrekening</i>, IBAN <code>{Iban(Praktijkset.SpaarIban)}</code>, rekening <code>1110</code>.</li>");
        L($"<li><input type=checkbox>Grootboek: rekening <code>{Praktijkset.Kruisposten}</code> <i>Kruisposten</i> toevoegen (balans). Beide kanten van een overboeking tussen de eigen rekeningen gaan hierop; aan het eind is het saldo nul.</li>");
        L("<li><input type=checkbox>Memoriaal: de beginbalans en de drie loonjournaalposten:</li></ol>");
        foreach (var post in set.Memoriaal)
        {
            L($"<h3>{E(post.Omschrijving)} – {D(post.Datum)}</h3>");
            Tabel(["Rekening", "Omschrijving", "Debet", "Credit"],
                post.Regels.Select(r => new[] { r.Rekeningnummer, E(r.Omschrijving ?? ""), r.Bedrag > 0 ? B(r.Bedrag) : "", r.Bedrag < 0 ? B(-r.Bedrag) : "" }), [2, 3]);
        }

        // ---- 2. inkoop
        L("<h2>2. Inkoop</h2>");
        L("<p>Lees de map <code>inkoop</code> maand voor maand in (eerst de bestanden van januari, dan februari, dan maart: de naam begint met de datum). " +
          "De eerste factuur van een leverancier boek je zelf; de tweede stelt het pakket voor; vanaf de derde boekt het pakket zelf, zolang het bedrag in het patroon past. " +
          "Een creditnota boekt het pakket nooit vanzelf.</p>");
        Tabel(["Leverancier", "Facturen", "Rekening", "Btw-code", "Bijzonder"],
            set.Inkoop.GroupBy(f => f.Partij.Naam).Select(g => new[]
            {
                E(g.Key), $"{g.Count()} ({g.First().Formaat.ToString().ToUpperInvariant()})", string.Join(", ", g.SelectMany(f => f.Regels).Select(r => r.Rekening).Distinct()),
                string.Join(", ", g.SelectMany(f => f.Regels).Select(r => r.Onzecode).Distinct()), E(Bijzonder(g.ToList())),
            }), [1]);
        var dubbel = set.Bestanden().Single(b => b.Naam.Contains("Doorgestuurd", StringComparison.Ordinal));
        L($"<ul><li><input type=checkbox><code>{E(dubbel.Naam)}</code> is dezelfde factuur nog eens: het pakket meldt hem als dubbel en leest hem niet in.</li>");
        L($"<li><input type=checkbox>Inkoopboek over het kwartaal: {set.Inkoop.Count} facturen, totaal inclusief btw <b>€ {B(set.Inkoop.Sum(f => f.Getekend))}</b> (creditnota's negatief).</li>");
        L("<li><input type=checkbox>In de automatische test boekte het pakket 67 van de 100 vanzelf; de rest (eerste en tweede factuur per leverancier, een hoger bedrag dan gewend en de creditnota's) met de hand.</li></ul>");

        // ---- 3. verkoop
        L("<h2>3. Verkoop</h2>");
        L("<p>Lees de map <code>verkoop</code> in bij Verkoop (facturen uit een ander pakket). Bij de eerste factuur van een klant is de klant nog onbekend: maak de klant aan vanaf het concept " +
          "(omzetrekening en btw-code zoals hieronder) en boek. Daarna herkent het pakket de klant en boekt het zelf.</p>");
        Tabel(["Klant", "Land", "Facturen", "Omzet", "Btw-code", "Bijzonder"],
            set.Verkoop.GroupBy(f => f.Partij.Naam).Select(g => new[]
            {
                E(g.Key), g.First().Partij.Land, g.Count().ToString(CultureInfo.InvariantCulture), string.Join(", ", g.SelectMany(f => f.Regels).Select(r => r.Rekening).Distinct()),
                string.Join(", ", g.SelectMany(f => f.Regels).Select(r => r.Onzecode).Distinct()), E(Bijzonder(g.ToList())),
            }), [2]);
        L($"<ul><li><input type=checkbox>Verkoopboek over het kwartaal: {set.Verkoop.Count} facturen, totaal inclusief btw <b>€ {B(set.Verkoop.Sum(f => f.Getekend))}</b>.</li>");
        L("<li><input type=checkbox>De facturen aan Velohaus Bergtal AG (Zwitserland) staan op <code>VEX</code> (uitvoer, rubriek 3a), die aan Radhaus Müller GmbH op <code>VEU</code> (rubriek 3b).</li>");
        L("<li><input type=checkbox>In de automatische test boekte het pakket 80 van de 100 vanzelf; de rest was de eerste factuur van een nieuwe klant.</li></ul>");

        // ---- 4. bank
        L("<h2>4. Bank</h2>");
        L("<p>Lees de bestanden in de map <code>bank</code> in volgorde in (01 tot en met 06) en verwerk na elk bestand wat blijft staan, vóór het volgende: zo leert het pakket van de maand ervoor. " +
          "Bestand 05 overlapt met 04: het pakket slaat de mutaties die er al zijn over en leest alleen de nieuwe in.</p>");
        Tabel(["Bestand", "Wat het pakket hoort te doen"],
        [
            ["01 januari", "38 mutaties; de klanten en leveranciers met een kenmerk of factuurnummer, de incasso's en de Belastingdienst gaan vanzelf. Met de hand: pin, bankkosten, lonen en de korting bij Reclamebureau Vonk."],
            ["02 februari", "77 mutaties (de verzamelbetaling van de groothandel en de vervoerder wordt in losse betalingen gesplitst). Met de hand o.a.: de storno van Energie Oost, de sombetaling van Hotel De Klant, de partner van J. de Vries, twee dubbele betalingen en de overboeking naar de spaarrekening."],
            ["03 maart", "73 mutaties; bijna alles vanzelf (pin, lonen en bankkosten zijn geleerd). Met de hand: de korting van Fietsverhuur Strand, de onbekende ontvangst en de overboeking van de spaarrekening."],
            ["04 april 1–15", "22 mutaties, alles vanzelf (ook de terugbetalingen en de tweede incasso van Energie Oost)."],
            ["05 april (overlapt)", "Overlapt met 04: 22 al ingelezen, 1 nieuw (de btw van het eerste kwartaal), vanzelf geboekt."],
            ["06 spaarrekening", "3 mutaties met de hand: twee overboekingen (kruisposten) en de rente."],
        ]);
        L("<h3>Wat er met de hand moet (als het pakket het niet zelf doet)</h3>");
        var bijzonder = set.Betaal.SelectMany(p => p.Batch ?? [p]).Select(p => (Rekening: "Betaal", Post: p))
            .Concat(set.Spaar.Select(p => (Rekening: "Spaar", Post: p)))
            .Where(x => !x.Post.Afhandeling.Toelichting.StartsWith("Afletteren op", StringComparison.Ordinal))
            .OrderBy(x => x.Post.Datum).ToList();
        L("<div class=\"blok\">");
        Tabel(["Datum", "Rekening", "Bedrag", "Tegenpartij", "Omschrijving", "Wat te doen"],
            bijzonder.Select(x => new[] { D(x.Post.Datum), x.Rekening, B(x.Post.Bedrag), E(x.Post.Naam ?? ""), E(x.Post.Omschrijving), E(x.Post.Afhandeling.Toelichting) }), [2]);
        L("</div><p class=\"zacht\">Pin, bankkosten en lonen hoeven na de eerste keer niet meer: het pakket onthoudt de keuze. Alle andere mutaties horen vanzelf afgeletterd te worden op het factuurnummer of kenmerk in de omschrijving.</p>");

        // ---- 5. eindcontrole
        L("<h2>5. Eindcontrole (30 april 2026)</h2>");
        var eindBetaal = Praktijkset.SaldoBetaal(set.Betaal, Praktijkset.Eind);
        var eindSpaar = Praktijkset.BeginSpaar + set.Spaar.Sum(p => p.Bedrag);
        Tabel(["Wat", "Moet zijn"],
        [
            ["Bank: niets meer te verwerken", "0 mutaties"],
            ["Betaalrekening (1100) = afschrift", $"€ {B(eindBetaal)}, beginstand sluit (verschil 0)"],
            ["Spaarrekening (1110) = afschrift", $"€ {B(eindSpaar)}"],
            ["Debiteuren (1300)", $"€ {B(set.OpenDebiteuren.Values.Sum())} ({set.OpenDebiteuren.Count} facturen, zie hieronder)"],
            ["Crediteuren (1600)", $"€ {B(set.OpenCrediteuren.Values.Sum())} credit ({set.OpenCrediteuren.Count} facturen, zie hieronder)"],
            ["Vraagposten (2000)", $"€ {B(set.Vraagpost)} credit (de onbekende ontvangst)"],
            [$"Kruisposten ({Praktijkset.Kruisposten})", "€ 0,00"],
            ["Loonheffing (1520) en nettolonen (1800)", "€ 0,00"],
            ["Betaalverschil op 9000 (naast de bankkosten)", "€ 0,02 (Bouwbedrijf Steen betaalde twee cent te weinig)"],
            ["Btw (1500 + 1510) na de betaling van het eerste kwartaal", $"€ {B(Math.Abs(set.AangifteQ1.Afronding))} {(set.AangifteQ1.Afronding > 0 ? "credit" : "debet")} (het afronden in de aangifte)"],
            ["Saldo-aansluitingen bank, debiteuren en crediteuren", "sluiten"],
            ["Auditfile 2026 maken en als nieuwe administratie inlezen", "dezelfde saldi per rekening"],
        ]);
        L("<h3>Openstaande debiteuren</h3>");
        Tabel(["Factuur", "Klant", "Openstaand"], set.OpenDebiteuren.Select(x => new[] { x.Key, E(set.Verkoop.Single(f => f.Nummer == x.Key).Partij.Naam), B(x.Value) }), [2]);
        L("<h3>Openstaande crediteuren</h3>");
        Tabel(["Factuur", "Leverancier", "Openstaand"], set.OpenCrediteuren.Select(x => new[] { E(x.Key), E(set.Inkoop.Single(f => f.Nummer == x.Key).Partij.Naam), B(x.Value) }), [2]);
        L("<h3>Btw-aangifte eerste kwartaal 2026</h3>");
        var a = set.AangifteQ1;
        Tabel(["Rubriek", "Omzet / grondslag", "Btw"],
            a.Rubrieken.Select(r => new[] { r.Rubriek, B(r.Grondslag), B(r.Btw) })
                .Append(["5b voorbelasting", "", B(a.Voorbelasting)])
                .Append(["Te betalen", "", B(a.TeBetalen)])
                .Append(["Te betalen (afgerond, betaald op 28-04-2026)", "", B(a.Afgerond().TeBetalen)]), [1, 2]);
        L("</body></html>");
        return h.ToString();
    }

    private static string Iban(string iban) => Boekhouding.Relaties.Iban.Toon(iban);

    /// <summary>Wat er bijzonder is aan de facturen van één relatie, in een paar woorden.</summary>
    private static string Bijzonder(IReadOnlyList<Factuurdata> facturen)
    {
        var delen = new List<string>();
        var codes = facturen.SelectMany(f => f.Regels).Select(r => r.Onzecode).Distinct().ToList();
        if (codes.Contains("IVL") || codes.Contains("VVL"))
        {
            delen.Add("btw verlegd (rubriek 2a)");
        }

        if (codes.Contains("IEU"))
        {
            delen.Add("dienst uit de EU (rubriek 4b)");
        }

        if (codes.Contains("I0"))
        {
            delen.Add("vrijgesteld");
        }

        if (codes.Contains("I9") && codes.Contains("I21"))
        {
            delen.Add("9% en 21% op één factuur");
        }

        if (facturen.Any(f => f.Credit))
        {
            delen.Add($"creditnota {string.Join(", ", facturen.Where(f => f.Credit).Select(f => f.Nummer))}");
        }

        foreach (var (wijze, tekst) in new (Betaalwijze, string)[]
                 {
                     (Betaalwijze.Verzamel, "betaald met een verzamelbetaling"), (Betaalwijze.Incasso, "incasso"), (Betaalwijze.IncassoStorno, "een incasso gestorneerd en opnieuw geïncasseerd"),
                     (Betaalwijze.Kenmerk, "betaald met kenmerk"), (Betaalwijze.MinCredit, "betaald min de creditnota"), (Betaalwijze.Dubbel, "één factuur dubbel betaald en terugbetaald"),
                     (Betaalwijze.Korting, "betaald met 2% korting"), (Betaalwijze.Som, "betaald in één bedrag voor meer facturen"), (Betaalwijze.Deel, "betaald in twee delen"),
                     (Betaalwijze.Verschil, "twee cent te weinig betaald"), (Betaalwijze.AnderIban, "één keer van een ander IBAN"), (Betaalwijze.Open, "staat aan het eind nog open"),
                 })
        {
            if (facturen.Any(f => f.Betaling == wijze))
            {
                delen.Add(tekst);
            }
        }

        if (facturen.Any(f => f.Partij.Land is "CH"))
        {
            delen.Add("uitvoer buiten de EU (rubriek 3a)");
        }

        if (facturen.Any(f => f.Partij.Land is "DE"))
        {
            delen.Add("levering binnen de EU (rubriek 3b)");
        }

        return string.Join("; ", delen.Distinct());
    }
}
