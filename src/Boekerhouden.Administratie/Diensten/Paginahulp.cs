namespace Boekerhouden.Administratie.Diensten;

/// <summary>De uitleg van één pagina: waar hij voor is en hoe je er werkt, in een paar korte punten.</summary>
public sealed record Paginauitleg(string Titel, string Wat, IReadOnlyList<string> Hoe)
{
    /// <summary>Een tekst in stukken: wat tussen sterretjes staat (de naam van een knop of tabblad), komt cursief.</summary>
    public static IEnumerable<(string Tekst, bool Cursief)> Delen(string tekst)
    {
        ArgumentNullException.ThrowIfNull(tekst);
        var stukken = tekst.Split('*');
        for (var i = 0; i < stukken.Length; i++)
        {
            if (stukken[i].Length > 0)
            {
                yield return (stukken[i], i % 2 == 1);
            }
        }
    }
}

/// <summary>
/// De uitleg bij het vraagteken rechtsonder: per pagina kort wat hij doet en hoe het werkt. De volledige tekst staat
/// in de documentatie (<c>/hulp</c>), in het hoofdstuk van <see cref="Hulpsectie"/>.
/// </summary>
public static class Paginahulp
{
    /// <summary>De uitleg bij een pad zonder de eerste slash (zoals <c>NavigationManager.ToBaseRelativePath</c> het geeft).</summary>
    public static Paginauitleg Van(string pad) => Uitleg[Sleutel(pad)];

    /// <summary>Welke uitleg bij een pad hoort.</summary>
    public static string Sleutel(string pad)
    {
        ArgumentNullException.ThrowIfNull(pad);
        var delen = pad.Split('?', '#')[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
        return delen switch
        {
            [] => "administraties",
            ["administraties", "nieuw"] => "nieuwe-administratie",
            ["beheer", var onderdeel, ..] when Uitleg.ContainsKey("beheer-" + onderdeel) => "beheer-" + onderdeel,
            ["beheer", ..] => "beheer-gebruikers",
            ["account", "beveiliging"] => "beveiliging",
            ["a", _] => "overzicht",
            ["a", _, "inkoop", _, ..] => "inkoopfactuur",
            ["a", _, "verkoop", "terugkerend", ..] => "terugkerend",
            ["a", _, "verkoop", _, ..] => "factuur",
            ["a", _, "bank", "openstaand", ..] => "bank",
            ["a", _, "activa", "staat", ..] => "activastaat",
            ["a", _, "activa", _, ..] => "activum",
            ["a", _, "kostenplaatsen", _, ..] => "kostenplaats",
            ["a", _, "relaties", _, ..] => "relatie",
            ["a", _, "btw", "rondrekening", ..] => "rondrekening",
            ["a", _, "instellingen", "btw", ..] => "instellingen-btw",
            ["a", _, "instellingen", "verwerken", ..] => "instellingen-verwerken",
            ["a", _, var onderdeel, ..] => onderdeel switch
            {
                "debiteuren" or "crediteuren" => "openstaand",
                _ when Uitleg.ContainsKey(onderdeel) => onderdeel,
                _ => "overzicht",
            },
            _ => "administraties",
        };
    }

    /// <summary>Alle uitleg, per pagina.</summary>
    public static readonly IReadOnlyDictionary<string, Paginauitleg> Uitleg = new Dictionary<string, Paginauitleg>
    {
        ["administraties"] = new("Administraties",
            "Alle administraties waar je toegang toe hebt, met wat er per administratie nog te doen is.",
            [
                "Klik op een administratie om hem te openen.",
                "De aantallen laten zien wat er klaarstaat: inkoop en bank om te verwerken, verkoopconcepten en vervallen debiteuren.",
                "Je kunt zoeken op naam of klantnummer.",
                "Met *Nieuwe administratie* maak je er een aan; daarna vul je de instellingen in.",
            ]),
        ["nieuwe-administratie"] = new("Nieuwe administratie",
            "Een nieuwe administratie, met het standaardrekeningschema (RGS) dat bij de rechtsvorm hoort.",
            [
                "Vul de naam in en kies het rekeningschema.",
                "Na *Aanmaken* vul je bij de instellingen je gegevens, de bank en de btw in.",
                "De beginbalans lees je in uit de jaarrekening van vorig jaar (PDF).",
                "Komt de administratie uit een ander pakket? Lees dan de auditfile (XAF) in.",
            ]),
        ["overzicht"] = new("Overzicht",
            "De stand van de administratie: wat er te doen is en de belangrijkste cijfers.",
            [
                "Onder *Te doen* staat wat je nu kunt verwerken; klik erop om te beginnen.",
                "Met *Aanpassen* kies je welke widgets je ziet, in welke volgorde en hoe groot. Dat geldt alleen voor jou.",
                "Klik op een cijfer om naar de bijbehorende pagina te gaan.",
                "Je kunt hier (en op elke andere pagina) een bestand neerzetten; Sifer zet het op de juiste plek.",
            ]),
        ["inkoop"] = new("Inkoop te verwerken",
            "Ingelezen inkoopfacturen die nog geboekt moeten worden.",
            [
                "Lees facturen in als PDF, foto of e-factuur, of sleep ze op de pagina.",
                "Open een factuur om het voorstel te controleren en te boeken.",
                "*Opnieuw herkennen* leest de facturen opnieuw, met wat Sifer inmiddels heeft geleerd.",
                "Staan er meerdere facturen in één PDF, splits die dan met *Stapel splitsen*.",
                "Herkent Sifer een bekende leverancier met zekerheid, dan boekt hij de factuur zelf.",
            ]),
        ["inkoopfactuur"] = new("Inkoopfactuur",
            "Eén factuur controleren en boeken: links de gegevens, rechts de factuur.",
            [
                "Controleer de leverancier, het factuurnummer, de datums, de bedragen en de btw.",
                "Staat er iets verkeerd, wijs dan de juiste plek aan op de factuur. Sifer onthoudt dat voor de volgende keer.",
                "Kies per regel de rekening en de btw-code; zo nodig verdeel je het bedrag over meer regels.",
                "*Boeken* zet de factuur in het inkoopboek en bij de crediteuren.",
                "Een geboekte factuur pas je aan met *Aanpassen*. Is hij al betaald, dan kun je alleen nog de kostenplaatsen wijzigen.",
            ]),
        ["inkoopboek"] = new("Inkoopboek",
            "Alle geboekte inkoopfacturen.",
            [
                "Kies een periode of *Alle jaren*, en zoek op leverancier, factuurnummer of omschrijving.",
                "Filter zo nodig op bedrag, of toon met *Alleen open* de facturen die nog niet betaald zijn.",
                "Klik op een factuur om hem te bekijken, aan te passen of terug te draaien.",
                "*Verwerking* toont of een factuur met de hand of vanzelf is geboekt; bovenaan staat welk deel vanzelf ging.",
            ]),
        ["stapel"] = new("Stapel splitsen",
            "Een PDF met meerdere facturen opdelen in losse facturen.",
            [
                "Sifer geeft aan waar volgens hem een nieuwe factuur begint; klik tussen twee pagina's om dat te veranderen.",
                "Pagina's die geen factuur zijn, sla je over met *Weglaten*.",
                "Na het splitsen staan de facturen klaar bij inkoop of verkoop, waar je de stapel hebt ingelezen.",
            ]),
        ["verkoop"] = new("Verkoop te verwerken",
            "Verkoopfacturen uit een ander pakket (kassa, webwinkel) inlezen en boeken.",
            [
                "Lees facturen in als PDF, e-factuur of foto, of sleep ze op de pagina.",
                "Is de klant bekend, dan wordt de factuur meteen geboekt.",
                "Is de klant nog niet bekend, kies hem dan of maak hem aan.",
                "De factuur houdt het nummer uit het andere pakket.",
            ]),
        ["verkoopboek"] = new("Verkoopboek",
            "Alle geboekte verkoopfacturen, zowel zelf gemaakt als ingelezen.",
            [
                "Kies een periode of *Alle jaren*, en zoek op klant, factuurnummer of omschrijving.",
                "Filter zo nodig op bedrag, of toon met *Alleen open* de facturen die nog niet betaald zijn.",
                "Klik op een factuur om hem te openen.",
                "*Verwerking* toont of een factuur met de hand of vanzelf is geboekt; bovenaan staat welk deel vanzelf ging.",
            ]),
        ["facturen"] = new("Facturen maken",
            "Je eigen verkoopfacturen, als concept of definitief.",
            [
                "Een *Nieuwe factuur* is eerst een concept.",
                "*Definitief maken* geeft de factuur een nummer en een betalingskenmerk, en boekt hem.",
                "Vink concepten aan om ze in één keer definitief te maken.",
                "*Alleen open* toont de facturen die nog niet betaald zijn.",
            ]),
        ["factuur"] = new("Factuur",
            "Eén verkoopfactuur maken, bekijken of versturen.",
            [
                "Kies de klant en vul de regels in, elk met een btw-code; de totalen worden vanzelf berekend.",
                "*Definitief maken* geeft het volgende nummer van het jaar en boekt de factuur.",
                "Een definitieve factuur kun je niet meer wijzigen; een fout herstel je met een creditfactuur.",
                "Je verstuurt de factuur als PDF, als e-factuur (UBL) of per e-mail.",
                "Onder *Nieuw van deze* kun je de factuur kopiëren, per maand maken, herhalen of crediteren.",
            ]),
        ["terugkerend"] = new("Terugkerende facturen",
            "Facturen die elke maand, elk kwartaal, elk halfjaar of elk jaar terugkomen.",
            [
                "Leg per klant de regels vast en hoe vaak de factuur terugkomt.",
                "*Facturen maken* maakt de facturen die aan de beurt zijn, als concept of, als je dat hebt ingesteld, meteen definitief.",
                "Zet #periode in de omschrijving; op de factuur komt daar de periode te staan.",
            ]),
        ["factuurlayout"] = new("Lay-out van de factuur",
            "Hoe je verkoopfacturen eruitzien.",
            [
                "Sleep de blokken (logo, adres, gegevens, regels, totalen, voettekst) naar de plek waar je ze wilt hebben.",
                "Met *Tekstblok* voeg je een eigen tekst toe; *Opslaan* bewaart de lay-out.",
                "Je kunt meer lay-outs maken. De standaardlay-out geldt, tenzij je bij een factuur een andere kiest.",
            ]),
        ["bank"] = new("Bank",
            "Bankafschriften inlezen en de mutaties verwerken.",
            [
                "Lees afschriften in als MT940, CAMT.053 of PDF, of sleep ze op de pagina.",
                "Mutaties die Sifer met zekerheid aan een factuur kan koppelen, worden vanzelf afgeletterd. Voor de rest staat een voorstel klaar.",
                "Per mutatie kies je de openstaande posten, boek je op een rekening of verdeel je het bedrag over meer regels.",
                "Met een regel worden terugkerende mutaties, zoals bankkosten of huur, voortaan vanzelf geboekt.",
                "Met *Ook geboekt* zie je per mutatie hoe hij geboekt is, en welk deel vanzelf ging.",
            ]),
        ["memoriaal"] = new("Memoriaal",
            "Boekingen die nergens anders thuishoren, zoals correcties, afschrijvingen en lonen.",
            [
                "Vul de datum en de omschrijving in, en daarna per regel de rekening en het bedrag (debet of credit).",
                "Je kunt pas boeken als debet en credit gelijk zijn.",
                "Een boeking kun je kopiëren of elke maand laten herhalen.",
                "Onder *Nieuw* lees je een loonjournaal in (PDF); met *+ Dagboek* maak je een extra memoriaal aan.",
            ]),
        ["boeking"] = new("Boeking",
            "Eén boeking met al zijn regels.",
            [
                "Als je een boeking aanpast, ontstaat er een nieuwe versie; de oude blijft in het journaal bewaard.",
                "In het memoriaal kun je alles aanpassen, behalve regels op debiteuren of crediteuren met een factuurnummer.",
                "Bij een bankboeking veranderen de bankregel en de datum niet.",
                "In een afgesloten periode kun je niets meer aanpassen.",
            ]),
        ["kolommenbalans"] = new("Kolommenbalans",
            "Per rekening de stand over een periode, verdeeld over balans en winst-en-verliesrekening.",
            [
                "Kies rechtsboven de periode. Je kunt ook per maand kijken, of uitgebreid met de RGS-indeling.",
                "Klik op een bedrag om de boekingen te zien.",
                "Onder *Weergave* zet je vorig jaar ernaast, toon je ook rekeningen zonder saldo of rond je af op hele euro's.",
                "Onder *Downloaden* vind je Excel, PDF en het boekjaar als auditfile (XAF).",
            ]),
        ["balans"] = new("Balans en W&V",
            "De balans en de winst-en-verliesrekening van een boekjaar.",
            [
                "Kies rechtsboven het jaar.",
                "Klik op een rekening om de grootboekkaart te openen.",
                "Onder *Downloaden* vind je het boekjaar als auditfile (XAF).",
            ]),
        ["proefbalans"] = new("Proefbalans",
            "Per rekening het saldo aan het eind van het boekjaar, met de RGS-code.",
            [
                "Kies rechtsboven het jaar.",
                "Vink *Ook zonder saldo* aan om ook de rekeningen zonder saldo te zien.",
                "Klik op een rekening om de grootboekkaart te openen.",
            ]),
        ["grootboek"] = new("Grootboekkaart",
            "Alle boekingen op één rekening, met het begin- en eindsaldo.",
            [
                "Kies de rekening en de periode.",
                "Je kunt zoeken op omschrijving, bedrag, relatie of factuurnummer.",
                "Klik op een regel om de boeking te openen.",
                "Vink regels aan om ze naar een andere rekening over te boeken. Dat kan niet bij bank, debiteuren, crediteuren of btw.",
            ]),
        ["dagboeken"] = new("Dagboeken",
            "Per dagboek wat er in een periode is geboekt.",
            [
                "Kies het dagboek met de tabbladen. Je kunt zoeken op omschrijving, bedrag of relatie.",
                "Klik op een boeking om hem te openen; met het pijltje zie je de regels.",
                "Onderaan staat het totaal van de periode.",
            ]),
        ["journaal"] = new("Journaal",
            "Alle boekingen op volgorde, met hun eerdere versies.",
            [
                "Filter op dagboek, periode of tekst.",
                "Klik op een boeking om hem te bekijken of aan te passen.",
                "Een aangepaste boeking houdt zijn oude versie, zodat er nooit iets ongemerkt verdwijnt.",
                "*Keten controleren* laat zien of er buiten Sifer om iets aan de boekingen is veranderd.",
            ]),
        ["openstaand"] = new("Openstaande posten",
            "Wat klanten nog moeten betalen (debiteuren) en wat je zelf nog moet betalen (crediteuren).",
            [
                "Kies het tabblad *Debiteuren* of *Crediteuren* en de peildatum.",
                "Kies de weergave: per relatie met subtotaal, alleen de subtotalen, of alle posten op datum.",
                "Klik op een post om de factuur te openen, of op een subtotaal om de posten van die relatie te zien.",
                "Excel en PDF volgen de weergave die je hebt gekozen.",
            ]),
        ["btw"] = new("Btw-aangifte",
            "De rubrieken van de btw-aangifte per periode, berekend uit de boekingen.",
            [
                "Kies per maand, kwartaal of jaar, en klik op een periode om de rubrieken te zien.",
                "*Vastleggen als aangegeven* bewaart de aangifte zoals je hem indient, in hele euro's.",
                "Wordt er daarna nog in die periode geboekt, dan zie je het verschil als suppletie.",
                "Zolang de periode niet is afgesloten, kun je een vastgelegde aangifte intrekken.",
            ]),
        ["rondrekening"] = new("Btw-rondrekening",
            "Controle of de omzet en de btw over het jaar overeenkomen met de aangiften.",
            [
                "Per periode zie je de rubrieken uit de administratie naast wat je hebt aangegeven.",
                "De *Jaaropstelling* zet omzet en btw onder elkaar, met de betalingen en de suppletie.",
                "Een verschil hoort in een suppletie. Klik op een bedrag om de boekingen te zien.",
            ]),
        ["vpb"] = new("Fiscale positie",
            "De berekening van de vennootschapsbelasting, uitgaande van het resultaat van de BV.",
            [
                "Het commerciële resultaat komt uit de boekingen.",
                "Daarna volgen de niet en beperkt aftrekbare kosten, de investeringsaftrek en de verliesverrekening.",
                "Je kunt een bedrag met de hand aanpassen; de tarieven horen bij het jaar.",
                "Hier stel je ook in welke rekeningen niet of beperkt aftrekbaar zijn.",
            ]),
        ["activa"] = new("Vaste activa",
            "Alle vaste activa, met aanschafwaarde, afschrijving en boekwaarde.",
            [
                "Bij een nieuw actief vul je de aanschaf, de looptijd en de restwaarde in; Sifer maakt het afschrijvingsplan.",
                "*Afschrijven* boekt de afschrijving per maand, kwartaal of jaar tot de datum die je kiest.",
                "De staat van vorig jaar lees je in uit Excel of PDF.",
                "De activastaat sluit aan op het grootboek.",
            ]),
        ["activum"] = new("Vast actief",
            "Eén actief, met het afschrijvingsplan en de aankoopfactuur.",
            [
                "Onder *Wijzigen* boek je een extra afschrijving, of pas je de jaarlijkse afschrijving of de restwaarde aan vanaf een datum.",
                "*Desinvesteren* boekt de verkoop of het buiten gebruik stellen; dat kun je terugdraaien.",
                "Wat al is geboekt, blijft staan; alleen de komende maanden worden opnieuw berekend.",
                "Bij een auto leg je ook het privégebruik vast.",
            ]),
        ["activastaat"] = new("Staat vaste activa inlezen",
            "De vaste activa overnemen uit de staat van vorig jaar.",
            [
                "Lees de staat in uit Excel of PDF. Het sjabloon is een leeg Excelbestand om zelf in te vullen.",
                "Controleer per actief de rekening en de bedragen; het verschil met het grootboek staat erbij.",
                "*Activa overnemen* zet de activa met hun afschrijvingsplan in de administratie.",
            ]),
        ["kostenplaatsen"] = new("Kostenplaatsen",
            "Kosten en opbrengsten per kostenplaats, bijvoorbeeld per auto of per afdeling.",
            [
                "Een nieuwe kostenplaats krijgt een code en een naam.",
                "Bij inkoop, verkoop, bank en memoriaal kies je per regel een kostenplaats. Dat is nooit verplicht.",
                "Klik op een kostenplaats om alle regels te zien die erop zijn geboekt.",
                "Met het slotje sluit je een kostenplaats af; daarna kan er niet meer op worden geboekt.",
                "Vink *Afgesloten tonen* aan om ook de afgesloten kostenplaatsen te zien.",
            ]),
        ["kostenplaats"] = new("Kostenplaats",
            "Alle regels die in de gekozen periode op deze kostenplaats zijn geboekt.",
            [
                "Je kunt zoeken op omschrijving, rekening, relatie of bedrag.",
                "Met *Per rekening* zie je de regels per rekening, met een subtotaal.",
                "*Boeken* maakt een memoriaalboeking op deze kostenplaats.",
                "Klik op een regel om de boeking te openen.",
            ]),
        ["aansluitingen"] = new("Aansluitingen",
            "Controle of bank, debiteuren, crediteuren, btw en vaste activa in het grootboek kloppen.",
            [
                "Per rekening zie je het saldo in het grootboek, het saldo volgens de bron en het verschil.",
                "Klik op een regel om naar de bank, de crediteuren of de grootboekkaart te gaan.",
                "Verschillen bij de bank staan onder de regel, elk met een link naar de boeking.",
                "*Opnieuw verwerken* zet een bankmutatie terug bij de mutaties die nog verwerkt moeten worden.",
            ]),
        ["relaties"] = new("Relaties",
            "Alle klanten en leveranciers.",
            [
                "Klik op een kolomkop om te sorteren.",
                "Klik op een relatie om de gegevens te zien en wat Sifer bij de inkoop heeft geleerd.",
                "Een relatie ontstaat vanzelf als je een factuur verwerkt, maar je kunt er ook zelf een toevoegen.",
                "Het IBAN van een relatie wijzig je hier, op de regel zelf.",
            ]),
        ["relatie"] = new("Relatie",
            "Eén klant of leverancier.",
            [
                "Je ziet de naam, het btw- en KvK-nummer, en bij een klant ook het adres, de betaaltermijn en het e-mailadres.",
                "De standaardrekening en de omschrijving van de boeking gaan voor op die van de administratie.",
                "Bij *Zelf controleren* stel je in dat facturen boven een bedrag of met een afwijking altijd worden nagekeken.",
                "*Vergeten* wist alles wat Sifer bij deze leverancier heeft geleerd.",
            ]),
        ["regels"] = new("Regels",
            "Vaste afspraken, bijvoorbeeld dat een bepaalde naam of omschrijving altijd op dezelfde rekening wordt geboekt.",
            [
                "In een regel leg je vast waar Sifer op let (naam, omschrijving, bedrag) en op welke rekening hij boekt.",
                "Met *Automatisch boeken* boekt Sifer zonder dat je het eerst controleert.",
                "Een regel gaat voor op wat Sifer heeft geleerd. Passen er meer, dan telt de bovenste.",
            ]),
        ["rekeningschema"] = new("Rekeningschema",
            "De rekeningen van de administratie, met hun RGS-code.",
            [
                "Een nieuwe rekening krijgt een nummer, een naam en de RGS-code waaronder hij valt.",
                "Klik op een rekening om de naam, de btw-code, de RGS-code of de margegroep te wijzigen.",
                "De RGS-codes zelf veranderen niet; een eigen indeling maak je met eigen rekeningen.",
            ]),
        ["perioden"] = new("Perioden",
            "Per maand of de periode open of afgesloten is.",
            [
                "*Periode afsluiten* loopt de stappen met je door: bank, inkoop, verkoop, aansluitingen en btw.",
                "In een afgesloten periode kun je niets meer boeken of aanpassen.",
                "Een periode heropenen kan alleen met een reden; die komt in het wijzigingslog.",
            ]),
        ["instellingen"] = new("Instellingen: gegevens",
            "De gegevens van de administratie zelf.",
            [
                "Vul het klantnummer, de naam, het KvK-nummer, het adres en de contactgegevens in.",
                "De IBAN's van de bank staan er al; andere eigen rekeningen vul je zelf aan.",
                "Elk tabblad heeft een eigen knop *Opslaan*.",
            ]),
        ["instellingen-btw"] = new("Instellingen: btw en belastingen",
            "Het btw-nummer, de btw-plicht en de betalingen aan de Belastingdienst.",
            [
                "Btw-plichtig: de btw wordt apart geboekt en komt in de aangifte. Niet btw-plichtig: inkoop inclusief btw en geen aangifte.",
                "Bij de betalingen aan de Belastingdienst staat in grijs de rekening die Sifer zelf kiest.",
                "Betalingen met een geldig betalingskenmerk kan Sifer vanzelf boeken.",
                "De aangifteperiode kies je bij de btw-aangifte.",
            ]),
        ["instellingen-verwerken"] = new("Instellingen: verwerken",
            "Hoe inkoop en bank vanzelf worden verwerkt, en of je kostenplaatsen gebruikt.",
            [
                "Wil je dat deze administratie afwijkt van het kantoor, zet dan per onderdeel de schakelaar uit.",
                "Laat je een veld leeg, dan geldt de standaard van het kantoor (in grijs).",
                "In de omschrijving van de boeking kun je #leverancier, #klant, #factuurnummer en #maand gebruiken.",
                "Hier stel je ook de map voor de facturen in, bijvoorbeeld in OneDrive.",
                "Met *Kostenplaatsen gebruiken* zet je kostenplaatsen aan voor deze administratie.",
            ]),
        ["beginbalans"] = new("Beginbalans",
            "De openingsbalans van het eerste boekjaar.",
            [
                "Lees de jaarrekening van vorig jaar in (PDF); Sifer stelt per post een rekening voor.",
                "Je kunt de bedragen ook met de hand invullen.",
                "Je kunt pas boeken als debet en credit gelijk zijn.",
            ]),
        ["beheer-gebruikers"] = new("Gebruikers",
            "Wie bij welke administratie mag, en met welke rechten.",
            [
                "Een nieuwe gebruiker krijgt een naam, initialen en een e-mailadres. Vink *Kantoor* of *Beheerder* aan als dat van toepassing is.",
                "Per administratie kies je: inzien, ondernemer, kantoor of geen toegang.",
                "Voor de beheerder is tweestapsverificatie verplicht.",
            ]),
        ["beheer-btw"] = new("Btw-tabel",
            "De btw-codes en tarieven die voor alle administraties gelden.",
            [
                "Per code zie je de soort, het tarief en de rubrieken (grondslag, btw en voorbelasting).",
                "Je kunt een btw-code of een tarief toevoegen; een tarief geldt vanaf een datum.",
                "Alleen de beheerder kan de tabel wijzigen.",
            ]),
        ["beheer-instellingen"] = new("Kantoorinstellingen",
            "Wat voor alle administraties geldt, tenzij een administratie afwijkt.",
            [
                "*Verwerken*: wanneer facturen en bankmutaties vanzelf worden geboekt, en de standaardomschrijving.",
                "*Overzichten*: de lettergrootte op het scherm en het afronden op hele euro's.",
            ]),
        ["beheer-wijzigingslog"] = new("Wijzigingslog",
            "Elke wijziging: wie, wat en wanneer.",
            [
                "Je kunt zoeken op gebruiker, actie of onderwerp; je ziet de laatste regels.",
                "Het log kan niet worden aangepast of verwijderd.",
            ]),
        ["beheer-reservekopieen"] = new("Reservekopieën",
            "Reservekopieën van de gegevens in de Windows-app.",
            [
                "Sifer maakt elke dag bij het starten een reservekopie, en ook vóór elke update.",
                "Met *Nu een reservekopie maken* maak je er zelf een.",
                "Kies een tweede map, bijvoorbeeld een netwerkschijf, voor een extra kopie.",
                "Terugzetten gebeurt bij de volgende start, of meteen met *Nu opnieuw starten*.",
            ]),
        ["beheer-delen"] = new("Delen op kantoor",
            "De gegevens van deze pc delen met de andere pc's op kantoor.",
            [
                "Met *Delen met andere pc's* wordt deze pc de kantoorserver.",
                "Op de andere pc's kies je *Verbinden* om met de kantoorserver te werken.",
                "Stop je met delen, dan kunnen de andere pc's niet meer verder.",
            ]),
        ["beveiliging"] = new("Beveiliging",
            "De beveiliging van je eigen account.",
            [
                "Zet tweestapsverificatie aan met een app op je telefoon.",
                "Bewaar de herstelcodes op een veilige plek.",
                "Met een passkey log je in zonder wachtwoord.",
            ]),
    };
}
