# Administratiepakket: ontwerp en roadmap

*Stand van zaken: 31 oktober 2026*

Naast de samenstel-app komt een **eigen administratiepakket**: een webapp waarin de administratie
van een ondernemer gevoerd wordt. Het doel is **zo veel mogelijk automatisch**: inkoopfacturen
inlezen en herkennen, voorstellen die leren van eerdere keuzes, slimme regels, verkoopfacturen die
vanzelf bij de debiteuren staan, en een bank die zelf aflettert. Wat overblijft, staat in een
korte werklijst. Aan het eind van het jaar gaat de saldibalans zonder export naar het
samenstelpakket.

## 1. Besluiten

| # | Onderwerp | Besluit |
|---|---|---|
| AD1 | Plaats | **Zelfde repository, eigen app**: `src/Boekerhouden.Boekhouding` (domein, geen I/O) en straks `src/Boekerhouden.Administratie` (webapp met een eigen database). RGS, bedragen en de saldibalans komen uit `Boekerhouden.Domain` |
| AD2 | Toegang | **Eerst op kantoor, later online.** Het kantoor boekt eerst zelf op de bestaande server-pc. Ondernemers krijgen toegang via internet zodra hosting, tweestapsverificatie en back-up rond zijn. Rechten zijn daarom vanaf het begin per administratie geregeld. Besluit A2 (alleen binnen kantoor) blijft gelden tot dat moment |
| AD3 | Herkenning (OCR) | **Stapsgewijs, met een uitwisselbare motor.** Eerst e-facturen (UBL/Peppol, exact en gratis) en PDF's met een tekstlaag, daarna Tesseract voor scans (open source, lokaal). Alles zit achter één interface (`IFactuurHerkenner`), zodat later eventueel een AI-model kan worden ingeplugd |
| AD4 | Rekeningschema | **RGS** ([§12 onderzoek](../onderzoek/12-rgs-als-rubriekenschema.md)): elke grootboekrekening heeft een RGS-code. Dan is de saldibalans meteen gerubriceerd voor de jaarrekening |
| AD5 | Bank | **Eerst bestanden**: MT940 en CAMT.053 (alle Nederlandse banken leveren beide). Een directe bankkoppeling (PSD2) kan alleen via een vergunninghouder en kost geld. Die komt later, achter dezelfde interface als de bestanden |
| AD6 | Leren | **Eerste keer kiest de gebruiker, tweede keer een voorstel, derde keer automatisch** (bij dezelfde keuze als de vorige twee keer). Een vaste regel van de gebruiker gaat altijd voor |
| AD7 | Beveiliging | Uitgewerkt in [beveiliging.md](beveiliging.md): wat er op het spel staat, wat gebruikelijk is en maatregelen per fase (kantoor / online). Besloten: TOTP én passkeys, standaard geen bedraglimiet (wel instelbaar), virusscan met ClamAV al op kantoor, voorbereiden op een mogelijke certificering, hosting in Nederland (partij nog te kiezen) |

## 2. Werkstroom

```
 Inkoop                     Verkoop                    Bank
 ──────                     ───────                    ────
 upload / e-mail / UBL      factuur maken              MT940 / CAMT.053 (later PSD2)
        │                         │                          │
 herkennen (UBL > tekst > OCR)    nummeren, PDF              mutaties inlezen, saldo controleren
        │                         │                          │
 leverancier herkennen            boeken: debiteuren,        afletteren op openstaande posten
 (btw-nr > IBAN > KvK > naam)     omzet, af te dragen btw    (kenmerk, factuurnummer, IBAN, bedrag)
        │                         │                          │
 regels > geleerd > niets         openstaande post           bankregels (rente, kosten, belasting)
        │                                                    │
 automatisch of in "Te verwerken"                            rest: werklijst
        │                                                    ├─ betaling zonder factuur → factuur uploaden
 boeken: kosten, voorbelasting,                              └─ factuur zonder betaling → herinneren/controleren
 crediteuren, openstaande post
                         ╲            │            ╱
                          journaal (inkoop, verkoop, bank, kas, memoriaal)
                                      │
            grootboekkaarten · proefbalans · balans · W&V · btw-aangifte
                                      │
                       saldibalans → samenstelpakket (jaarrekening)
```

## 3. Onderdelen

### 3.1 Grootboek en journaal

- **Rekeningschema** per administratie: nummer, naam, RGS-code en eventueel een **functie**
  (bank, kas, debiteuren, crediteuren, te vorderen btw, af te dragen btw, resultaat). Met de functie
  vinden de automatische boekingen de juiste rekening, zonder vaste nummers.
- **Dagboeken**: inkoop, verkoop, bank (één per bankrekening, met de grootboekrekening van de
  bank), kas en memoriaal.
- **Journaalpost**: dagboek, boekstuknummer, datum, omschrijving en regels (rekening, bedrag
  debet + / credit −, omschrijving, btw-code, relatie, factuurnummer). Een journaalpost is altijd in
  evenwicht, met bedragen in centen en minstens twee regels.
- **Memoriaal**: vrije journaalposten, bijvoorbeeld voor afschrijvingen, overlopende posten,
  correcties en de beginbalans.
- **Overzichten**:
  - **grootboekkaart** per rekening: beginsaldo, mutaties met lopend saldo, eindsaldo;
  - **proefbalans** per datum;
  - **balans en W&V** gegroepeerd op RGS-hoofdpost (niveau 2), met het resultaat;
  - **saldibalans** voor het samenstelpakket (rekening, naam, saldo, RGS-code).
- **Periodes**: boekjaar en maand. Een afgesloten periode (bijvoorbeeld na de btw-aangifte) kan niet meer
  bebouwd worden.

### 3.2 Btw

- **Btw-codes** met soort (hoog 21%, laag 9%, nul, vrijgesteld, verlegd, EU, buiten de EU, geen),
  richting (inkoop of verkoop) en **rubriek van de aangifte** (1a, 1b, 1e, 2a, 3b, 4a, 4b …).
- **Bij boeken**: voorbelasting of af te dragen btw op de btw-rekening. Bij verlegging en bij inkopen uit
  de EU gaan zowel de af te dragen btw als de voorbelasting op dezelfde post.
- **Btw-aangifte** per periode: per rubriek de omzet en de btw, controle op het saldo van de
  btw-rekeningen, en de afdrachtboeking naar de rekening *te betalen omzetbelasting*.
- **Btw-tabel** (gebouwd, `/beheer/btw`, alleen de beheerder): de btw-codes van het kantoor met hun **soort** en **tarief**.
  De soort (`Btwsoorten`) is de koppeling met de aangifte: in welke rubriek de grondslag en de btw komen en of de
  btw aftrekbaar is (5b):

  | Soort | Grondslag | Btw | 5b |
  |---|---|---|---|
  | Omzet hoog / laag | 1a / 1b | 1a / 1b | |
  | Omzet 0%, verlegd door u | 1e | | |
  | Export buiten de EU / levering binnen de EU (ICP) / afstandsverkopen | 3a / 3b / 3c | | |
  | Voorbelasting (inkoop hoog of laag) | | | ja |
  | Verlegd naar u | 2a | 2a | ja |
  | Import buiten de EU / inkoop binnen de EU (ICV) | 4a / 4b | 4a / 4b | ja |
  | Inkoop 0% of vrijgesteld, vrijgesteld, geen btw | | | |

  Het **tarief** (hoog, laag, nul, geen) krijgt zijn percentage uit de **tarieven door de tijd** (`Btwtarieven`): per tarief
  percentages met een ingangsdatum (hoog 19% → 21% per 1-10-2012, laag 6% → 9% per 1-1-2019). Een nieuw percentage
  (bijv. hoog 21,15% per een datum) voegt de beheerder toe; boeken gebeurt met het percentage op de factuurdatum
  (`Btwtabel.Op`), wat al geboekt is verandert niet. Een code is (in)actief te zetten; soort en tarief van een bestaande
  code liggen vast. Codes en tarieven staan in de database (`Btwcodes`, `Btwtarieven`), gevuld met de standaard.
- **Aangifte berekenen** (gebouwd in het domein, `Btwaangifte.Bereken`): uit de boekingen van een periode per rubriek de
  grondslag en de btw, de voorbelasting (5b) en het te betalen of terug te vragen bedrag. Het scherm volgt in A10.

### 3.3 Relaties en openstaande posten

- **Relatie**: naam, één of meer IBAN's, btw-nummer, KvK-nummer, adres, betalingstermijn en de
  **standaard tegenrekening** (wordt geleerd).
- **Openstaande post**: één per factuur, met het oorspronkelijke bedrag, het openstaande bedrag, het factuurnummer, het
  betalingskenmerk en de vervaldatum. Bij debiteuren is het bedrag positief, bij crediteuren
  negatief (de tekenconventie van het grootboek). Een afgeletterde betaling maakt het saldo nihil.
- **Ouderdomsanalyse** van de debiteuren en crediteuren (0–30, 31–60, 61–90, >90 dagen).

### 3.4 Inkoopfacturen: inlezen, herkennen, voorstellen

1. **Inlezen**: uploaden (slepen, meerdere tegelijk), later per e-mail (eigen adres per administratie)
   en via de telefoon (foto).
2. **Herkennen** (`IFactuurHerkenner`, AD3), in volgorde van betrouwbaarheid:
   1. **UBL/e-factuur** (los of als bijlage in de PDF): exacte gegevens, geen OCR nodig;
   2. **PDF met tekstlaag**: tekst eruit, dan tekstanalyse;
   3. **scan of foto**: Tesseract (Nederlands en Engels), dan dezelfde tekstanalyse. Een foto (JPEG, PNG) wordt
      bewaard als PDF met de foto als pagina; van een gescande PDF gaat per pagina de grootste afbeelding door de OCR.
      De woorden met hun plek (`DocumentInhoud.Tekstlaag`) maken aanwijzen, oplichten en het zoektemplate mogelijk,
      net als bij een PDF met tekst. Zonder Tesseract blijft een scan in Te verwerken met een melding.
   
   De **tekstanalyse** zoekt het factuurnummer, de factuur- en vervaldatum, IBAN, btw-nummer (met
   controle), KvK, het totaal, het btw-bedrag per tarief en het bedrag exclusief btw. Ze controleert ook of
   het geheel klopt: excl + btw = totaal, en btw = tarief × grondslag (met een afrondingsmarge). Staan er meer
   "totalen" op de factuur (bijv. een specificatie met een kolom *Totaal* op pagina 2), dan kiest ze de combinatie
   die sluit. Heeft maar één van de twee totalen een bekend label, dan zoekt ze het andere terug op de factuur:
   exclusief plus btw (of totaal min btw) moet er als bedrag op staan, en bij één tarief moet de btw dat percentage
   van het exclusieve bedrag zijn. Zonder bruikbaar label nemen ze twee bedragen die met de btw sluiten (het grootste
   totaal). Is er geen btw-regel herkend, dan geldt een drietal als exclusief, btw en totaal als het grootste bedrag
   min een ander bedrag een derde bedrag op de factuur is dat 21% of 9% daarvan is (200,00 + 42,00 = 242,00); niet
   bij vrijgesteld, KOR of verlegd. Drie bedragen die zo bij elkaar passen, zijn zelden toeval. De **naam** van de leverancier: de eerste naam met een rechtsvorm (niet de eigen naam, het klantblok,
   een regel boven *t.a.v.*, adressen, postbussen of contactgegevens); staat de naam alleen in het logo (een
   plaatje, zonder tekst), dan uit het domein van een e-mailadres of website op de factuur (`info@q4-adviseurs.nl`
   wordt *Q4 Adviseurs*; niet van maildiensten als gmail of van de eigen administratie). Na de eerste keer boeken
   herkent het pakket de leverancier aan IBAN of btw-nummer, dus de naam is maar één keer nodig.
3. **Leverancier herkennen**: eerst op btw-nummer, dan IBAN, dan KvK en ten slotte op naam (genormaliseerd,
   zonder "B.V." e.d.). Een onbekende leverancier wordt met de herkende gegevens als nieuwe relatie
   voorgesteld.
4. **Voorstel voor rekening en btw-code**, in deze volgorde:
   1. **Regels van de gebruiker** (§3.6), bijvoorbeeld: *KPN, totaal tussen € 10 en € 30 → telefoonkosten, 21%,
      automatisch*;
   2. **Geleerd** (AD6): de eerste keer kiest de gebruiker. De tweede keer volgt een voorstel. Is de
      keuze twee keer achter elkaar bevestigd (de drempel, per administratie in te stellen van 1 tot 10), dan
      verwerkt het pakket de derde factuur zelf. Bij elke bevestiging onthoudt het pakket ook het bedrag, het IBAN
      en de postcodes op de factuur. Zelf verwerken gebeurt alleen als de nieuwe factuur in dat patroon past:
      - een bedrag tot **1,25 keer** het hoogste bevestigde bedrag (bevestigd 30 en 45 → automatisch tot 56,25);
      - **hetzelfde IBAN**;
      - **geen nieuwe postcode** (een ander adres van de leverancier).

      Past de factuur niet, dan komt die ter controle in Te verwerken, met de reden. Na de bevestiging begint de
      telling opnieuw: pas na weer twee bevestigingen verwerkt het pakket zelf (bij het derde exemplaar). Zolang
      de drempel nog niet is gehaald, telt elk bedrag mee. Wijkt de gebruiker af in rekening of btw-code, dan
      begint de telling ook opnieuw. Staat er op de factuur een ander btw-tarief dan bij de geleerde keuze (9% in
      plaats van 21%), dan past het voorstel de btw-code aan en verwerkt het pakket de factuur niet zelf. Het
      pakket onthoudt ook de vorm van de omschrijving als die variabelen bevat (zie hieronder);
   3. **Niets**: de gebruiker kiest.
5. **Automatisch verwerken** gebeurt alleen als ook alles klopt: leverancier herkend,
   factuurnummer en datum gevonden, totalen sluitend, niet eerder geboekt (zelfde leverancier en
   factuurnummer, of zelfde bedrag en datum) en de periode open. Anders komt de factuur in **Te verwerken**, met
   de reden erbij.
6. **Boeken**: kosten (excl.) debet, voorbelasting debet en crediteuren credit, plus een openstaande post. Het
   document hoort bij de journaalpost en blijft altijd op te vragen.
7. **Omschrijving met variabelen** (`Omschrijvingssjabloon`): `#factuurnummer`, `#omschrijving`, `#leverancier`,
   `#datum`, `#vervaldatum`, `#maand`, `#kwartaal`, `#jaar` en `#kenmerk`. *Telefoon #maand #jaar
   (#factuurnummer)* wordt bij het boeken *Telefoon oktober 2026 (2026-0412)*. Een regel kan zo'n omschrijving
   hebben, en het leergeheugen onthoudt de laatst gebruikte.
8. **Bedragen controleren** (`Bedragcontrole`, naast de controle van de factuur zelf): de btw per regel bij het
   tarief (100,00 en 21% is 21,00; een cent per factuurregel mag door afronden), en de boeking samen tegen het
   totaal en de btw van de factuur (100,00 + 21,00 = 121,00). Bij een verschil toont het scherm de reden en heet
   de knop *Toch boeken*; zelf verwerken doet het pakket dan nooit.

### 3.5 Verkoopfacturen

- **Factuur maken**: klant, regels (omschrijving, aantal, prijs, btw-code, omzetrekening), korting,
  betalingstermijn, en een **doorlopende nummering** per jaar (bijv. `2026-0001`, zonder gaten).
- Bij **definitief maken**: een PDF in de huisstijl, de boeking (debiteuren debet, omzet en af te
  dragen btw credit) en een **openstaande post in de debiteurenlijst**. Daarna wijzigen kan
  alleen met een creditfactuur.
- **Betalingskenmerk** op de factuur. De klant betaalt met dat kenmerk, en het afletteren is dan 100% zeker.
- **Later**: verzenden per e-mail, UBL meesturen, herinneringen en terugkerende facturen
  (abonnementen).

### 3.6 Regels (automatisering)

Een **verwerkingsregel** heeft voorwaarden en een actie, en werkt voor inkoopfacturen én
bankmutaties:

| Voorwaarde | Voorbeeld |
|---|---|
| Relatie of naam bevat | `KPN` |
| Bedrag (totaal) van–tot | € 10 – € 30 |
| Omschrijving bevat | `rente`, `abonnement` |
| Tegenrekening (IBAN) | `NL..BELA..` (Belastingdienst) |
| Richting | betaling of ontvangst |

**Actie**: grootboekrekening, btw-code, omschrijving en **automatisch verwerken** ja/nee. Regels hebben
een volgorde: de eerste die past, wint. Kenmerkende bankregels worden standaard meegeleverd, zoals
*bankkosten → algemene kosten, geen btw*.

### 3.7 Bank

- **Inlezen**:
  - **MT940** (`:20:`, `:25:`, `:28C:`, `:60F:`, `:61:`, `:86:` en `:62F:`). De `:86:` verschilt per bank: gestructureerd met
    `/IBAN/`, `/NAME/`, `/REMI/` en `/EREF/` (ING, Rabobank, bunq e.a.), of vrije tekst met `IBAN:`, `NAAM:` en
    `OMSCHRIJVING:` (ABN AMRO), of vrije tekst zonder labels met IBAN en naam op de eerste regel en de omschrijving
    daaronder.
  - **CAMT.053** (XML, ISO 20022): per mutatie het bedrag, debet/credit, de datum, de tegenpartij met IBAN,
    de omschrijving (`Ustrd`), het gestructureerde betalingskenmerk en de end-to-end-id.
  - **Controle**: beginsaldo + mutaties = eindsaldo, en het beginsaldo sluit aan op het vorige afschrift. Een
    mutatie die al eerder is ingelezen, wordt niet nog eens geboekt.
- **Handmatig inboeken** van een mutatie: op een openstaande post, op een grootboekrekening (met btw-code) of
  gesplitst over meerdere regels.
- **Afletteren** (§3.8): wat zeker is, wordt automatisch geboekt. Wat waarschijnlijk is, staat klaar als
  voorstel, met één klik te bevestigen. De rest gaat naar de werklijst.
- **Werklijsten**:
  - **betalingen en ontvangsten zonder factuur**: per mutatie een knop *factuur uploaden*. Een factuur die later
    binnenkomt, wordt automatisch gekoppeld;
  - **openstaande posten zonder betaling**, op vervaldatum;
  - **niet-afgeletterde verschillen**, zoals deelbetalingen.

### 3.8 Afletteren

Per bankmutatie zoekt de afletteraar tussen de openstaande posten met **hetzelfde teken**: een ontvangst bij
debiteuren, een betaling bij crediteuren. Elke kandidaat krijgt een **zekerheid** en een **reden**:

| # | Kenmerk | Zekerheid |
|---|---|---|
| 1 | Gestructureerd **betalingskenmerk** gelijk | Zeker |
| 0 | IBAN bekend en de **som van enkele** open posten van die relatie (ook een factuur min een creditnota), en de omschrijving noemt elk nummer (of het laatste getal ervan: "Fact GB-2026-1041 1042 1043") | Zeker, in één keer over alle posten |
| 2 | **Factuurnummer** in de omschrijving, en het bedrag gelijk | Zeker |
| 3 | **IBAN** van de relatie bekend, het bedrag gelijk, en het is de enige open post van die relatie met dat bedrag (ook zonder factuurnummer) | Zeker |
| 4 | Factuurnummer of kenmerk en het IBAN van de relatie, en het bedrag is lager dan wat openstaat (**deelbetaling**) | Zeker; het restant blijft openstaan |
| 4b | Factuurnummer, iets lager (hoogstens 5%) en de omschrijving noemt een **korting** | Voorstel (*Afletteren…*: het verschil afboeken op een rekening met btw-code, bijv. 40,00 korting en 8,40 correctie voorbelasting) |
| 4c | Factuurnummer, maar het bedrag wijkt anders af | Voorstel |
| 4d | Het nummer of kenmerk van een factuur die **al betaald** is, met hetzelfde bedrag | Voorstel *dubbele betaling?*, nooit automatisch (ook niet op een andere factuur); afletteren zet het als te vorderen op die factuur |
| 5 | IBAN bekend, en het bedrag gelijk aan meerdere open posten | Voorstel (oudste eerst) |
| 6 | IBAN bekend, en het bedrag gelijk aan de **som van enkele** open posten (verzamelbetaling) | Voorstel |
| 7 | **Naam** van de tegenpartij lijkt op de relatienaam, en het bedrag gelijk | Voorstel |
| 8 | Het bedrag is gelijk en uniek over alle open posten | Voorstel |

- IBAN en bedrag zijn niet zeker als de omschrijving een ander nummer uit dezelfde reeks noemt (even lang, zelfde
  begin: "Betaling factuur 9100234603" bij open factuur 9100234601): dan een voorstel.
- **Zeker** wordt automatisch geboekt, maar alleen als er precies één zekere kandidaat is, en nooit als de mutatie
  naar een **ander IBAN** gaat dan de relatie bij ons heeft (gewijzigd rekeningnummer, mogelijk fraude): dat is
  hoogstens een voorstel.
- Te veel betaald blijft als te vorderen op de (laatste) factuur staan.
- **Zelf kiezen**: dubbelklik op de omschrijving van een mutatie (of *Zelf kiezen…*) opent een venster met alle
  openstaande posten (die van de tegenpartij bovenaan): zoeken op leverancier, factuurnummer of bedrag, een of meer
  posten aanvinken (ook een creditnota), het verschil met de mutatie zien en open laten of afboeken op een rekening
  met btw-code (`OpenpostenVenster`, `BankDienst.Afletteren` met `Verschilafboeking`, `Bankboeker.Afletteren` met
  `Afboeking`).
- Bij een **voorstel** bevestigt de gebruiker. Bevestigt hij, dan leert het pakket het IBAN bij de relatie, zodat
  het de volgende keer *zeker* is.
- Een factuurnummer wordt herkend zonder leestekens of hoofdlettergebruik (`2026-001` = `2026001`), en los
  geschreven met dezelfde groepen in dezelfde volgorde (`inv 2026 87` = `INV-2026-0087`, bij minstens drie groepen).
  Een kort nummer (minder dan vier tekens) moet als los woord in de omschrijving staan.
- Een bankmutatie zonder tegenpartij (bankkosten, pinbetaling) leert het pakket op de woorden van de omschrijving,
  zonder getallen en maanden ("Kosten betaling buitenland CT-884200" = "… DS-5510"); na twee keer dezelfde rekening
  gaat hij vanzelf (ook met *Opnieuw herkennen*).
- Gemeten met een set van 100 facturen en een afschrift van twee maanden (batch 3, verzonnen, niet in git): herkenning
  100% op leverancier, nummer, datum en bedragen; 21 facturen automatisch geboekt, geen fout; 95 van de 98
  factuurbetalingen automatisch afgeletterd, 3 voorstellen (te veel betaald); crediteuren per factuur 100/100 en de
  bank sluit.
- Getest met twee praktijksets (oktober 2026, verzonnen): `BankpraktijkTests` (deelbetaling in twee delen, dubbele
  betaling en terugbetaling; kenmerk, factuur min creditnota, sombetaling, betalingskorting en een ander IBAN).

### 3.9 Rechten en gebruikers

- Een gebruiker heeft per administratie de rol **inzien** (alleen overzichten), **ondernemer** (inlezen,
  factureren, eigen overzichten) of **kantoor** (alles, ook memoriaal, rekeningschema, periodes en regels).
- In het pakket zelf zijn er twee rollen: **kantoor** (mag administraties aanmaken en wordt daar lid met de rol
  kantoor) en **beheerder** (gebruikers en leden, wijzigingslog; ziet géén cijfers zonder lidmaatschap).
- Elke handeling die gegevens wijzigt, staat in het **wijzigingslog** (wie, wat, onderwerp), zoals in de
  samenstel-app.

### 3.10 Koppeling met het samenstelpakket

Aan het eind van het jaar staat de saldibalans **met RGS-codes** in de administratie. Het
samenstelpakket haalt die op als import, zonder sjabloon. De codes zijn hetzelfde, dus het rubriceren is al
gedaan.

## 4. Techniek

- **Zelfde stack** als de samenstel-app ([§7 onderzoek](../onderzoek/07-technische-architectuur.md)): .NET 10, Blazor
  Server, PostgreSQL (een **eigen database** `boekerhouden_administratie`), Identity en het wijzigingslog.
- **Domein zonder I/O** in `Boekerhouden.Boekhouding`, met namespaces `Grootboek`, `Btw`, `Relaties`,
  `Facturen`, `Bank`, `Afletteren`, `Regels` en `Herkenning`. Bankbestanden en UBL worden gelezen uit een `TextReader`/`Stream`.
- **Pakketten** (alle open source, licentie gecontroleerd vóór het toevoegen):

  | Doel | Pakket | Licentie |
  |---|---|---|
  | Tekst uit PDF | PdfPig | Apache-2.0 |
  | OCR | Tesseract (via `TesseractOCR` of de opdrachtregel) | Apache-2.0 |
  | PDF van een verkoopfactuur | de bestaande `PdfDienst` (Chromium) | – |

- **Bedragen**: `decimal`, exact in centen, en ook zo getoond (een administratie is exact; de jaarrekening in
  de samenstel-app toont hele euro's). Btw wordt per regel berekend en per tarief opgeteld, met afronding
  *away from zero*.
- **Huisstijl**: Bootstrap, `app.css` en het logo staan in de bibliotheek `Boekerhouden.Huisstijl` en worden door
  beide apps gedeeld (`_content/Boekerhouden.Huisstijl/...`).

## 5. Roadmap in plakjes

| # | Plakje | Resultaat |
|---|---|---|
| A1 ✅ | **Grootboek en journaal** (domein): rekeningschema met RGS en functies, dagboeken, journaalpost met controles, grootboekkaart, proefbalans, balans en W&V per RGS-hoofdpost, saldibalans; btw-codes en het boeken van inkoop- en verkoopfacturen met openstaande posten | Kern van de boekhouding, volledig getest |
| A2 ✅ | **Bankbestanden** (domein): MT940 (gestructureerd en vrije tekst) en CAMT.053, met saldocontrole | Afschriften van alle gangbare banken inlezen |
| A3 ✅ | **Afletteren** (domein): kandidaten met zekerheid en reden (§3.8), verzamelbetalingen | Slimme aflettering, ook zonder factuurnummer |
| A4 ✅ | **Regels en leren** (domein): verwerkingsregels, leergeheugen (1-2-3), advies voor factuur en bankmutatie | *KPN € 10–30 → telefoonkosten*, en zelf leren |
| A5 ✅ | **Herkenning** (domein): tekstanalyse van een factuur, UBL lezen, leverancier herkennen | Gegevens uit e-facturen en PDF-tekst |
| A6 ✅ | **Webapp I**: project `Boekerhouden.Administratie`, database, inloggen met tweestapsverificatie (app en passkeys), administraties met rechten, standaardrekeningschema (RGS), memoriaal, journaal met hash-keten, grootboekkaart, proefbalans, balans en W&V, gebruikers en leden, wijzigingslog | Werkende boekhouding in de browser |
| A7 ✅ | **Inkoop in de app**: uploaden, herkennen (UBL, PdfPig), Te verwerken, voorstel en automatisch boeken, regels beheren | Inkoopfacturen verwerken |
| A8 ✅ | **Bank in de app**: inlezen, afletteren (automatisch en met voorstellen), handmatig boeken, werklijsten met *factuur uploaden* | Bank bijna vanzelf bij |
| A9 (deels) | **Verkoop**: factuur maken, nummeren, PDF, debiteurenlijst, ouderdomsanalyse; lay-out, terugkerende facturen en inlezen uit een ander pakket | Factureren |
| A10 | **Btw-aangifte en periodes**: aangifte per rubriek, afdrachtboeking, periodes afsluiten | Aangifte klaarzetten |
| A11 ✅ | **Scans**: Tesseract voor scans en foto's (`TesseractOcr`, `OcrLezer`); in de Windows-app als Tesseract op de pc staat | Ook papieren bonnen |
| A12 | **Saldibalans naar het samenstelpakket** | Jaarrekening zonder export |
| gepland | **Regels splitsen per leverancier** (wens gebruiker, oktober 2026): de factuurregels uitlezen en per leverancier een vaste verdeling over rekeningen leren (energie: levering en netbeheer; telefoon: abonnement en toestel; groothandel: 9% en 21%), en die de volgende keer voorstellen | Ook gesplitste facturen vanzelf |
| gepland | **Verzamel-PDF splitsen** (wens gebruiker, oktober 2026): een PDF met meerdere facturen (één per pagina of meer) bij het inlezen opdelen in losse documenten, per factuurnummer of leverancier, met een controle-overzicht voor het splitsen | Een stapel facturen in één keer uploaden |
| gepland | **Vreemde valuta** (batch 2, scenario 8): factuur in USD e.d. op de koers van de factuurdatum, betaling op de werkelijke koers, het verschil naar koersverschillen; bankkosten apart | Buitenlandse leveranciers zonder handwerk |
| gepland | **Ontbrekende factuur** (batch 2, scenario 16 en 18): een betaling zonder factuur (huur, pinbon) markeren als *factuur ontbreekt*, tijdelijk op een tussenrekening, en de vaste last later automatisch | Vaste lasten en bonnetjes netjes opvolgen |
| later | Online voor ondernemers (AD2), facturen per e-mail, directe bankkoppeling (PSD2), herinneringen, terugkerende facturen, kas, AI-herkenning als extra motor, e-facturen verzenden (Peppol) | |

### Stand na A1–A7

De boekhoudkern staat in `src/Boekerhouden.Boekhouding`, met tests in `tests/Boekerhouden.Boekhouding.Tests` en
verzonnen voorbeeldbestanden in `tests/Fictief/administratie`:

| Onderdeel | Klassen |
|---|---|
| Grootboek | `Rekeningschema`, `Journaal` (boeken met controles, `Kaart`, `Proefbalans`), `Jaaroverzicht`, `Proefbalans.NaarSaldibalans` |
| Btw | `BtwCodes` (definities en standaard), `Btwsoorten` (koppeling met de aangifte), `Btwtarieven` en `Btwtabel` (percentages per datum), `BtwBerekening`, `Btwaangifte` |
| Facturen | `Factuurboeker` (factuur → journaalpost + openstaande post) |
| Bank | `Bankbestand` (herkent MT940 of CAMT.053), `Mt940Lezer`, `Camt053Lezer`, `Bankboeker` (afletteren, op rekening), `Bankverwerker` (afletteren en regels/leergeheugen samen: wat automatisch mag) |
| Afletteren | `Afletteraar` (kandidaten met zekerheid en reden), `Factuurnummers` |
| Regels | `Verwerkingsregel`, `Leergeheugen`, `Verwerkingsadviseur` |
| Herkenning | `Factuurtekst` (tekstanalyse), `UblFactuurLezer`, `Leverancierherkenning`, `Inkoopverwerker` (herkende factuur → voorstel met belemmeringen), `IFactuurHerkenner` |
| Documenten (`src/Boekerhouden.Documenten`) | `PdfTekst` (tekst uit een PDF met PdfPig, regels op hun plek, begrensd), `PdfFactuurHerkenner` (UBL-bijlage of tekst), `UblHerkenner`, `Factuurherkenning` (eerst de virusscan, dan de motoren op volgorde), `ClamAvScanner` |

Proberen op een eigen factuur (die blijft buiten git, bijv. in `testdata/`):

```bash
dotnet run --project src/Boekerhouden.Cli -- herken testdata/factuur.pdf --eigen-naam "Mijn Bedrijf B.V." --leer 4500 I21
```

Dat toont wat het pakket herkent, het inkoopvoorstel in een lege administratie, en daarna drie facturen van dezelfde
leverancier: kiezen, voorstel, automatisch, met de journaalpost.

#### De webapp (plakje A6)

```bash
dotnet run --project src/Boekerhouden.Administratie     # http://localhost:5090, eigen database boekerhouden_administratie
```

De eerste bezoeker maakt het eerste account (beheerder én kantoor) en stelt daarna eerst tweestapsverificatie in.
Daarna: *Nieuwe administratie* (krijgt het standaardrekeningschema en de dagboeken), boeken in het memoriaal, en de
overzichten.

| Onderdeel | Hoe |
|---|---|
| Toegang | `Toegang.Open(wie, administratie, recht)` is de enige weg naar de gegevens: lidmaatschap en recht worden gecontroleerd, en de `AdministratieDbContext` laat daarna alleen die administratie zien (query filter) en slaat alleen daarin op. Zonder lidmaatschap: "bestaat niet of geen toegang" |
| Boeken | `GrootboekDienst.Boek`: controles uit het domein, één boeking tegelijk per administratie (rij-lock), volgnummer en SHA-256-keten (`Journaalketen`); triggers in de database weigeren wijzigen en verwijderen van boekingen en het wijzigingslog |
| Overzichten | balans en W&V per RGS-hoofdpost (`RgsHoofdposten`, zonder werkboek), proefbalans, grootboekkaart, journaal met ketencontrole |
| Inloggen | wachtwoord en daarna de code uit de authenticator-app (of een herstelcode), of een passkey; voor de beheerder is tweestapsverificatie verplicht (`TweestapVerplicht`) |
| Schermen | `/` administraties; in een administratie een menu horizontaal boven de inhoud (de gegevens krijgen de volle breedte), in groepen waarvan de onderdelen verschijnen bij aanwijzen of aanklikken: **Boekhouden** (`/inkoop`, `/verkoop`, `/bank`, `/memoriaal`), **Dagboeken** (`/inkoopboek`, `/verkoopboek`, `/journaal`), **Rapportages** (`/a/{id}` balans en W&V, `/kolommenbalans`, `/debiteuren`, `/crediteuren`, `/proefbalans`, `/grootboek` met periode) en **Inrichting** (`/relaties`, `/regels`, `/rekeningschema`, `/instellingen`); `/account/beveiliging`; beheer: `/beheer/gebruikers`, `/beheer/instellingen`, `/beheer/btw`, `/beheer/wijzigingslog` |
| Kolommenbalans | `/a/{id}/kolommenbalans`: per rekening het saldo in de W&V (debet, credit) of op de balans (debet, credit), onderaan *Totaal*, *Resultaat* en *Totaal generaal*. Bovenin de periode (van, tot en met; ‹ › schuift een periode van dezelfde lengte, `Periode.Schuif`): alleen boekingen in die periode tellen, balansrekeningen met hun beginstand (`Journaal.Kolommenbalans(van, tot)`). Een regel aanklikken opent de grootboekkaart over dezelfde periode (per mutatie ook de btw-code en de btw uit dezelfde boeking, naar verhouding als meer regels die code hebben: `Kaartregel.BtwCode` en `Btw`; met ‹ › naast de rekening naar de vorige of volgende rekening met mutaties in de periode; kolommen breder of smaller slepen aan de rand van de kolomkop, bewaard in deze browser, dubbelklik zet ze terug: `kolommen.js`); een mutatie daar opent de boeking (`/a/{id}/boeking/{dagboek}/{boekstuk}`) met de factuur ernaast (`Factuurbeeld`, alleen bekijken; *Factuur* opent het verwerkscherm) |
| Datums | Overal dd-mm-jjjj (`DatumVeld`), los van de taal van de browser of Windows: typen mag kort (30-10, 301026, 30/10/26; `Weergave.LeesDatum`), de kalenderknop opent de kalender van de browser; een ongeldige datum blijft staan en wordt rustig gemarkeerd |
| Openstaande crediteuren | Rapportages → *Openstaande crediteuren* (een post opent de factuur of de boeking met een pijl linksboven terug naar de lijst op dezelfde peildatum) (`/a/{id}/crediteuren?peildatum=…`, `OpenpostenDienst.Crediteuren`): per leverancier (code en naam) de posten die op de peildatum openstonden, met boekdatum, vervaldatum, omschrijving, factuurnummer, factuurbedrag, betaald, openstaand en ouderdom in dagen, een totaal per leverancier en het totaal generaal; te beperken tot één leverancier. Wat op de peildatum openstond, volgt uit het journaal: de facturen tot en met de peildatum min de betalingen tot en met die datum (regels op crediteuren met dezelfde leverancier en hetzelfde factuurnummer); een verrekening met een creditnota heeft geen eigen datum en telt altijd mee. Een betaling of correctie die niet via afletteren liep (bijv. in een memoriaal op crediteuren, met leverancier en factuurnummer) telt ook mee en werkt bij het boeken de openstaande post bij, net als de bank (`GrootboekDienst.Boek`); te veel betaald staat negatief open. Andere regels op crediteuren (zonder of met een onbekend factuurnummer, een vooruitbetaling, een beginbalans zonder leverancier onder *Zonder leverancier*) staan los in de lijst, zodat het totaal sluit op het saldo crediteuren in het grootboek. Getest in `CrediteurenTests` (deelbetalingen per peildatum, te veel betaald, creditnota open en verrekend, verrekend en het restant in het memoriaal betaald, correcties die de schuld verhogen of verlagen, een verzamelbetaling voor meer leveranciers in een eigen memoriaal, een aangepaste memoriaalboeking, losse regels, facturen met meer tarieven, verlegd, binnen de EU, korting, eigen btw en periodeverdeling, de jaarovergang, hetzelfde nummer bij twee leveranciers, aangepaste en teruggedraaide facturen, 60 facturen; steeds sluitend op het grootboek) en `BankTests` (te veel betaald via de bank: te vorderen op de factuur). De PDF heeft de huisstijl van het pakket (lettertype, kleuren, kolomkoppen). Een post aanklikken opent de bron: de factuur met de boeking, of de boeking. *PDF* maakt een A4 (liggend) in dezelfde opmaak (`OpenpostenHtml`, `PdfDienst` met Edge/Chromium zonder venster, instellingen in sectie `Pdf`) en slaat die op via de browser (`download.js`, ook in de Windows-app) |
| Kolommenbalans: presentatie | Bovenin twee schakelaars (in het adres, dus te bewaren als link: `indeling=rgs`, `weergave=maand`). **Grootboek / Uitgebreid**: de rekeningen onder elkaar, of ingedeeld naar RGS (`Rekeningindeling`, op de hoofdpost waarmee de RGS-code begint, `RgsHoofdposten`): activa (vaste activa: immateriële en materiële; vlottende activa: voorraden, vorderingen, liquide middelen), passiva (eigen vermogen, lang- en kortlopende schulden) en de W&V (omzet, kostprijs, personeel, afschrijvingen, overige bedrijfskosten, financiële baten en lasten, belastingen), met een subtotaal onder elke groep, tussenkop en sectie; een rekening zonder (bekende) RGS-code staat onder *Niet ingedeeld*. De koppeling is de RGS-code van de rekening: op het rekeningschema een rekening aanklikken (recht Boekhouden, `GrootboekDienst.WijzigRekening`) om naam, btw-code en RGS-code te wijzigen; de indeling verschijnt er meteen naast. **Totaal / Per maand**: het totaal over de periode, of per rekening de beginstand, de mutaties per maand naast elkaar en het eind (`Journaal.Maandoverzicht`, hoogstens 24 maanden; onderaan het resultaat per maand). Een maandbedrag aanklikken opent de grootboekkaart van die rekening over die maand (bijv. kantoorbenodigdheden april 2026); daar is de periode weer te wijzigen. De overige bedrijfskosten staan per soort (RGS niveau 3: huisvestings-, exploitatie-, verkoop-, auto-, kantoor-, assurantie-, accountants- en algemene kosten) onder de tussenkop *Overige bedrijfskosten*. Per maand heten de kolommen *Beginbalans* en *Eindbalans*. Onder Beheer, Kantoorinstellingen, *Overzichten*: lettergrootte en afronden op hele euro's (1.200 in plaats van 1.200,45, zonder euroteken; `KantoorDienst.WijzigWeergave`); afgerond wordt per kolom met `Afronding.OpHeleEuros`, zodat subtotalen en totalen sluiten. Alle overzichtstabellen zijn compact (weinig ruimte per regel) |
| Memoriaal | `/a/{id}/memoriaal` (recht Boekhouden): een tab per memoriaal; *+ Dagboek* voegt er een toe met code en naam, bijv. MLON *Memoriaal lonen* of MACC *Memoriaal accountant* (`GrootboekDienst.VoegMemoriaalToe`; elk memoriaal nummert zelf, `2026-0001`). Bovenaan het formulier voor een nieuwe boeking, daaronder de boekingen van dat memoriaal (datum, boekstuk, omschrijving, bedrag). Een boeking aanklikken zet hem meteen in het formulier om aan te passen; *Opslaan* maakt een nieuwe versie (zie *Boeking aanpassen*). Hetzelfde formulier (`BoekingBewerken`) staat op de boekingpagina. Een periodeverdeling van een factuur komt in het standaardmemoriaal (MEM) |
| Boeking aanpassen | Op de boeking *Aanpassen*: datum, omschrijving en regels (rekening, omschrijving, btw-code, debet, credit) wijzigen, regels erbij of eraf, dan *Opslaan*. Het is dezelfde boeking (zelfde dagboek en boekstuk) met een nieuwe **versie** (`GrootboekDienst.PasAan` en `NieuweVersie`, `Boekingscorrectie`): de nieuwe versie komt achteraan in de keten en verwijst naar de vorige (`Boeking.Versie`, `Boeking.Vervangt`). Alleen de laatste versie telt in journaal, grootboekkaart, proef- en kolommenbalans, inkoopboek en bank (`AdministratieDbContext.Geldend`, `GeldendeRegels`); de vorige versies blijven bewaard en de database weigert nog steeds elke wijziging of verwijdering van een boeking, zodat de ketencontrole blijft werken. Op de boeking staat "versie 2, aangepast door …"; *Eerdere versies* (schakelaar) toont ze. Regels op bank, debiteuren en crediteuren horen bij het afschrift of de openstaande posten en blijven gelijk, en dan ook de datum. Een boeking bij een factuur (of de periodeverdeling ervan) pas je aan via de factuur: *Aanpassen* gaat daar meteen naartoe. Rechten als bij boeken (memoriaal: Boekhouden); in het log |
| Grootboekkaart: zoeken en herboeken | Boven de kaart een zoekveld: omschrijving, relatie, factuurnummer, boekstuk of bedrag (debet, credit of btw, ook zonder minteken); onderaan het aantal gevonden regels met hun debet en credit. Met recht Boekhouden een vinkje per regel (en *alles*): de gekozen regels in één keer **herboeken** naar een andere rekening (`GrootboekDienst.Herboek`): per boeking een nieuwe versie waarin alleen die regels een andere rekening krijgen (bedrag, btw-code, relatie en omschrijving blijven), in het log. Niet vanaf of naar bank, debiteuren, crediteuren of de btw-rekeningen: die horen bij het afschrift, de openstaande posten of de aangifte. Ook niet bij een teruggedraaide boeking of een tegenboeking (die moeten elkaar blijven opheffen) en bij een periodeverdeling (via de factuur); een gewone inkoopfactuur wel: de factuur toont daarna de nieuwe rekening |
| Excel en PDF | Op de grootboekkaart en de kolommenbalans de knoppen *Excel* en *PDF* (A4 liggend; `OverzichtDienst`, `OverzichtExport`): de kaart met beginsaldo, mutaties (met btw), lopend saldo en eindsaldo; de kolommenbalans met *Totaal*, *Resultaat* en *Totaal generaal*. Excel met echte getallen (om mee te rekenen), PDF met dezelfde stijl als de openstaande posten (`Exportstijl`) |
| Kopiëren en herhalen | Op een memoriaalboeking *Kopiëren* (ook in het memoriaal bij een gekozen boeking): een nieuwe boeking met dezelfde regels, een maand later, met een nieuw boekstuknummer. Bij een nieuwe boeking de schakelaar *elke maand t/m* (maand kiezen): de boeking plus elke maand een kopie op dezelfde dag (of de laatste dag van een kortere maand), elk met een eigen boekstuk (`GrootboekDienst.BoekPerMaand`, hoogstens 24), bijv. huur of afschrijving |
| Aansluitingen | `/a/{id}/aansluitingen` onder Rapportages (`AansluitingDienst`): elke bank op het eindsaldo van het laatste ingelezen afschrift (het grootboek tot en met die datum; met het aantal mutaties dat nog te verwerken is), crediteuren en debiteuren op de openstaande posten, de vraagposten (horen 0 te zijn) en het saldo van voorbelasting en af te dragen btw. Een regel die niet sluit krijgt "sluit niet"; aanklikken opent de bank, de openstaande crediteuren of de grootboekkaart. Een bank sluit pas als ook de beginbalans geboekt is |
| Sorteren en kolommen | In de lijsten (journaal, memoriaal, grootboekkaart, inkoop, bank, openstaande posten, relaties) een kolomkop aanklikken sorteert, nog eens klikken keert om (`Sortering`, `SorteerKop`). In alle overzichtstabellen zijn de kolommen breder of smaller te slepen aan de rand van de kop; bewaard per tabel in deze browser, dubbelklik zet ze terug (`kolommen.js`, `data-sleepbaar`) |
| Uitstraling | Gedeeld in de huisstijl (`uitstraling.css`, na Bootstrap en de css van de app): lettertype van het systeem (Segoe UI Variable op Windows, het Apple-lettertype op de Mac; IBM Plex Sans lokaal meegeleverd als terugval, OFL), een eigen accentkleur (petrol), kolomkoppen en kopjes in gewone letters, dunne lijnen. Iconen uit één lijnset (Lucide, ISC; `iconen.svg`, component `Icoon`; een test controleert dat elk gebruikt icoon erin staat). Bewegingen kort en functioneel: een pagina komt rustig binnen (vervagen en 4 px omhoog), een verwerkte bankmutatie licht op en schuift weg waarna de volgende klaarstaat, het sorteerpijltje draait, knoppen geven een kleine indruk en tonen een draaiend bolletje zolang ze bezig zijn, laadschetsen in plaats van "Laden…" en lege lijsten met een klein icoon. Met "minder beweging" in het besturingssysteem vallen de animaties weg. Boekhoudkundig: bedragen met een echt minteken (`Weergave.Bedrag`, −199,65), rekeningnummers als gewone grijze cijfers, een lijn boven de totalen en het eindtotaal dubbel onderstreept, een haarlijn tussen bedragkolommen; lijsten als strook zonder kaart, kleine hoeken. Elke pagina kan onder de titel een regel met kenmerken tonen (`Kenmerken`): bij de bank per rekening het IBAN, het laatste afschrift, het saldo en wat te verwerken is (`BankDienst.Stand`), bij de grootboekkaart periode, begin, aantal mutaties en eind, bij de inkoop het aantal te verwerken en het oudste. Inlezen bij bank en inkoop met een knop in de kop; slepen kan op de hele pagina (`Sleepvlak Pagina`, er verschijnt een rand "Loslaten om in te lezen"). Iconen alleen op knoppen zonder tekst; het menu alleen tekst. Kopbalk met het woordmerk "Boekerhouden administratie" en rechts de naam van de gebruiker; onderaan een statusbalk met administratie, rechtsvorm, gebruiker, rol en datum. Vinkjes in plaats van schuifschakelaars; invoer in regeltabellen (bank, memoriaal, factuur) als lichte velden met alleen een onderlijn. Tijdstippen als "vandaag 18:32" of "gisteren 09:05" (`Weergave.Moment`, de volledige datum in de tooltip); bevestigingen met wat er gebeurde, bijv. "MEM 2026-0002 geboekt · € 250,00 · 4300 Kantoorbenodigdheden, 1100 Bank" |

#### Inkoop (plakje A7)

| Onderdeel | Hoe |
|---|---|
| Inlezen | `/a/{id}/inkoop`: PDF's en UBL-bestanden op het sleepvlak slepen of aanklikken (`Sleepvlak`, ook bij de bank; tot 250 tegelijk). De pagina is alleen voor uploaden en de werkvoorraad: *Te verwerken* links, het sleepvlak rechts; na het inlezen staan alleen problemen (quarantaine, niet gelukt) onder het sleepvlak; geboekte facturen staan onder **Dagboeken → Inkoop** (`/a/{id}/inkoopboek`): zoeken op tekst (leverancier, factuurnummer, boekstuk, omschrijving; alle woorden), leverancier, bedrag inclusief btw van–tot (een creditnota zonder minteken) en *alleen open*, over een periode of *alle jaren* (`Inkoopboekfilter`); een regel opent de factuur met de boeking; een kolomkop aanklikken sorteert (tekst A–Z, bedragen hoog–laag, nog eens klikken keert om). Eerst de virusscan (ClamAV via `Virusscan:Adres`; zonder scanner buiten Development *fail closed*), dan herkennen; een besmet bestand gaat in quarantaine. Het bestand wordt op SHA-256 bewaard; hetzelfde bestand nog eens inlezen geeft een melding |
| Herkenning | `Factuurtekst` zoekt de leverancier per kolom (een regel met grote tussenruimte, of met "·" of "|" zoals in een voettekst). De eigen naam van de administratie (de klant op de factuur, ook met "Klant:", "Bill to:", "t.a.v." of in een andere schrijfwijze), het klantblok en adresregels tellen niet; eerst een naam met rechtsvorm bovenaan, anders in de rest van de factuur (een leverancier die alleen in de voettekst staat), anders de eerste naam bovenaan. Eigen IBAN's en btw-nummers worden overgeslagen. Btw-nummers uit de EU na "VAT", "BTW", "TVA", "USt-IdNr." e.d.; Engelse datums ("May 1, 2026"). Een btw-tabel ("Btw-percentage · Grondslag · Btw" met regels als "21% 1.035,00 217,35") wordt gelezen. Staat er "vrijgesteld van btw", "kleineondernemersregeling" of "VAT exempt" en geen btw, dan is het 0% (I0) over het hele bedrag, in plaats van 21% eruit te rekenen. Zonder kopregel "Omschrijving" is de omschrijving de eerste regel met een bedrag na nummer en datum. "Btw verlegd", "reverse charge", artikel 196 e.d. (of in een e-factuur btw-categorie AE of K) maakt de factuur *verlegd*: de btw-code wordt IVL (Nederland), IEU (EU) of IEX (buiten de EU), naar het land van het btw-nummer of anders van het IBAN (`Inkoopverwerker.VerlegdeCode`); het formulier rekent dan geen btw-bedrag (dat berekent het pakket bij het boeken) |
| Stresstest | `InkoopStressTests`, alleen met `BOEKERHOUDEN_STRESS=1` (30 facturen per leverancier) of een aantal, bijv. `BOEKERHOUDEN_STRESS=100`: tien leveranciers in bulk inlezen, per leverancier twee zelf boeken, de rest met *Opnieuw herkennen*, de overzichten en de keten; en vier gebruikers die tegelijk inlezen en automatisch boeken. Gemeten (oktober 2026, cloudcontainer): 1000 facturen inlezen ca. 19 ms per stuk (p95 37 ms), opnieuw herkennen 980 facturen in 40 s, proefbalans, kolommenbalans, grootboekkaart en ketencontrole met 1000 boekingen elk onder 0,1 s; tegelijk inlezen zonder fouten en met een kloppende keten. In de browser: 150 PDF's in één keer uploaden in 5 s, het verwerkscherm met 150 in de werkvoorraad opent in 0,9 s, boeken met Ctrl+Enter en door naar de volgende 0,5–1 s; geheugen van de server 236 → 309 MB. *Te verwerken* toont altijd alle documenten (van de rest de laatste 100; alles staat in het inkoopboek); *Opnieuw herkennen* toont de stand ("Bezig: 120 van 980") |
| Stresstest administraties | `AdministratiesStressTests`, alleen met `BOEKERHOUDEN_STRESS_ADMINISTRATIES=<aantal>` (en `BOEKERHOUDEN_STRESS_JAREN`, standaard 5): per administratie per maand 25 inkoopfacturen met openstaande post, een bankafschrift met betalingen, 25 ontvangsten en memoriaal; daarna de schermen van de eerste, middelste en laatste administratie. Gemeten (oktober 2026, cloudcontainer, PostgreSQL): 300 administraties × 5 jaar = 1,38 miljoen boekingen, 1,7 GB; lijst en zoeken in de administraties 58 en 12 ms; per administratie proefbalans, balans en W&V, kolommenbalans, grootboekkaart en journaal van een jaar 30–100 ms, ketencontrole 160–250 ms, inkoopboek en bankmutaties onder 25 ms, nieuwe factuur inlezen 30 ms. Proefbalans, kolommenbalans en grootboekkaart laden alleen de boekingen van de periode; wat daarvoor ligt telt de database op tot een beginstand (vroeger alle jaren: 150 ms en groeiend). Het journaal toont standaard het laatste boekjaar |
| Reeks | Getest met vijf PDF's van één leverancier (`InkoopReeksTests`): met de standaarddrempel (2) gaan na twee keer zelf boeken de andere drie vanzelf (ook met *Opnieuw herkennen* na een bulkupload), met drempel 3 de vierde en vijfde; een veel hoger bedrag, een ander IBAN of een ander adres komt ter controle terug, en na die bevestiging begint de telling opnieuw. Een opnieuw gescande factuur (zelfde nummer) is dubbel |
| Voorstel | `InkoopDienst` maakt het voorstel met `Inkoopverwerker`: regel > geleerd > relatie, met de signalen (afwijkend IBAN, afwijkend bedrag, dubbel, onbekende leverancier, boven de limiet). Dubbel is hetzelfde factuurnummer, of dezelfde datum en hetzelfde bedrag; de tweede controle meldt hetzelfde bedrag binnen zes dagen met een ander nummer (een herinnering of kopie?). Staat er HERINNERING of AANMANING op (of "nog niet voldaan"), dan heet het *Herinnering van factuur … die is al geboekt (en betaald). Niet nog eens boeken.* Een factuur in vreemde valuta (USD e.d. bij het totaal, of `DocumentCurrencyCode` in UBL) gaat nooit automatisch: *vul de bedragen in euro's in (koers van de factuurdatum)*. De herkenning leest ook "Nummer:" als factuurnummer en "Betaalkenmerk" als betalingskenmerk; een kolomkop als "BTW  Bedrag excl." is geen btw-nummer. Een maand- of weekfactuur valt daarbuiten. Zonder signaal en met een automatisch advies boekt het pakket zelf (`AutomatischGeboekt` in het log) |
| Openstaande posten extern | Knop *Openstaande posten ⧉* bij het bankboek: de openstaande posten (crediteuren en debiteuren) in een eigen venster, bijv. op een tweede beeldscherm, net als de factuurviewer: zoeken op leverancier, factuurnummer of bedrag, crediteuren of debiteuren, en *Verversen* om bij te werken na het afletteren (`OpenpostenExtern`; in de webapp een pop-up `/a/{id}/bank/openstaand` zonder kopbalk, in de Windows-app een tweede venster via `IDocumentvenster.OpenOpenposten`, één tegelijk) |
| Opnieuw herkennen | Knop bij *Te verwerken* (`InkoopDienst.VerwerkOpnieuw`): het pakket gaat opnieuw door alle facturen in Te verwerken, met wat het nu weet (nieuwe relaties, regels, wat er intussen geleerd is, het zoektemplate). Wat nu automatisch kan, wordt geboekt, de oudste eerst; bij de rest komt de actuele reden. Zo gaan na een bulkupload en een paar keer zelf boeken de andere facturen van die leverancier vanzelf |
| Te verwerken | `/a/{id}/inkoop/{document}`: het formulier links en de factuur rechts in de viewer; de scheiding ertussen is te verslepen (breedte onthouden, dubbelklik = standaard), de hoogte met de hoek rechtsonder, en in- en uitzoomen met − en +. Verder de gegevens, rekening en btw-code per regel kiezen, nieuwe leverancier bevestigen, live bedragcontrole; elke keuze gaat in het leergeheugen |
| Werkvoorraad | Links van het formulier de facturen in *Te verwerken* (aan of uit bij Weergave); boven het scherm "2 van 12" met vorige en volgende. Na het boeken opent de factuur die nu op dezelfde plaats staat, met een korte melding wat er geboekt is. De pagina blijft staan, dus gegevens en viewer worden opnieuw geladen; staat de factuur in een eigen venster, dan toont dat venster het nieuwe document |
| Sneltoetsen | Ctrl+Enter = boeken, Alt+↑/↓ = vorige of volgende factuur, Alt+1…5 = naar factuurnummer, factuurdatum, omschrijving, rekening of bedrag (`wwwroot/sneltoetsen.js`). Staat alleen het totaal op de factuur, dan rekent het pakket het bedrag exclusief btw terug met het tarief |
| Rekening kiezen | Op nummer of naam (`Rekeningzoeker`, zonder hoofdletters of accenten): "tele" vindt *Telefoon en internet*. De rekeningen die bij deze leverancier het vaakst gebruikt zijn (uit de laatste facturen, `InkoopDienst.Historie`), staan bovenaan; pijltjes en Enter kiezen, en bij het verlaten van het veld wordt de enige passende rekening gekozen (`RekeningKeuze`, ook bij de bank en bij het aanpassen van een boeking). De naam van de gekozen rekening staat achter het nummer. F2, *Alle rekeningen…* onder de lijst of ⋯ (bij aanwijzen) opent een venster met alle rekeningen (`RekeningVenster`: zoeken, pijltjes en Enter); met het recht Boekhouden kan daar een rekening bij, met nummer, omschrijving, btw-code (of variabel), RGS-code en soort, die meteen gekozen is. Hoort er bij een rekening een btw-code, dan komt die bij het kiezen mee (ook op het rekeningschema in te stellen) |
| Oplichten | Klik een veld aan en de plek waar die waarde op de factuur staat, licht op in de viewer (ook in het eigen venster): `PdfTekst.Zoek` zoekt de waarde als één of enkele opeenvolgende woorden op een regel, in de vormen waarin hij kan staan (`Tekstvormen`: `1-4-2026`, `1 april 2026`, `1.234,56`, `1,234.56`, zonder spaties en hoofdletters). Na een wijziging volgt de markering de nieuwe waarde |
| Onzeker | Bij een factuur uit de tekst van een PDF (geen e-factuur) krijgt een veld dat de herkenning niet vond, of waarvan de waarde niet op de factuur terug te vinden is, een gestippelde rand met de reden in de tooltip (factuurnummer, datum, kenmerk; bij een nieuwe leverancier ook IBAN, btw- en KvK-nummer). Wijzigen of aanwijzen haalt de markering weg |
| Formulier | Labels links, velden rechts: factuurnummer, leverancier, factuurdatum, vervaldatum en kenmerk, omschrijving en het *bedrag* van de factuur (inclusief btw; ook aan te wijzen op de factuur). Daaronder de regels (omschrijving, grootboekrekening, btw, bedrag) met *+ Nog een regel*, het totaal en het verschil met het bedrag. De reden van het voorstel, de signalen en waar het document vandaan komt staan achter de knop *Info* (met het aantal signalen) |
| Creditnota | Schakelaar naast het bedrag. Een creditnota (herkend aan een negatief totaal, of zelf aangezet) staat in het formulier zonder minteken en wordt omgekeerd geboekt; *Verrekenen met* kiest een openstaande factuur van dezelfde leverancier en verlaagt beide posten (*Creditnota verrekend* in het log). Een creditnota leert niets. Eerder zonder boeking bewaarde documenten (privé of geen boeking) zijn terug te zetten naar Te verwerken |
| Aanpassen | Een geboekt document (aanklikken onder *Verwerkt*, in het inkoopboek of via de boeking) toont de boeking (factuur, regels, boekstuk) met de factuur ernaast. *Aanpassen* zet het formulier vol met wat er geboekt is (`InkoopDienst.Geboekt`); er verandert niets tot *Opslaan* (*Annuleren* laat alles zoals het was). Opslaan geeft dezelfde boeking een nieuwe versie met hetzelfde boekstuk (zie *Boeking aanpassen*), en dezelfde openstaande post krijgt het nieuwe bedrag, de nieuwe datum en relatie. Een periodeverdeling gaat mee: de maandboekingen krijgen een nieuwe versie, en vallen er maanden af, dan vervallen die (een versie zonder regels). In het leergeheugen vervangt de nieuwe keuze die van toen, zodat de factuur één keer meetelt. Is de factuur al (deels) betaald of verrekend, dan eerst die betaling terugdraaien |
| OCR | Scans en foto's (`TesseractOcr` als eigen proces, `OcrLezer`). Tesseract wordt gezocht via `Ocr:Tesseract`, het zoekpad en (Windows) `C:\Program Files\Tesseract-OCR`; `Ocr:Aan` = false zet het uit. Op Windows: Tesseract installeren met Nederlands (de installer van UB Mannheim, Apache-2.0). Inkoop en *factuur uploaden* bij de bank nemen ook JPEG en PNG aan |
| Periode verdelen | Schakelaar onder de regels (niet automatisch). Aan: van en tot en met (herkend uit de factuur, bijv. "periode 01-09-2026 t/m 31-08-2027" of "1 september 2026 tot en met 31 augustus 2027", anders een jaar vanaf de maand van de factuurdatum) en de rekening ertussen (standaard vooruitbetaalde kosten, RGS `BVorOvaVan`). De factuur gaat met de kosten op die balansrekening; per maand die de periode raakt (op de eerste dag) boekt het pakket in het memoriaal een gelijk deel naar de kostenrekening, op de cent met het restje in de laatste maand (`Periodeverdeling`). De btw blijft bij de factuur. Bewaard bij het document (`Document.Verdeling`); de geboekte factuur toont de verdeling, heropenen zet hem terug in het formulier, en terugdraaien of verwijderen draait ook elke maandboeking terug. Leren en de historie gebruiken de kostenrekening |
| Inkoopboek | `/a/{id}/inkoopboek` (menu Boekhouden, en *Alles in het inkoopboek* bij Verwerkt): elke geboekte inkoopfactuur en creditnota over een periode (standaard het jaar van de laatste factuur), met boekstuk, leverancier, factuurnummer, omschrijving, exclusief, btw, totaal en wat nog openstaat, onderaan de totalen; zoeken op leverancier, nummer, omschrijving of boekstuk. Een regel opent de factuur (met de boeking), of de boeking als er geen document is (`InkoopDienst.Inkoopboek`). Onder *Verwerkt* op het inkoopscherm staan alleen de laatste 100 documenten |
| Verwijderen | *Verwijderen* op het verwerkscherm, met *Zeker weten? Ja, verwijderen* (`VerwijderKnop`, `InkoopDienst.Verwijder`): het document en zijn inhoud verdwijnen. Is het geboekt, dan wordt eerst de boeking teruggedraaid (zie hieronder): het journaal wijzigt nooit, dus de boeking en de tegenboeking blijven samen staan. Niet als de factuur al (deels) betaald of verrekend is. Een kopie in de documentmap blijft staan. In het log als *Document verwijderd* |
| Terugdraaien | Op een geboekt document: een tegenboeking in hetzelfde dagboek (`Journaalpost.Tegenboeking`; de boeking en de keten blijven staan), de openstaande post vervalt en het document staat weer in Te verwerken, om opnieuw te boeken. Niet als de factuur al (deels) betaald of verrekend is |
| Uitsplitsen | Per regel een eigen omschrijving (leeg = die van de factuur; ook aan te wijzen op de factuur). *Regel toevoegen* vult het bedrag dat nog niet verdeeld is. Onder de regels het totaal, het factuurtotaal en het verschil; zolang er een verschil is, heet de knop *Toch boeken* |
| Btw per regel | Berekend uit bedrag en tarief, maar met de hand aan te passen (bijv. een afronding van de leverancier); ↺ zet hem terug op het berekende bedrag. Een btw van de factuur die afwijkt van het tarief geldt als aangepast |
| Weergave | Onderdelen aan of uit (werkvoorraad, factuur naast het formulier, btw-bedrag per regel, omschrijving per regel, vervaldatum en kenmerk), onthouden in de browser (`Weergavekeuze`: een nieuw onderdeel volgt de standaard). Zonder btw-bedrag rekent het pakket de btw uit de btw-code |
| Zoektemplate | Wijst de gebruiker het factuurnummer, de factuurdatum, vervaldatum, het kenmerk, de omschrijving of een bedrag (het totaal; bij één regel ook het bedrag exclusief btw en de btw) aan op de factuur, dan onthoudt het pakket die plek per leverancier (`Zoekplek`, bij het boeken). Op een volgende PDF van die leverancier vult de tekst op die plek aan wat de herkenning mist; vond de herkenning toen iets anders dan de gebruiker aanwees, dan gaat de plek voortaan voor (`Zoektemplate`). Een aangewezen bedrag gaat alleen voor als de herkende bedragen niet kloppen (exclusief plus btw is niet het totaal, of de btw is niet het tarief over het exclusieve bedrag) en het aangewezen bedrag wel bij de btw past; het andere totaal volgt dan uit de btw. Ook voor facturen die al klaarstonden; in het log als *Zoektemplate van leverancier bijgewerkt* |
| Zelf leren | Na het boeken zoekt het pakket op de PDF waar de geboekte waarden staan (factuurnummer, datums, kenmerk, totaal; bij één regel ook exclusief en btw). Staat een waarde op precies één plek, dan wordt die plek (met wat ruimte voor een langer nummer of bedrag) het zoektemplate van de leverancier, zonder dat de gebruiker hem aanwijst. Een bestaande plek blijft, tenzij de herkenning toen iets anders gaf dan er geboekt is: dan verhuist hij en gaat hij voortaan voor. In het log als *zelf geleerd* |
| Viewer | pdf.js (lokaal in `wwwroot/lib/pdfjs`, Apache-2.0) tekent de PDF; `wwwroot/factuurviewer.js`. Klik een veld aan en sleep een gebied op de factuur (of klik op een woord): de server haalt de tekst uit dat gebied met PdfPig (`PdfTekst.InGebied`, `InkoopDienst.TekstInGebied`) en zet die in het veld; een bedrag of datum wordt eruit gelezen. *Extern* opent de viewer in een eigen venster (`/a/{id}/inkoop/{document}/document`) voor een tweede beeldscherm; dat venster stuurt het gebied via een `BroadcastChannel` naar het verwerkscherm. Blijft het venster open, dan toont het elke factuur die daarna wordt geopend (ook vanuit de lijst of een geboekte factuur). ⟳ draait de factuur een kwartslag (per document onthouden; aanwijzen blijft kloppen), de zoom wordt per browser onthouden. Ctrl+wiel (of knijpen op een touchpad) zoomt de factuur en niet de pagina; in het eigen venster ook Ctrl+plus, Ctrl+min en Ctrl+0 (passend). Waar een gebied heen gaat, bepaalt het veld dat in de browser het laatst de focus had (`data-doel`), ook bij de omschrijving, het bedrag en de btw van de tweede en volgende regel |
| Relaties | `/a/{id}/relaties`: toevoegen en de standaardrekening; het IBAN wijzigen vraagt het wachtwoord en logt oud en nieuw (`InrichtingDienst.ZetIbans`) |
| Geleerd | Een relatie aanklikken (`/a/{id}/relaties/{code}`, `InrichtingDienst.Geleerd`): wat het pakket bij inkoopfacturen geleerd heeft (rekening, btw, omschrijving, hoe vaak op rij bevestigd en tot welk bedrag automatisch), de bevestigingen (bedrag, IBAN, postcodes), de plekken op de factuur (vult aan of gaat voor) en de regels die gelden. *Vergeten* (recht Boekhouden) wist de geleerde keuzes of één plek; in het log. Onder *Zelf controleren* (recht Boekhouden, `InrichtingDienst.ZetControle`) een eigen grens: facturen vanaf een bedrag (inclusief btw), of met meer dan een percentage verschil met de vorige factuur (hoger of lager), komen altijd ter controle, ook als ze herkend en geleerd zijn (`Inkoopverwerker.EigenControle`); bijv. een leverancier die altijd € 1.000 factureert met 50%: een factuur van € 2.000 boek je zelf |
| Regels | `/a/{id}/regels`: automatiseringsregels toevoegen, volgorde en verwijderen; alleen het kantoor (recht Boekhouden) |
| Instellingen | Twee niveaus. `/beheer/instellingen` (beheerder, `KantoorDienst`): de standaard van het kantoor. `/a/{id}/instellingen` (kantoor): naam, KvK, btw-nummer en eigen IBAN's, en per instelling afwijken van het kantoor; een leeg veld volgt het kantoor (de kantoorwaarde staat grijs in het veld). Instellingen: drempel (na hoeveel gelijke keuzes), bandbreedte (automatisch tot zoveel keer het hoogste bevestigde bedrag), limiet (geen, kantoor of eigen; B2), betalingstermijn, adrescontrole aan/uit en de documentmap (`Verwerkingsinstellingen`, `AfwijkendeInstellingen`) |
| Documentmap | Na het boeken een kopie van het document in een map met variabelen (`Documentpad`): `#administratie`, `#jaar`, `#kwartaal`, `#maand`, `#soort`, `#leverancier`, bijv. `C:\Users\naam\OneDrive\Klanten\#administratie\#jaar\#soort`. Bestandsnaam: datum, relatie en factuurnummer. Nooit overschrijven (anders `(2)`); lukt het niet, dan blijft de boeking staan met de reden bij het document en in het log. Het origineel blijft in de database |

#### Bank (plakje A8)

| Onderdeel | Hoe |
|---|---|
| Inlezen | `/a/{id}/bank`: MT940 en CAMT.053 slepen of kiezen (`BankDienst.Lees`). Een afschrift waarvan beginsaldo plus mutaties niet het eindsaldo is, weigert het pakket: dan wordt niets ingelezen. Sluit het beginsaldo niet aan op het eindsaldo van het vorige afschrift van die rekening, dan staat dat als melding bij het afschrift (mist er een afschrift?) |
| Dubbel | Elke mutatie krijgt een sleutel (SHA-256 van datum, bedrag, tegenrekening, referenties en omschrijving, met een volgnummer voor gelijke mutaties op één afschrift). Hetzelfde of een overlappend bestand nog eens inlezen slaat bekende mutaties over |
| Bankdagboek | Het dagboek met het IBAN van het afschrift. Heeft geen dagboek dat IBAN, dan krijgt het eerste bankdagboek zonder IBAN het, en het IBAN komt bij de eigen rekeningen. Staat er al een eigen rekening en is dit IBAN er niet bij, dan weigert het pakket het bestand: zo komt een afschrift van een andere klant nooit in deze administratie |
| Automatisch | Per nieuwe mutatie `Bankverwerker`: eerst afletteren (§3.8); de enige zekere kandidaat wordt geboekt. Kan er geen openstaande post bij horen, dan boekt een automatische regel of een geleerde keuze (na de drempel, binnen de bandbreedte, zelfde IBAN) op de rekening. De rest staat in *Te verwerken*, met de reden |
| Verwerken | De mutaties staan links; een mutatie aanklikken opent rechts het verwerkpaneel (`BankPagina`): het voorstel om af te letteren (één klik), en de **regels**: per regel een factuurnummer (een openstaande post, ook een deel van het bedrag) of een grootboekrekening met btw-code, bedrag en omschrijving. In de kolom factuurnummer typen (een nummer, een bedrag of de naam van de leverancier) toont de vijf beste treffers (`Postzoeker`: gelijk bedrag of nummer, dan een nummer dat ermee begint, de naam, een deel van het nummer; de tegenpartij gaat voor), te kiezen met een klik of pijltjes en Enter; of F2 of ⋯ voor het venster met alle openstaande posten (`PostKeuze`, `OpenpostenVenster`: zoeken op leverancier, nummer of bedrag, filteren op relatie, crediteuren/debiteuren en de tegenpartij van de mutatie, pijltjes en Enter, Esc sluit); *Posten kiezen…* (of dubbelklik op de mutatie) vinkt er meer tegelijk aan. Posten en regels samen zijn precies het bedrag (`BankDienst.Verdeel`, `Bankboeker.Verdeeld`); alleen hele posten = afletteren, waarbij een verschil op de post blijft. **Autoherkenning** (recht Boekhouden): van de mutatie een regel maken (tekst in de naam of de omschrijving, eventueel de tegenrekening, bedrag van–tot, naar de rekening en btw van de eerste regel, automatisch boeken) en die meteen toepassen op alles in Te verwerken, bijv. *KPN, € 1–50 → telefoonkosten, 21%*; de regel staat daarna bij Inrichting → Regels. Kan de mutatie een factuur betalen, dan staat er geen rekening vooraf ingevuld. Een deelbetaling laat het restant op de factuur openstaan; te veel betaald blijft als te vorderen op de (laatste) factuur staan, zodat het in de openstaande posten en de crediteurenlijst zichtbaar is. Een betaling met het nummer of kenmerk van een factuur die al betaald is, krijgt het voorstel *Factuur is al betaald: dubbele betaling?* en wordt nooit automatisch geboekt (ook niet op een andere factuur met hetzelfde bedrag); afletteren zet het bedrag als te vorderen op die factuur, en de terugbetaling lettert daarna (automatisch) af. Een deelbetaling met het factuurnummer of kenmerk én het IBAN van de leverancier gaat automatisch (`Afletteraar`, `Bankboeker.Afletteren`; getest in `BankpraktijkTests`: vijftien facturen van drie leveranciers, een afschrift met deelbetaling in twee delen, kosten zonder factuur en een dubbele betaling, sluitend op bank en crediteuren); bevestigt de gebruiker een voorstel en hoort het IBAN nog bij geen relatie, dan leert het pakket het bij de relatie (*IBAN geleerd bij relatie* in het log). Op rekening boeken gaat in het leergeheugen |
| Factuur uploaden | In het paneel van een mutatie: de factuur inlezen zoals bij inkoop (`InkoopDienst.Lees` met de mutatie). Na het boeken van die factuur lettert het pakket de mutatie erop af als het bedrag gelijk is, en leert het IBAN (`BankDienst.NaFactuur`). Ook zonder upload: komt een factuur later binnen en is die nu de enige zekere kandidaat voor een mutatie in *Te verwerken*, dan wordt die mutatie afgeletterd |
| Opnieuw herkennen | Knop bij *Te verwerken* (`BankDienst.VerwerkOpnieuw`): alle mutaties in Te verwerken opnieuw herkennen: afletteren, regels (ook een autoherkenning) en geleerde keuzes, en de reden bij wat overblijft bijwerken, bijv. als er na het inlezen nog facturen zijn geboekt of een regel is toegevoegd. *Opslaan en toepassen* bij de autoherkenning doet dit meteen |
| Werklijsten | Tabbladen op het bankscherm: *Te verwerken* (de betalingen en ontvangsten zonder factuur of regel), *Openstaande posten* (debiteuren en crediteuren op vervaldatum, met het aantal dagen vervallen en een link naar de factuur) en *Verschillen* (deels betaalde of verrekende posten, en het saldo van de vraagposten met een link naar de grootboekkaart) |
| Klein betaalverschil | Kantoorinstelling (per administratie af te wijken): *Klein betaalverschil afboeken tot* een bedrag en de *Rekening betaalverschillen*. Noemt een betaling het factuurnummer of kenmerk van precies één factuur en komt hij van het IBAN van de leverancier, en wijkt het bedrag hoogstens zoveel af (te veel of te weinig), dan lettert het pakket de factuur automatisch helemaal af en boekt het verschil op die rekening (`Afletteradvies.KleinVerschil`). Niet als de factuur al betaald was, bij betalingskorting, bij een betaling van meer facturen of als er een andere factuur precies past; standaard uit. Een administratie zet het uit met 0; de rekening moet in de administratie bestaan en mag geen bank, debiteuren, crediteuren of btw zijn |
| Opslag | `Afschrift` (saldi en melding) en `Banktransactie` (de mutatie, status, de boeking); bij een document de mutatie waarbij het geüpload is (`Document.BanktransactieId`); migraties `Bank` en `DocumentBijBank` voor PostgreSQL en SQLite |

#### Verkoop (plakje A9)

| Onderdeel | Hoe |
|---|---|
| Domein | `Boekhouding/Verkoop`: `Verkoopfactuur` met `Verkoopregel` (aantal × prijs, korting in procenten; een creditfactuur heeft een negatief aantal), `Partij` en `Adres` (afzender en klant zoals op de factuur). De btw wordt per btw-code over de som van de regels berekend, zoals op de factuur; `NaarFactuur` verdeelt die btw zo over de regels dat de boeking er tot op de cent mee klopt. `Factuurnummering`: jaar + volgnummer van vier cijfers (`20260001`, na 9999 verder met vijf), per jaar van de factuurdatum, zonder gaten (`Gaten` controleert). `Betalingskenmerk`: 16 cijfers met controlecijfer (gewichten 2, 4, 8, 5, 10, 9, 7, 3, 6, 1 van rechts), gemaakt uit het factuurnummer; betaalt de klant daarmee, dan lettert de bank met zekerheid af. `Verkoopcontrole`: naam en adres van beide partijen, het eigen IBAN, het eigen btw-nummer (niet bij alleen vrijgestelde prestaties), een omschrijving per regel, verkoopcodes, en bij verlegd of ICP een geldig btw-nummer van de klant (bij ICP uit een ander EU-land); `Vermeldingen` geeft de teksten die op de factuur moeten (*Btw verlegd*, *Intracommunautaire levering …*, *Vrijgesteld van btw*, *Uitvoer buiten de EU*) |
| Klanten | Een relatie is klant met het vinkje *Klant* (relatiepagina, of *Nieuwe klant…* op de factuur): adres, land, e-mail, contactpersoon, betalingstermijn, omzetrekening en btw-code verkoop (`Klantgegevens`). Het adres en contact van de eigen onderneming staan bij Instellingen (`Contactgegevens`) |
| Factuur maken | `/a/{id}/verkoop` (Boekhouden → Verkoop): concepten bovenaan, dan de facturen in de periode met vervaldatum en wat openstaat. *Nieuwe factuur* (`/a/{id}/verkoop/nieuw`): klant, datum en vervaldatum (volgt de termijn van de klant of de administratie tot je hem zelf wijzigt), uw referentie, omschrijving en de regels (omschrijving, aantal, eenheid, prijs, korting, omzetrekening, btw) met de btw per tarief en het totaal; *Bewaren als concept* of *Definitief maken* (`VerkoopDienst`) |
| Definitief | `VerkoopDienst.MaakDefinitief`, in één transactie met de administratie vergrendeld: controle, het volgende nummer, het betalingskenmerk, de boeking in het verkoopboek (boekstuk = factuurnummer; debiteuren debet, omzet en af te dragen btw per code credit) en de openstaande post. De klant wordt dan vastgelegd (een latere wijziging van de relatie verandert de factuur niet). Mislukt de controle, dan blijft het een concept en is er geen nummer verbruikt. Stond de ontvangst al op de bank, dan lettert die nu af (`BankDienst.NaFactuur`). Een definitieve factuur is niet meer te wijzigen of te verwijderen |
| Corrigeren | *Crediteren*: een concept-creditfactuur met dezelfde regels negatief (`Crediteer`); definitief krijgt hij een nummer uit dezelfde reeks en wordt hij verrekend met wat er van de factuur nog openstaat. *Kopiëren*: een nieuw concept met dezelfde klant en regels, vandaag |
| Periodeverdeling | Vinkje *omzet verdelen over een periode* (bijv. in januari een jaarabonnement): de omzet gaat eerst op een balansrekening (standaard 1710 *Vooruitontvangen omzet*, RGS `BSchOpa`), en per maand boekt het memoriaal een gelijk deel naar de omzetrekening (`GrootboekDienst.BoekVerdeling`, dezelfde verdeling als bij inkoop). De btw blijft bij de factuur |
| Verkoopboek en debiteuren | Dagboeken → Verkoop (`/a/{id}/verkoopboek`) is hetzelfde scherm als het inkoopboek (`Factuurboekoverzicht`, `Factuurboek.Lees`); Rapportages → *Openstaande debiteuren* (`/a/{id}/debiteuren`) hetzelfde als de crediteuren (`OpenpostenOverzicht`, `OpenpostenDienst.Debiteuren`), met PDF; een post opent de factuur |
| Lay-out | Inrichting → *Factuurlay-out* (`/a/{id}/factuurlayout`, `FactuurlayoutPagina`): de ontwerper. Links het A4 met een voorbeeldfactuur (`Verkoopbeeld.Voorbeeld`), rechts de instellingen. Een blok aanklikken kiest het, slepen verplaatst het, het handvat rechts maakt het breder of smaller, de pijltjes schuiven 1 mm (Shift 5 mm) (`Ontwerpvlak`, `factuurontwerp.js`). Blokken (`Factuurontwerp`, `Ontwerpblok`, maten in mm vanaf linksboven): logo, afzender, klant (op de plek van het envelopvenster), factuurgegevens, titel, regels met totalen (lopen door naar een volgende pagina), betaalinstructie (direct onder de regels), voettekst (op elke pagina) en eigen tekstblokken. Per blok: tonen, plek, breedte, lettergrootte, kleur, vet, uitlijning en (bij titel, tekst, betaalinstructie en voettekst) de tekst met variabelen (`Factuurteksten`: `#totaal`, `#vervaldatum`, `#iban`, `#kenmerk`, `#kvk` …). Voor de pagina: lettertype (IBM Plex Sans meegeleverd, verder lettertypen die overal zijn), lettergrootte, accent- en tekstkleur, de stijl van de regels en welke kolommen (aantal, eenheid, prijs, korting, btw). Logo: PNG of JPEG tot 1 MB, op inhoud gecontroleerd en door de virusscan. Meer lay-outs per administratie (bijv. een Engelse), één is de standaard; per factuur te kiezen. Opgeslagen als JSON (`Factuurlayout`, migratie `Factuurlayout`); een ontwerp wordt altijd binnen de grenzen gebracht (`Compleet`: kleuren alleen `#rrggbb`, maten binnen het A4) |
| PDF en afdrukken | Eén opmaak voor alles (`FactuurHtml`): het voorbeeld op het scherm, de ontwerper, de afdruk en de PDF (`FactuuropmaakDienst.Pdf`, met `PdfDienst`). Op papier staat de voettekst in de paginamarge (CSS `@page { @bottom-center }`), zodat hij op elke pagina op dezelfde plek staat en de regels er nooit overheen lopen; de kolomkoppen herhalen op elke pagina. Een concept krijgt "CONCEPT" over de pagina. Afdrukken drukt dezelfde HTML af in een verborgen frame (`download.js`) |
| E-factuur | `UblFactuurSchrijver`: UBL 2.1 volgens Peppol BIS Billing 3.0 (voldoet aan NLCIUS), met btw-categorie per code (S, Z, E, AE verlegd, K ICP, G uitvoer) en de reden, korting per regel, het betalingskenmerk en het IBAN; een creditfactuur wordt een `CreditNote` met verwijzing naar de factuur. Alleen van een definitieve factuur. Getest door hem terug te lezen met de eigen `UblFactuurLezer` |
| Versturen | *E-mail*: een `.eml` met de PDF en de e-factuur als bijlage, aan het e-mailadres van de klant, met een korte tekst en de betaalinstructie; het opent als concept in Outlook of een ander e-mailprogramma om te versturen (`X-Unsent`). Zonder mailserver in het pakket; in het log als *Verkoopfactuur als e-mail klaargezet* |
| Terugkerende facturen | Boekhouden → *Terugkerende facturen* (`/a/{id}/verkoop/terugkerend`, `AbonnementDienst`), of *Herhalen…* op een factuur: naam, klant, elke maand, kwartaal, halfjaar of jaar vanaf een eerste factuurdatum, eventueel tot en met een datum (`Abonnementsplan`; datums tellen vanaf het begin, dus de 31e blijft het eind van de maand). Omschrijving en regels met variabelen: `#periode` (oktober 2026, 4e kwartaal 2026, 2e halfjaar 2026, 2026, of van t/m als de periode niet samenvalt), `#maand`, `#kwartaal`, `#jaar`, `#van`, `#tot`. *Facturen maken* maakt alles tot en met vandaag: als concept, of meteen definitief (vinkje), eventueel met de omzet verdeeld over de periode van de factuur (bijv. een jaarabonnement in januari, per maand naar de omzet). Het volgnummer gaat in dezelfde transactie omhoog (administratie vergrendeld), dus twee keer klikken maakt geen dubbele facturen; kan een factuur niet definitief, dan blijft hij concept met de reden. Op de verkooppagina staat hoeveel er klaar staan. Verwijderen laat de gemaakte facturen staan; *actief* uit stopt hem |
| Rechten | Lezen: lijst, factuur, verkoopboek en debiteuren, PDF en e-factuur; Invoeren (ondernemer en kantoor): concepten, definitief, crediteren en kopiëren, de lay-out en terugkerende facturen. In het log: concept gemaakt, gewijzigd, verwijderd, *Verkoopfactuur definitief* en de wijzigingen van een lay-out |

#### Windows-app voor de tests

Een echte Windows-applicatie voor één pc (`src/Boekerhouden.Administratie.Windows`): dezelfde schermen als de
webapp in een eigen venster, zonder webserver, zonder databaseserver en zonder inloggen.

| Onderdeel | Hoe |
|---|---|
| Venster | WinForms met een `BlazorWebView` (WebView2): de Razor-schermen draaien in het venster zelf, er is geen HTTP. Startscherm `Routes`, de titel volgt de pagina |
| Database | Eén SQLite-bestand (`administratie.db`) in `%LOCALAPPDATA%\Boekerhouden\Administratie`. `SqliteAdministratieDbContext` met eigen migraties (`Data/MigratiesSqlite`): bedragen exact als geheel getal (× 10.000), tijdstippen sorteerbaar, triggers die boekingen en het log onwijzigbaar maken. De hele testsuite draait ook op SQLite (`BOEKERHOUDEN_TEST_SQLITE=1`, in CI) |
| Gebruiker | `LokaleApp` en `LokaleGebruiker`: wie de app start, is de lokale gebruiker (beheerder en kantoor) met de naam van de Windows-gebruiker; geen wachtwoord, geen tweestapsverificatie. `Werkomgeving.Lokaal` laat uitloggen, beveiliging, wachtwoord en gebruikersbeheer weg; het IBAN van een relatie wijzigen vraagt geen wachtwoord (het log houdt oud en nieuw bij) |
| Documenten | `IDocumentvenster`: het origineel opent in het standaardprogramma (alleen PDF of XML, op inhoud bepaald: `Bestandssoort`); de factuurviewer krijgt de inhoud mee in plaats van een adres; *Extern* opent een tweede venster (`LosseViewer`) dat via hetzelfde kanaal met het verwerkscherm praat |
| Virusscan | Geen: wat de gebruiker op zijn eigen pc uploadt, is van hemzelf (Windows scant de bestanden op schijf al). De webapp op kantoor scant wel, met ClamAV |
| Bouwen | `scripts/maak-windows-app.ps1` of `.sh`: `dotnet publish -r win-x64 --self-contained`, gezipt met `LEESMIJ.txt`. De CI maakt de zip en start hem op een Windows-runner met `--proef` (eerste scherm en proefbalans, met schermafdrukken) |

Nog niet gebouwd: terugkerende facturen vanzelf (zonder klik) maken, versturen per e-mail vanuit het pakket (nu een .eml), Peppol, verkoopfacturen inlezen, facturen per e-mail of uit een map, Peppol, OCR, btw-aangifte en periodes, het RGS-werkboek in de
administratie (nu de RGS-code als tekst bij een nieuwe rekening), en de hulppagina.

## 6. Open vragen

1. **Btw-tarieven en -rubrieken** zijn opgenomen zoals in 2026 (21%, 9%, 0%). Controleer de rubrieken van het
   aangifteformulier bij plakje A10.
2. **Drempel voor automatisch verwerken**: standaard twee bevestigingen (de derde factuur gaat vanzelf), op
   kantoorniveau en per administratie in te stellen, net als de bandbreedte en de adrescontrole.
3. **Directe bankkoppeling**: welke aanbieder (kosten per rekening per maand), en wanneer?
4. **Hosting voor ondernemers** (AD2): VPS in Nederland, tweestapsverificatie verplicht, back-up buiten de
   server. Een besluit is nodig vóór "later".
