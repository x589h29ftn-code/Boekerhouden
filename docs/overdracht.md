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

13. **Statusbolletjes, lege staten en ongedaan maken** (stap 3): `Statusstip` (ring = open, gevuld = klaar, warm =
    let op, lichtgrijs = uit) bij verkoopfacturen (nieuwe kolom Status: concept, ingelezen, open, vervallen, betaald;
    `Verkooplijstregel.Stand`), vaste activa, terugkerende facturen, perioden, bank (dubbel?), reservekopieën, btw-tabel,
    gebruikers en gearchiveerde administraties. `LegeStaat` = icoon, één regel en de knop om te beginnen (verkoop,
    activa, terugkerend, bank, inkoop, dashboard, startpagina). `Meldingen` (scoped) met `Meldingenhouder` in de layout:
    de bevestiging linksonder overleeft een navigatie, met "Ongedaan maken" na het boeken van een inkoopfactuur
    (tegenboeking, terug naar het document; niet als er direct is geactiveerd) en na het archiveren van een administratie.
    `MeldingenTests`.

14. **Formulieren, vaste koppen en meerdere regels tegelijk** (stap 4): `Formulierblok` (naam links, velden rechts) en
    `Opslaanbalk` (vast onderaan, "niet opgeslagen" zolang er iets gewijzigd is, Herstellen, en een vraag bij weggaan
    via `NavigationLock`) bij de instellingen van een administratie en de kantoorinstellingen (één Opslaan voor
    verwerken en overzichten). Kolomkoppen blijven bovenaan bij scrollen; een kader scrolt alleen horizontaal als de
    tabel echt te breed is (`.te-breed`, `kolommen.js`). Vinkjes en een actiebalk: verkoopconcepten definitief maken of
    verwijderen, documenten in Te verwerken verwijderen (`Bulk.Voer`: regel voor regel via de gewone dienst). `BulkTests`.

15. **Donkere weergave en dichtheid** (stap 5): in het gebruikersmenu Weergave (licht, donker, zoals het systeem) en
    Dichtheid (compact, ruim), per browser bewaard. `wwwroot/thema.js` (gewoon script in de kop, zodat er geen lichte
    flits is) zet `data-bs-theme="dark"` en de klasse `ruim` op `<html>`; `wwwroot/donker.css` (als laatste geladen)
    zet de kleuren om. Wat op papier hoort (factuur, factuurontwerp, PDF-voorbeeld) blijft wit.
    `THEMA=donker node scripts/ui/schermafbeeldingen.js …` maakt alle schermen in donker (ook `DICHTHEID=ruim`).
    Nieuwe vaste kleuren in de CSS: ook een donkere waarde in `donker.css` geven.

16. **Merkteken** (gekozen uit vier concepten): eerst de B, nu de S (Sifer) met de dubbele streep (zoals onder een totaal dat sluit), wit op
    roest. `favicon.svg` (met vlak) en `favicon-merk.svg` (alleen het teken, de kopbalk zet het roestvlak eronder) in
    de huisstijl; de Windows-app heeft `sifer.ico` (16 t/m 256 pixels) als programmapictogram en in de
    titelbalk van elk venster (`Merk.cs`; de proef controleert dat hij er is). Nog niet: een eigen kantoorlogo
    uploaden voor op de rapporten (de verkoopfactuur heeft al een logo per administratie in de factuurlay-out).

17. **Naam: Sifer** (Fries voor cijfer) met als merkteken de S met de dubbele streep, wit op oranje (roest). In alle
    zichtbare teksten van beide apps, de Windows-app (`Sifer.exe`, venster, zip `Sifer-win-x64`, pictogram
    `sifer.ico`), de naam in de authenticator-app en de auditfile. Intern blijft het Boekerhouden (zie CLAUDE.md).

18. **Afschrijving wijzigen per datum** (README §3.14): `Herziening` (ingangsmaand, percentage per jaar) op het actief
    (kolom `Herzieningen`, JSON; migratie AfschrijvingHerzien in beide databases); het plan rekent er vanaf die maand mee,
    dus ook afschrijven, activastaat en achterstand. Let op: `Activum` (domein) is een record met een lijst erin; vergelijk
    niet met `==` zonder die lijst gelijk te zetten (zie `ActivaDienst.Bewaar`). Foutmeldingen van Opslaan op de
    activakaart staan nu bij de knop (ze stonden bovenaan, buiten beeld). `HerzieningTests`, `ActivaTests`.

19. **Stapel splitsen** (README §3.19): één PDF met veel inkoop- of verkoopfacturen aanleveren, voorstel waar te
    knippen (`Stapelsplitser`), miniaturen met knipplekken en een pagina groot ernaast (`Stapelvlak`, `stapel.js`),
    scans op de achtergrond door de tekstherkenning, na Splitsen elke factuur via de gewone weg van inlezen
    (`StapelDienst`). `PdfStapelTests`, `StapelTests`; browsercontrole met `node scripts/ui/stapel.js <map>` (maakt de
    stapel met `scripts/ui/maak-stapel.py`, vereist reportlab). Let op: `scripts/ui/schermafbeeldingen.js` loopt op
    10 november vast op een oudere stap (memoriaal, link *Herhalen*) en moet nog worden bijgewerkt. De overdracht mist
    nog de btw-module, vennootschapsbelasting, loonjournaal, privégebruik auto en het verkoopboek voor ingelezen
    facturen (wel in README §3.2–§3.18).

20. **Bonnetjes, CAMT.053 van 2019, instellingen** (11 november): een foto wordt vóór de tekstherkenning een scan
    (`Fotoverbetering`: rechtop, grijs, belichting vlak, contrast, vlakken weg, rechtzetten; SkiaSharp, MIT) en een bon
    krijgt zijn bonnummer als factuurnummer (README §3.20); CAMT.053 `001.08` naast `001.02` (README §3.7); na Opslaan
    van de instellingen van een administratie terug naar het hoofdscherm met de melding linksonder.
    `FotoverbeteringTests`, `tests/Fictief/administratie/maak-bon.py` maakt de verzonnen telefoonfoto.

21. **Bonnen-stresstest** (README §3.20): 25 verzonnen bonnen (`scripts/bonnen/maak-bonnen.py`) door de hele keten
    (`BonnenstressTests`, draait alleen met `BOEKERHOUDEN_BONNENMAP`); 24 van 25 goed. Tesseract leest sindsdien in twee
    standen (psm 3 en 4) en kiest de beste. Een echte bon van de gebruiker staat in `testdata/bonnen/` (niet in git).
    13 november: bonnen in een ordner (`maak-ordnerbonnen.py`, 7 van 10): ringen weg (`WisDik`), de bon uitsnijden
    (`Uitsnede`), zachter contrast bij lichte inkt. Vier echte foto's van de gebruiker (alleen als kleine afbeelding,
    640×480) in `testdata/bonnen/echt/`: de uitsnede klopt; voor de tekstherkenning zijn ze te klein, de originelen
    (volle grootte) zijn nog nodig om de herkenning echt te toetsen. Daarna in 1500×2000 (`testdata/bonnen/groot/`): de scans
    zijn goed leesbaar, maar Tesseract leest matrixletters (Poiesz, Primera) slecht; een extra leesronde met dikkere
    inkt (3×3-minimum) won niet op de bonnen zelf en is niet opgenomen. Een onleesbaar logo geeft geen straat of
    kolomkop meer als naam. Daarna een andere motor: **PaddleOCR** (PP-OCRv5 via RapidOcrNet en ONNX Runtime,
    `PaddleOcr`, README §3.20), met Tesseract als terugval; leest de vier echte bonnen goed (naam, datum, totaal,
    btw). De bonnenstresstest gebruikt dezelfde motor als de app (`BOEKERHOUDEN_OCR=tesseract` voor Tesseract):
    25 van 25 en 8 van 10 in een ordner. Let op: RapidOcrNet is gebouwd tegen SkiaSharp 3.x, wij draaien 4.155; dat
    werkt (getest), maar controleer het bij een update van een van beide.

22. **Pinbetaling bij een bon** (README §3.8): het afletteren herkent een afschrijving zonder tegenrekening als de
    betaling van een bon op bedrag, dag (uit de omschrijving of de boekdatum) en winkelnaam (`Afletteren/Pinbetaling`,
    stap 3b in `Afletteraar`); zeker bij precies één bon. `PinbetalingTests` met 50 willekeurige bonnen.

23. **Verkoopfactuur verwerken als inkoop** (12 november): gebied aanwijzen op het origineel, variabelen in de
    omschrijving (`#klant`, `#maandnr`), een standaardomschrijving voor ingelezen verkoopfacturen (kantoor), en het
    scherm in de opzet van het verwerkscherm van inkoop (`Verkoopregels` met `Verwerken`, `Verkooporigineel`).

24. **Omvangtest** (`OmvangStressTests`, `BOEKERHOUDEN_OMVANG=300`): 300 administraties × 5 jaar × 500 facturen en
    500 bankmutaties (1,5 miljoen boekingen, 4,2 GB in PostgreSQL): alle schermen onder 150 ms. Niet gemeten:
    SQLite, meerdere gebruikers tegelijk, echte PDF-groottes (documenten staan in de database).

25. **Samenwerken vanuit de Windows-app** (beveiliging.md, punt 16 in §5; LEESMIJ van de app): de app kent drie
    werkwijzen (`Lokaal/Werkwijze`): alleen, kantoorserver (de app start de webapp in zijn eigen proces op het
    SQLite-bestand, `Kantoorserver.Start`) en werkplek (`Kantoorvenster`: een WebView2-venster op de server).
    Inloggen zonder wachtwoord met een sleutel per Windows-gebruiker per pc (`ApparaatDienst`, `Werkpleksleutel`,
    `Werkplekopslag` met DPAPI); de beheerder koppelt werkplekken en zet de toegang per administratie onder
    *Beheer, Gebruikers en toegang*; wisselen van werkwijze onder *Beheer, Delen* (`Werkwijzebeheer`).
    Het Windows-deel is alleen in de CI te draaien (`--proef map --kantoorserver`); een tweede pc koppelen is
    niet automatisch getest: dat moet de gebruiker op twee pc's proberen.

26. **Stresstests samenwerken, reservekopie en update** (12-13 november): `GelijktijdigStressTests`
    (`BOEKERHOUDEN_GELIJKTIJDIG=5`; vijf gebruikers tegelijk, op PostgreSQL en SQLite: geen fouten; twee gebruikers
    met hetzelfde boekstuk of dezelfde nieuwe leverancier krijgen een gewone melding, `Databasefout.IsDubbel`) en
    `KopieEnUpdateStressTests` (`BOEKERHOUDEN_KOPIESTRESS=600`; kopieën terwijl er gewerkt wordt, update, mislukte
    update, onderbroken situaties). Daaruit: de ruimtecontrole vóór een kopie of update (`Reservekopieen.MaakRuimte`).
    Open: inkoop inlezen wordt traag (1 s per factuur) als vijf gebruikers dat tegelijk in dezelfde administratie
    doen; en één CI-run was rood op een niet-gevonden wankele test (run 197).

**Branch**: al het werk staat op `claude/amazing-albattani-pmv7g5`, de enige branch op GitHub
(`x589h29ftn-code/boekerhouden`). Ga daarop verder (clone of checkout van die branch).

Alle tests zijn groen op PostgreSQL en SQLite. De CI (GitHub Actions) bouwt bij elke push ook de Windows-app als
artefact `Sifer-win-x64`.

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
| Gepland (README §5) | Regels splitsen per leverancier, vreemde valuta, ontbrekende factuur |
| Stapel splitsen: vervolg | Pagina's blijvend draaien en verplaatsen (om en om gescande voor- en achterkanten), voortgang bij het uploaden; afstellen op een echte gescande stapel van de gebruiker |
| Bonnetjes: vervolg | HEIC, bijsnijden op het papier, meerdere foto's van één bon, eigen soort bon die direct tegen de pinbetaling of kas boekt; afstellen op echte foto's van de gebruiker |
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
