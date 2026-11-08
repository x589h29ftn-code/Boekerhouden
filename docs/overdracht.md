# Overdracht: stand van zaken en werkwijze

Voor wie (mens of Claude-sessie) verder gaat aan Boekerhouden. Lees eerst `CLAUDE.md` (werkafspraken en structuur),
dan dit document, en voor de inhoud `docs/administratie/README.md` (administratiepakket) en `docs/onderzoek/README.md`
(samenstelpakket). Stand: 7 november 2026 (na het dashboard met brutomarge).

## 1. Waar we staan

Er zijn twee apps die één huisstijl delen:

- **Samenstelpakket** (`src/Boekerhouden.Server`, poort 5080): jaarrekening samenstellen en winstaangifte. Roadmap in
  `docs/onderzoek` §8.4. In de laatste periode niet aangepast, behalve de gedeelde uitstraling.
- **Administratiepakket** (`src/Boekerhouden.Administratie`, poort 5090, en de Windows-app
  `src/Boekerhouden.Administratie.Windows`): de boekhouding zelf. Hier is de laatste weken aan gewerkt. Wat er is, staat
  per onderdeel in `docs/administratie/README.md` §5 ("Stand na A1–A7", "Inkoop", "Bank" en de tabellen met
  *Onderdeel | Hoe*).

Laatst gebouwd (oktober en begin november 2026), in deze volgorde:

1. **Inkoop en bank in de praktijk** (batch 1–3 met fictieve facturen en MT940 van de gebruiker): herkenning,
   afletteren met verzamelbetalingen, deelbetalingen, dubbele betalingen als te vorderen, betalingskorting,
   autoherkenning op de bankomschrijving, openstaande posten in een eigen venster (tweede scherm).
2. **Handigheden bij overzichten en boekingen**:
   - grootboekkaart doorzoeken en regels herboeken;
   - grootboekkaart en kolommenbalans naar Excel en PDF;
   - saldo-aansluitingen;
   - memoriaal kopiëren en elke maand herhalen;
   - kleine betaalverschillen automatisch afboeken (kantoorinstelling);
   - sorteren en kolommen slepen in alle tabellen.

   Met `ScenarioTests`.
3. **Uitstraling** (minder "gegenereerd", zakelijker):
   - iconenset (Phosphor) en een eigen palet (inktblauw met roest als accent, donkere kopbalk);
   - het systeemlettertype (Segoe UI op Windows) met IBM Plex Sans als terugval;
   - boekhoudconventies: gewoon minteken (-), dubbele lijn onder het totaal;
   - koppen met kenmerken (saldo, laatste afschrift);
   - slepen op de hele pagina, statusbalk, vinkjes in plaats van schakelaars, lichte invoervelden;
   - "vandaag 18:32" en bevestigingen met inhoud.

   Alles in `src/Boekerhouden.Huisstijl/wwwroot/uitstraling.css`.
4. **Verkoop (A9)**, in zes plakjes (README §5 *Verkoop*):
   - verkoopfactuur maken: concept → definitief met doorlopend nummer per jaar (20260001), betalingskenmerk,
     btw-controle (verlegd, ICP), boeking op debiteuren, creditfactuur, periodeverdeling;
   - verkoopboek en openstaande debiteuren (gedeelde schermen met inkoop en crediteuren);
   - factuurlay-out met een ontwerper (blokken slepen, logo, lettertype, kleuren, kolommen, teksten met variabelen),
     PDF, afdrukken, e-factuur (UBL Peppol BIS 3) en e-mail als .eml;
   - terugkerende facturen (maand, kwartaal, halfjaar, jaar; `#periode`), ook meteen definitief en verdeeld;
   - verkoopfacturen uit een ander pakket inlezen (UBL, PDF, scan), vanzelf boeken als alles klopt.
5. **Bank, vervolg**: overlappende afschriften herkennen (melding, *mogelijk dubbel* rechts van de omschrijving,
   mutaties selecteren en verwijderen); aansluiting ingelezen bankmutaties tegen het bankdagboek (een verwijderde
   boeking valt op); verkoopregel met vast bedrag (zonder aantal); standaardomschrijving (kantoorinstelling).
6. **Rustiger schermen**: formulieren achter een knop (`Uitklapformulier`), links in lijsten in tekstkleur, één regel
   per wijziging in het log, nauwelijks vet (alleen paginatitel en paneelnaam, gewicht 500).
7. **Einde klantrelatie en overstappen** (README §3.9 en §3.11): administraties archiveren (alleen inzien) en door
   de beheerder verwijderen (tombstone `VerwijderdeAdministraties` voor de triggers); een boekjaar als auditfile
   (XAF 3.2, knop op *Balans en W&V*) en een auditfile inlezen als nieuwe administratie (*Nieuwe administratie*).
8. **Dashboard** (README §3.12, `DashboardDienst`, `DashboardPagina`): de startpagina `/a/{id}` van een
   administratie; *Balans en W&V* is verhuisd naar `/a/{id}/balans`.
   - Widgets per gebruiker te kiezen en te ordenen (*Aanpassen*, tabel `Dashboardindelingen`): bank en kas, bankmutaties
     en inkoop te verwerken, btw (afgelopen maand of kwartaal plus lopende periode), omzet/inkoop/resultaat per maand,
     liquiditeit (bank en kas cumulatief per maand, 12 maanden), openstaande debiteuren en crediteuren, kerncijfers
     tegen vorig jaar, verkoop, aansluitingen, brutomarge.
   - Bedragen in hele euro's (`Weergave.HeleEuros`: kolommen sluiten op het totaal).
   - Rekent altijd vanaf vandaag (lopend jaar, saldi per vandaag), los van het jaar dat elders open staat.
   - Alles door te klikken, met de periode in het adres (kolommenbalans `?van=&tot=`, grootboekkaart, bank, inkoop).
   - Grafieken als SVG zonder script (`Components/Gedeeld/Grafiek.cs`, `Maandgrafiek`, `Liquiditeitsgrafiek`);
     kleuren `--grafiek-1..3` in `administratie.css`, gecontroleerd op kleurenblindheid.
   - **Margegroepen**: in het rekeningschema per resultaatrekening een groep (bijv. *Product X*); RGS `WOmz` telt als
     omzet, de rest als inkoop (`GrootboekDienst.ZetMargegroep`, domein `Grootboek/Marges`). Widget *Brutomarge*:
     tegen vorig jaar met afwijking, of per kwartaal.

9. **Perioden afsluiten en vaste activa** (README §3.13 en §3.14): een blokkeerdatum per maand (heropenen met
   reden; elke dienst die boekt controleert, de keten is het vangnet) en een module vaste activa (activakaart vanuit
   een aanschaf op een activarekening, afschrijvingsplan, afschrijven per maand/kwartaal/jaar (ook vanaf de kaart), desinvesteren:
   verkoop, inruil, sloop, ook een deel, met de boeking vooraf en terugdraaien, direct activeren bij het boeken,
   aanschaf en factuur op de kaart, activastaat naar Excel en PDF, activastaat met aansluiting op het grootboek). `PeriodeTests`, `ActivaTests`.

10. **Updates en reservekopieën van de Windows-app** (beveiliging.md §4.9): reservekopie vóór elke update met
    controles en terugzetten bij een fout, nieuwere database niet openen, dagelijkse kopie, bewaarschema, tweede
    map, terugzetten bij de volgende start (Beheer, Reservekopieën). `UpdateEnReservekopieTests`.

11. **Zijbalk en zoeken** (stap 1 van het plan voor de uitstraling): het menu van een administratie staat links in
    een zijbalk (`Administratiemenu` = de groepen en pagina's), bovenaan de administratiewisselaar (naar een andere
    administratie op dezelfde pagina), smal te maken (onthouden per browser, `toetsen.js`). Ctrl+K of de knop
    Zoeken in de kopbalk opent het zoekvenster (`ZoekDienst`): pagina's, relaties, rekeningen, boekingen op boekstuk,
    omschrijving, factuurnummer of bedrag, verkoopfacturen, vaste activa en administraties. `ZoekTests`.

12. **Werklijst en kantooroverzicht** (stap 2): de startpagina toont bovenaan wat er over alle administraties te doen
    is (inkoopfacturen en bankmutaties te verwerken, verkoopconcepten, vervallen debiteuren; een klik toont alleen die
    administraties, de meeste bovenaan) en per administratie in de tabel dezelfde aantallen, tot wanneer de bank is
    ingelezen (let op na 45 dagen) en tot wanneer de perioden zijn afgesloten (`KantooroverzichtDienst`, via
    `Toegang.OpenOverzicht`: een paar gegroepeerde queries over alle administraties van de gebruiker). Bij 200
    administraties × 5 jaar (919.000 boekingen): PostgreSQL 130 ms koud en 25 ms warm, SQLite 214 en 108 ms; zoeken
    (Ctrl+K) 150 en 123 ms. `KantooroverzichtTests`.
    Nog te doen uit het plan: statusbolletjes, lege staten met een
    actie, meldingen met ongedaan maken; formulieren in blokken met een vaste opslaanbalk, vaste tabelkoppen,
    bulkacties; dichtheid, donkere modus, logo.

**Branch**: al het werk staat op `claude/amazing-albattani-pmv7g5`, de enige branch op GitHub
(`x589h29ftn-code/boekerhouden`). Ga daarop verder (clone of checkout van die branch).

Alle tests zijn groen op PostgreSQL en SQLite. De CI (GitHub Actions) bouwt bij elke push ook de Windows-app als
artefact `Boekerhouden-Administratie-win-x64`.

## 2. Wat nog openstaat

Afgesproken of voorgesteld maar nog niet gebouwd, ongeveer in volgorde van wens:

| Wat | Toelichting |
|---|---|
| Logo / woordmerk | Een eigen woordmerk of het logo van het kantoor (kantoorinstelling), ook in favicon en PDF's |
| Afdrukken | Elk overzicht netjes met Ctrl+P: zonder menu en knoppen, met administratie, periode en paginanummers |
| Lettertype beoordelen | Segoe UI (systeem) staat aan sinds commit `aea7fd9`; de gebruiker test het in de Windows-app. Terug naar IBM Plex is één regel (`--bs-body-font-family` in `uitstraling.css`) |
| Verkoop: vervolg | Versturen vanuit het pakket (SMTP of Peppol) in plaats van een .eml; terugkerende facturen vanzelf maken (zonder klik); herinneringen; ouderdomsanalyse debiteuren |
| Btw-aangifte (A10) | Aangifte per rubriek en afdracht (perioden afsluiten is er: na de aangifte de periode afsluiten) |
| Saldibalans naar het samenstelpakket (A12) | Jaarrekening zonder export |
| Gepland (README §5) | Regels splitsen per leverancier, verzamel-PDF splitsen, vreemde valuta, ontbrekende factuur |
| Dashboard: vervolg | Voorgesteld, nog niet gevraagd: afwijking marge in procentpunten; in de kwartaalweergave ook vorig jaar per kwartaal; btw-frequentie (maand/kwartaal) als instelling van de administratie in plaats van per widget |
| Auditfile: vervolg | De btw-code per boekingsregel zit niet in de XAF-export; XAF 4.0 is alleen met een zelfgemaakt voorbeeld getest |
| Vaste activa: vervolg | Degressief en fiscaal naast commercieel, overzicht investeringsaftrek (KIA) |
| Updates webapp | Dezelfde aanpak voor de webapp op kantoor: onderhoudsmodus, `pg_dump` met controle, `--migreer` in plaats van automatisch bij het starten, versleutelde kopie buiten de server, CI-test vanaf de vorige release |
| Hulppagina | De uitleg hoort in `/hulp` (CLAUDE.md, rustige uitstraling); in het administratiepakket nog niet gebouwd |

## 3. Werkwijze met de gebruiker

- **Taal**: alles in het Nederlands (gesprek, code, meldingen, documentatie, commitberichten).
- **Kleine plakjes**: per verzoek bouwen, met tests (ook negatieve toegangstests), documentatie bijwerken
  (`docs/administratie/README.md`, waar nodig `CLAUDE.md`), dan `dotnet format --verify-no-changes`, commit en push.
- **Na elke push**: na ongeveer 15 minuten de CI controleren. Is hij groen, dan kort melden met de link naar de run;
  de gebruiker test de Windows-app uit het artefact. Is hij rood, dan de oorzaak uitzoeken en herstellen.
- **Schermen**: bij wijzigingen aan de uitstraling schermafbeeldingen van voor en na laten zien (§4); de gebruiker
  beoordeelt op beeld.
- **Testbestanden van de gebruiker** (fictieve facturen en MT940-bestanden) komen in `testdata/` (in `.gitignore`)
  en nooit in git. Een nieuwe sessie heeft ze niet: vraag de gebruiker ze opnieuw te uploaden als een test ze nodig
  heeft. Voor tests in de repository alleen zelf verzonnen data in `tests/Fictief/`.
- **Geen pull request** maken tenzij de gebruiker erom vraagt; er wordt op één branch doorgewerkt en gepusht.
- Geen verwijzingen naar bestaande samenstelpakketten of leveranciers (CLAUDE.md).

## 4. Testen en controleren in een cloudsessie

```bash
dotnet build Boekerhouden.slnx
dotnet test Boekerhouden.slnx
BOEKERHOUDEN_TEST_SQLITE=1 dotnet test tests/Boekerhouden.Administratie.Tests
dotnet format Boekerhouden.slnx --verify-no-changes   # duurt in de cloud enkele minuten
scripts/ui/herstart.sh && node scripts/ui/schermafbeeldingen.js /tmp/schermen voor   # browsercontrole, zie scripts/ui
```

- **PDF en Chromium**: in de cloudcontainer blijft de gewone Chromium hangen, ook op een lege pagina. Daardoor falen
  daar de PDF-tests van het samenstelpakket (`CorrectieExportTests`, `RapportDienstTests`) met een time-out van een
  minuut. Dat ligt aan de omgeving, niet aan de code: met de lichte Chromium slagen ze. Zet daarvoor
  `Pdf__Browser=/opt/pw-browsers/chromium_headless_shell-*/chrome-headless-shell-linux64/chrome-headless-shell`.
  `scripts/ui/herstart.sh` doet dat zelf. Op de CI slagen ze gewoon.
- **Schermafbeeldingen met Segoe UI**: Segoe UI staat niet in de Linux-container. Microsoft heeft het open lettertype
  Selawik uitgebracht als vervanger met dezelfde maten (github.com/microsoft/Selawik, OFL; alleen bronbestanden, te
  bouwen met `pip install fontmake` en `fontmake -u Selawik-Regular.ufo -o ttf`). Lokaal installeren onder de naam
  "Segoe UI" (naamtabel aanpassen met fontTools, in `~/.fonts`, dan `fc-cache -f`) geeft een goede benadering. Niet
  in de repository zetten.
- **Playwright** met Chromium is in de cloudsessie al geïnstalleerd; niet opnieuw installeren.

## 5. Valkuilen die we tegenkwamen

- **Sorteren**: een `SorteerKop` heeft `Gewijzigd="StateHasChanged"` van de pagina nodig (verplicht). Zonder tekent
  alleen de kop opnieuw en blijven de regels in de oude volgorde.
- **Animaties en vaste lagen**: een animatie met `transform` en `animation-fill-mode: both/forwards` maakt van het
  element een *containing block*; een `position: fixed`-laag erin (venster, sleeprand) beslaat dan niet het hele
  venster. In `uitstraling.css` daarom `backwards`.
- **Iconen**: alleen uit `iconen.svg` via `<Icoon Naam="..." />`; `UitstralingTests` controleert dat elk gebruikt icoon
  erin staat en dat het bestand geldige XML is (een kapot bestand maakt alle iconen onzichtbaar).
- **Bedragen tonen versus invoeren**: op het scherm `Weergave.Bedrag` (gewoon minteken -, geen lang minteken); in invoervelden en exports
  `Bedragen.Getal` (koppelteken). `BedragParser` leest beide.
- **SQLite** telt geen decimals op in de database: eerst ophalen, dan optellen (zie `AansluitingDienst`). Na een
  modelwijziging altijd beide migraties maken (PostgreSQL en SQLite).
- **Bedragkolommen**: `td.bedrag` in lijsten krijgt `width: 1%` met een minimumbreedte; in invoertabellen
  (`.regeltabel`, `.bankregels`) niet, anders worden Debet en Credit te smal.
- **Prerender**: `WebTests` controleert teksten in de HTML zoals de server die eerst stuurt; bij het hernoemen van
  knoppen of koppen die tests bijwerken.
- **Een blok in de ontwerper krijgt geen klik**: de doorlopende tekst (`fp-stroom`) begint bovenaan de pagina; zijn
  witruimte lag over de blokken. Daarom `pointer-events: none` op `.fp-stroom` (en `auto` op zijn kinderen).
- **Voettekst op elke pagina**: `position: fixed` gedraagt zich in Chromium-print onbetrouwbaar (op een pagina weg, op
  een andere bovenaan). De voettekst staat daarom in de paginamarge (`@page { @bottom-center { content: ... } }`).
- **JavaScript koppelen binnen `AdministratieKader`**: de pagina krijgt geen `OnAfterRender` als de kader zijn inhoud
  pas later toont; maak er een eigen component van die zichzelf koppelt (zoals `Ontwerpvlak`).
- **Weergave wisselen**: haal bij een schakelaar (Totaal/Per maand, Grootboek/Uitgebreid) eerst de cijfers voor de
  nieuwe weergave op en zet dan weergave en cijfers samen om. Wie eerst de schakelaar omzet en daarna laadt, laat de
  pagina één keer tekenen met de cijfers van de vorige weergave (kolommenbalans: index buiten de array, tabel weg, alleen
  de tweede keer). `scripts/ui/schakelaars.js` klikt alle schakelaars op alle pagina's heen en terug en meldt fouten.
- **Windows-app nalopen**: de CI-job `windows-app-proef` vult de app met twee jaar gegevens en klikt elke pagina na
  (`Proef.cs`, `proef.js`); bij een rode job staan in het artefact `windows-app-proef` het verslag `proef.txt` (met de
  pagina en knop van de fout) en schermafdrukken. Een nieuwe pagina hoort in `Proef.Paginas`.
- **Migraties en de Windows-app**: elke migratie moet ook terug kunnen (Down) en met gegevens werken;
  `UpdateEnReservekopieTests.Bijwerken_vanaf_een_eerdere_versie` werkt vanaf de laatste versies bij. Voeg daar een
  regel toe na een nieuwe migratie. SQLite staat in WAL-modus: kopieer een database nooit als los bestand terwijl
  hij open is, gebruik `Reservekopieen.Maak` (SQLite-backup).
- **Een nieuwe manier van boeken**: laat hem via `GrootboekDienst.VoegToeAanKeten` of `NieuweVersie` lopen en
  controleer vooraf `GrootboekDienst.Afgesloten` (anders krijgt de gebruiker bij een afgesloten periode een
  uitzondering in plaats van een melding).
- **Processen stoppen**: `pkill -f` met een patroon dat ook in de eigen opdrachtregel staat, stopt de eigen shell;
  gebruik `pgrep` met een specifiek pad (zoals in `scripts/ui/herstart.sh`).
- **Razor en SVG**: een `<text>`-element met attributen kan niet in Razor (`<text>` is daar een sleutelwoord); maak het
  als `MarkupString` (`Grafiek.Label`). `<a href>` rond een `rect` in een SVG werkt wel.
- **Tests met een vaste datum**: `VasteDagFixture` (in `DashboardDienstTests`) zet een vaste `TimeProvider`
  (15 oktober 2026); diensten die "vandaag" gebruiken horen `TimeProvider` te injecteren.
- **Selectors in `schermafbeeldingen.js`**: de liquiditeitsgrafiek gebruikt ook de class `maandgrafiek`; kies in het
  script een widget via `[data-widget="..."]`.
