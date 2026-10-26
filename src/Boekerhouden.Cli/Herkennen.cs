using Boekerhouden.Boekhouding.Btw;
using Boekerhouden.Boekhouding.Facturen;
using Boekerhouden.Boekhouding.Grootboek;
using Boekerhouden.Boekhouding.Herkenning;
using Boekerhouden.Boekhouding.Regels;
using Boekerhouden.Boekhouding.Relaties;
using Boekerhouden.Documenten;
using Boekerhouden.Domain;

/// <summary>
/// <c>boekerhouden herken</c>: een inkoopfactuur herkennen en verwerken zoals het administratiepakket dat doet, zonder
/// database. Handig om de herkenning op echte facturen te proberen (die blijven buiten de repository).
/// </summary>
internal static class Herkennen
{
    public static async Task<int> Voer(string pad, string[] opties)
    {
        if (!File.Exists(pad))
        {
            Console.Error.WriteLine($"Bestand niet gevonden: {pad}");
            return 2;
        }

        var ibans = new List<string>();
        var btwNummers = new List<string>();
        var namen = new List<string>();
        var tekst = false;
        IVirusscanner? scanner = null;
        Keuze? leer = null;
        for (var i = 0; i < opties.Length; i++)
        {
            switch (opties[i])
            {
                case "--eigen-iban" when i + 1 < opties.Length:
                    ibans.Add(opties[++i]);
                    break;
                case "--eigen-btw" when i + 1 < opties.Length:
                    btwNummers.Add(opties[++i]);
                    break;
                case "--eigen-naam" when i + 1 < opties.Length:
                    namen.Add(opties[++i]);
                    break;
                case "--clamav" when i + 1 < opties.Length:
                    scanner = ClamAvScanner.Van(opties[++i]);
                    break;
                case "--tekst":
                    tekst = true;
                    break;
                case "--leer" when i + 2 < opties.Length:
                    leer = new Keuze(opties[i + 1], opties[i + 2]);
                    i += 2;
                    break;
                default:
                    Console.Error.WriteLine($"Onbekende optie: {opties[i]}");
                    return 2;
            }
        }

        var eigen = new EigenGegevens(ibans, btwNummers, namen.Count > 0 ? namen : null);
        if (tekst && Path.GetExtension(pad).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            await using var pdfStream = File.OpenRead(pad);
            var pdf = PdfTekst.Lees(pdfStream);
            Console.WriteLine($"── Tekst uit de PDF ({pdf.Paginas} pagina's, {pdf.Regels.Count} regels, {pdf.Bijlagen.Count} bijlagen) ──");
            foreach (var regel in pdf.Regels)
            {
                Console.WriteLine($"  │ {regel}");
            }

            Console.WriteLine();
        }

        await using var stream = File.OpenRead(pad);
        var resultaat = await Factuurherkenning.Standaard(eigen, scanner).HerkenAsync(stream, Path.GetFileName(pad));
        if (resultaat.Factuur is not { } herkend)
        {
            Console.WriteLine($"{(resultaat.Geweigerd ? "Geweigerd" : "Niet herkend")}: {resultaat.Melding}");
            return 1;
        }

        Toon(herkend);

        // Een lege administratie: de leverancier is nog onbekend, zoals bij de eerste factuur.
        var schema = Rekeningschema(leer?.Rekeningnummer);
        var codes = BtwCodes.Standaard;
        var relaties = new List<Relatie>();
        var geheugen = new Leergeheugen();
        var voorstel = Inkoopverwerker.Verwerk(herkend, relaties, [], geheugen, codes, []);
        Console.WriteLine();
        Console.WriteLine("── Inkoopvoorstel in een lege administratie ──");
        ToonVoorstel(voorstel, codes);

        var nieuw = new Relatie("LEV1", herkend.Naam ?? "Nieuwe leverancier", herkend.Iban is { } iban ? [iban] : [], herkend.BtwNummer, herkend.KvkNummer);
        Console.WriteLine($"  Nieuwe relatie voorgesteld: {nieuw.Naam}; IBAN {Of(nieuw.Ibans.Count > 0 ? nieuw.Ibans[0] : null)}; btw {Of(nieuw.BtwNummer)}; KvK {Of(nieuw.KvkNummer)}");
        if (leer is null)
        {
            return 0;
        }

        // Drie keer dezelfde leverancier: kiezen, voorstel, automatisch.
        relaties.Add(nieuw);
        var journaal = new Journaal(schema, [new Dagboek("INK", "Inkoopboek", Dagboeksoort.Inkoop)]);
        var gegevens = Inkoopverwerker.Gegevens(herkend, nieuw);
        for (var keer = 1; keer <= 3; keer++)
        {
            voorstel = Inkoopverwerker.Verwerk(herkend, relaties, [], geheugen, codes, []);
            Console.WriteLine();
            Console.WriteLine($"── {keer}e factuur van deze leverancier (leverancier herkend op {voorstel.Leverancier?.Op}) ──");
            ToonVoorstel(voorstel, codes);
            if (keer < 3)
            {
                Console.WriteLine($"  De gebruiker kiest rekening {leer.Rekeningnummer} met btw-code {leer.BtwCode}.");
                geheugen.Onthoud(gegevens, leer);
            }
        }

        if (voorstel.Factuur is { } factuur)
        {
            var (post, open) = Factuurboeker.Boek(factuur, journaal.Dagboeken[0], journaal.VolgendBoekstuk("INK", factuur.Datum.Year), schema, codes);
            journaal.Boek(post);
            Console.WriteLine();
            Console.WriteLine($"── Journaalpost {post.Dagboek} {post.Boekstuk} ({post.Datum:dd-MM-yyyy}) ──");
            foreach (var regel in post.Regels)
            {
                var rekening = schema.Zoek(regel.Rekeningnummer)!;
                Console.WriteLine($"  {rekening.Nummer,-6} {rekening.Naam,-28} {Debet(regel.Bedrag),12} {Credit(regel.Bedrag),12}  {regel.BtwCode}");
            }

            Console.WriteLine($"  Openstaande post: {open.Factuurnummer}, {Bedragen.Euro(-open.Openstaand)} te betalen vóór {open.Vervaldatum:dd-MM-yyyy}");
            var nogmaals = Inkoopverwerker.Verwerk(herkend, relaties, [], geheugen, codes, [new BestaandeFactuur(nieuw.Code, factuur.Factuurnummer, factuur.Datum, factuur.Totaal(codes))]);
            Console.WriteLine($"  Dezelfde factuur nog eens aangeboden: automatisch {(nogmaals.Automatisch ? "ja" : "nee")} – {string.Join(" ", nogmaals.Belemmeringen)}");
        }

        return 0;
    }

    private static void Toon(HerkendeFactuur f)
    {
        Console.WriteLine($"── Herkend ({f.Bron}) ──");
        Console.WriteLine($"  Leverancier:    {Of(f.Naam)}");
        Console.WriteLine($"  Btw-nummer:     {Of(f.BtwNummer)}");
        Console.WriteLine($"  KvK-nummer:     {Of(f.KvkNummer)}");
        Console.WriteLine($"  IBAN:           {Of(f.Iban)}");
        Console.WriteLine($"  Factuurnummer:  {Of(f.Factuurnummer)}");
        Console.WriteLine($"  Factuurdatum:   {Of(f.Factuurdatum?.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture))}");
        Console.WriteLine($"  Vervaldatum:    {Of(f.Vervaldatum?.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture))}");
        Console.WriteLine($"  Omschrijving:   {Of(f.Omschrijving)}");
        Console.WriteLine($"  Excl. btw:      {Of(f.TotaalExclusief is { } e ? Bedragen.Euro(e) : null)}");
        foreach (var b in f.Btwregels)
        {
            Console.WriteLine($"  Btw {b.Percentage:0.##}%:{new string(' ', Math.Max(1, 9 - b.Percentage.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Length))}{Bedragen.Euro(b.Btw)}{(b.Grondslag is { } g ? $" over {Bedragen.Euro(g)}" : string.Empty)}");
        }

        Console.WriteLine($"  Totaal:         {Of(f.TotaalInclusief is { } t ? Bedragen.Euro(t) : null)}");
        Console.WriteLine($"  Kenmerk:        {Of(f.Betalingskenmerk)}");
        Console.WriteLine($"  Op eigen naam:  {f.OpEigenNaam switch { true => "ja", false => "nee", null => "(niet nagegaan, geef --eigen-naam)" }}");
        var punten = f.Controleer();
        Console.WriteLine(punten.Count == 0 ? "  Controle:       compleet en sluitend" : $"  Controle:       {string.Join(" ", punten)}");
    }

    private static void ToonVoorstel(Inkoopvoorstel voorstel, IReadOnlyList<BtwCode> codes)
    {
        Console.WriteLine($"  Advies:         {voorstel.Advies.Reden}{(voorstel.Advies.Rekeningnummer is { } r ? $" → rekening {r}, btw-code {voorstel.Advies.BtwCode}" : string.Empty)}");
        if (voorstel.Factuur is { } factuur)
        {
            foreach (var regel in factuur.Regels)
            {
                Console.WriteLine($"  Regel:          {regel.Rekeningnummer} {regel.BtwCode} {Bedragen.Euro(regel.Bedrag)} + btw {Bedragen.Euro(regel.Btw ?? 0)}");
            }

            Console.WriteLine($"  Te boeken:      {Bedragen.Euro(factuur.Totaal(codes))}, vervalt {factuur.Vervaldatum:dd-MM-yyyy}");
        }

        Console.WriteLine($"  Automatisch:    {(voorstel.Automatisch ? "ja, het pakket boekt zelf" : "nee, naar Te verwerken")}");
        foreach (var belemmering in voorstel.Belemmeringen)
        {
            Console.WriteLine($"                  - {belemmering}");
        }
    }

    private static Rekeningschema Rekeningschema(string? kosten)
    {
        var schema = new Rekeningschema(
        [
            new("1510", "Te vorderen btw", Rekeningsoort.Balans, "BVorVbkTvo", Rekeningfunctie.Voorbelasting),
            new("1520", "Te betalen btw", Rekeningsoort.Balans, "BSchBepBtw", Rekeningfunctie.AfTeDragenBtw),
            new("1600", "Crediteuren", Rekeningsoort.Balans, "BSchCreHac", Rekeningfunctie.Crediteuren),
        ]);
        if (kosten is not null && schema.Zoek(kosten) is null)
        {
            // Decimaal rekeningschema: rubriek 0–3 is balans (vaste activa, financieel, tussenrekeningen, voorraad).
            var balans = kosten.Length > 0 && kosten[0] is >= '0' and <= '3';
            schema.Voegtoe(new(kosten, balans ? "Gekozen balansrekening" : "Gekozen kostenrekening", balans ? Rekeningsoort.Balans : Rekeningsoort.Resultaat));
        }

        return schema;
    }

    private static string Of(string? waarde) => waarde ?? "(niet gevonden)";

    private static string Debet(decimal bedrag) => bedrag > 0 ? Bedragen.Getal(bedrag) : string.Empty;

    private static string Credit(decimal bedrag) => bedrag < 0 ? Bedragen.Getal(-bedrag) : string.Empty;
}
