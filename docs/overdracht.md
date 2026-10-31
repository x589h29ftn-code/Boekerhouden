# Overdracht: stand van zaken en werkwijze

Voor wie (mens of Claude-sessie) verder gaat aan Boekerhouden. Lees eerst `CLAUDE.md` (werkafspraken en structuur),
dan dit document, en voor de inhoud `docs/administratie/README.md` (administratiepakket) en `docs/onderzoek/README.md`
(samenstelpakket). Stand: 31 oktober 2026.

## 1. Waar we staan

Er zijn twee apps die één huisstijl delen:

- **Samenstelpakket** (`src/Boekerhouden.Server`, poort 5080): jaarrekening samenstellen en winstaangifte. Roadmap in
  `docs/onderzoek` §8.4. In de laatste periode niet aangepast, behalve de gedeelde uitstraling.
- **Administratiepakket** (`src/Boekerhouden.Administratie`, poort 5090, en de Windows-app
  `src/Boekerhouden.Administratie.Windows`): de boekhouding zelf. Hier is de laatste weken aan gewerkt. Wat er is, staat
  per onderdeel in `docs/administratie/README.md` §5 ("Stand na A1–A7", "Inkoop", "Bank" en de tabellen met
  *Onderdeel | Hoe*).

Laatst gebouwd (oktober 2026), in deze volgorde:

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
   - iconenset (Lucide) en een eigen accentkleur (petrol);
   - het systeemlettertype (Segoe UI op Windows) met IBM Plex Sans als terugval;
   - boekhoudconventies: echt minteken, dubbele lijn onder het totaal;
   - koppen met kenmerken (saldo, laatste afschrift);
   - slepen op de hele pagina, statusbalk, vinkjes in plaats van schakelaars, lichte invoervelden;
   - "vandaag 18:32" en bevestigingen met inhoud.

   Alles in `src/Boekerhouden.Huisstijl/wwwroot/uitstraling.css`.

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
| Eén menubalk | De kopbalk en het menu van de administratie samenvoegen (kruimelpad *kantoor › administratie › pagina*) |
| Lettertype beoordelen | Segoe UI (systeem) staat aan sinds commit `aea7fd9`; de gebruiker test het in de Windows-app. Terug naar IBM Plex is één regel (`--bs-body-font-family` in `uitstraling.css`) |
| Verkoop (A9) | Factuur maken, nummeren, PDF, debiteurenlijst |
| Btw-aangifte en periodes (A10) | Aangifte per rubriek, afdracht, periodes afsluiten |
| Saldibalans naar het samenstelpakket (A12) | Jaarrekening zonder export |
| Gepland (README §5) | Regels splitsen per leverancier, verzamel-PDF splitsen, vreemde valuta, ontbrekende factuur |
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
- **Bedragen tonen versus invoeren**: op het scherm `Weergave.Bedrag` (echt minteken −); in invoervelden en exports
  `Bedragen.Getal` (koppelteken). `BedragParser` leest beide.
- **SQLite** telt geen decimals op in de database: eerst ophalen, dan optellen (zie `AansluitingDienst`). Na een
  modelwijziging altijd beide migraties maken (PostgreSQL en SQLite).
- **Bedragkolommen**: `td.bedrag` in lijsten krijgt `width: 1%` met een minimumbreedte; in invoertabellen
  (`.regeltabel`, `.bankregels`) niet, anders worden Debet en Credit te smal.
- **Prerender**: `WebTests` controleert teksten in de HTML zoals de server die eerst stuurt; bij het hernoemen van
  knoppen of koppen die tests bijwerken.
- **Processen stoppen**: `pkill -f` met een patroon dat ook in de eigen opdrachtregel staat, stopt de eigen shell;
  gebruik `pgrep` met een specifiek pad (zoals in `scripts/ui/herstart.sh`).
