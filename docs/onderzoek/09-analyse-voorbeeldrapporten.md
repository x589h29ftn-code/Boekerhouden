# 9. Analyse voorbeeldrapporten: wat moet de rapportgenerator kunnen?

> Het kantoor leverde twee definitieve rapporten uit zijn huidige pakket aan. Om privacyredenen staan
> ze **niet** in deze repository en noemen we hier geen klantnamen of bedragen.
>
> - **Voorbeeld A** – *BV klein*, "Jaarrekening 2025": accountantsrapport plus jaarrekening op fiscale grondslagen, 33 pagina's.
> - **Voorbeeld B** – *Eenmanszaak*, "Bijlage bij de aangifte inkomstenbelasting 2024": geen samenstellingsverklaring, 13 pagina's.
>
> Dit is de norm voor onze rapportsjablonen. Wat hier is geproduceerd, moet onze
> applicatie minstens even goed kunnen.

## 9.1 Opbouw voorbeeld A (BV klein)

| Deel | Hoofdstuk | Inhoud | Gegenereerd uit |
|---|---|---|---|
| Voorblad | – | Logo, klantnaam, "te {plaats}", "Jaarrekening {jaar}", blauwe band onderaan | Klant + dossier + huisstijl |
| Inhoudsopgave | – | Twee delen (ACCOUNTANTSRAPPORT, JAARREKENING {jaar}), hoofdstukken doorlopend genummerd 1–13, met paginanummers | Automatisch |
| Tussenblad | – | "ACCOUNTANTSRAPPORT" | Sjabloon |
| Accountantsrapport | Briefhoofd | Kantooradres (rechtsboven), geadresseerde ("Aan de directie van …" + adres), plaats/datum, aanhef, openingszin | Klant + kantoor + dossier |
| | 1 Samenstellingsverklaring | Standaardtekst (Standaard 4410, Titel 9 BW2, VGBA), "namens deze", **gescande handtekening**, naam en titel accountant. Voettekst met handelsnaam/KvK en algemene voorwaarden | Standaardtekst + variabelen + handtekening |
| | 2 Algemeen | Oprichting (aktedatum, KvK-nummer), Doelstelling (statutenartikel, letterlijk), Bestuur (rechtspersoon-bestuurder + vertegenwoordigers) | Klantgegevens (permanent dossier) |
| | 3 Resultaten | (a) **Ontwikkeling resultaat**: verkorte W&V, huidig en vorig jaar, met **% van de omzet**. (b) **Vergelijking resultaat**: "Resultaat hoger door / lager door" per hoofdgroep. (c) **Kengetallen resultaat** over **5 jaar**: omzetindex, brutomarge %, nettowinstmarge %, fte, omzet per fte, personeelskosten per fte, personeelskosten % omzet | Rubriekensaldi + historie + fte |
| | 4 Financiële positie | (a) Ontwikkeling werkkapitaal (beschikbaar op lange termijn − vastgelegd op lange termijn). (b) Opbouw werkkapitaal (vlottende activa − kortlopende schulden). (c) Kengetallen over **5 jaar**: current ratio, quick ratio, solvabiliteit I en II, omloopsnelheid debiteuren en crediteuren (dagen), rentabiliteit eigen vermogen. Elk met een vaste uitlegtekst | Rubriekensaldi + historie |
| | 5 Fiscale positie | Tekst over de **fiscale eenheid**, de **Vpb-berekening** (resultaat voor belastingen → fiscaal resultaat → niet-aftrekbare kosten/boetes → belastbaar bedrag → tarief per schijf → verschuldigd), specificatie belastinglast, belastingpositie per balansdatum (verrekening via rekening-courant) | Fiscale module + tarieftabel |
| | 6 Meerjarenoverzicht | W&V en verkorte balans over **5 jaar** | Historie |
| Tussenblad | – | "JAARREKENING {jaar}" | Sjabloon |
| Jaarrekening | 7 Balans | Activa en passiva "(Na resultaatbestemming)", **tweekoloms per jaar** (detail en subtotaal), vorig jaar ernaast | Rubriekenschema |
| | 8 Winst-en-verliesrekening | Brutomarge-presentatie, personeelskosten, overige bedrijfskosten (per groep), som der kosten, bedrijfsresultaat, financiële baten en lasten, belastingen, resultaat na belastingen | Rubriekenschema |
| | 9 Algemene toelichting | Activiteiten, feitelijke vestiging, groepsverhoudingen, schattingen | Standaardteksten + variabelen |
| | 10 Grondslagen | Algemeen (fiscale grondslagen: Titel 9 behalve afdeling 6, Besluit fiscale waarderingsgrondslagen, RJ-handreiking), waardering per post (voorraden, onderhanden projecten, vorderingen, liquide middelen, voorzieningen, schulden), resultaatbepaling (omzet incl. PoC-methode, brutomarge, personeelskosten/pensioen, kosten, rente, belastingen) | Grondslagenbibliotheek, **alleen de posten die voorkomen** |
| | 11 Toelichting op balans | **Specificaties** per post met subgroepen en subtotalen. **Verlooptabellen** (rekening-courant groepsmaatschappij: stand 1-1, mutaties, stand 31-12, twee jaar naast elkaar; voorzieningen). **Verloop eigen vermogen** in meerdere kolommen (aandelenkapitaal, agio, overige reserves, totaal). Teksten (aandelenkapitaal: maatschappelijk/geplaatst/aantal/nominaal, agio, ingehouden resultaat, "niet ter vrije beschikking", rente r/c %). **Niet in de balans opgenomen verplichtingen** (fiscale eenheid, huur, overige) | Specificaties + verloopoverzichten + teksten |
| | 12 Toelichting op W&V | Per rubriek de **grootboekrekeningen als specificatieregels** (omschrijving zoals in de administratie), gegroepeerd, met subtotalen en een vergelijkend jaar | Automatisch uit proefbalans + rubricering |
| | 13 Overige toelichtingen | Werknemers (gemiddeld fte, binnen/buiten NL), leningen aan bestuurders, resultaatbestemming (vorig jaar vastgesteld), **voorstel resultaatbestemming** (bijv. 50% reserves / 50% dividend), **ondertekening** (plaats/datum + tabel naam/functie/rechtspersoon/handtekening) | Klantgegevens + dossierinvoer |

**Kop- en voettekst (voorbeeld A):**
- logo gecentreerd bovenaan elke pagina;
- in het jaarrekeningdeel linksboven "{klantnaam}, te {plaats}";
- voettekst met paginanummer (midden) en **"Samenstellingsverklaring afgegeven"** (rechts).

## 9.2 Opbouw voorbeeld B (eenmanszaak, bijlage aangifte IB)

| Deel | Hoofdstuk | Inhoud |
|---|---|---|
| Voorblad | – | Logo, naam onderneming, "te {plaats}", **"Bijlage bij de aangifte inkomstenbelasting {jaar}"** |
| Inhoudsopgave | – | Eén deel: JAARREKENING {jaar} |
| Tussenblad | – | "JAARREKENING {jaar}" |
| Jaarrekening | 1 Balans | Vaste activa (MVA), vlottende activa; passiva: **Eigen vermogen = ondernemingsvermogen + fiscale oudedagsreserve**, langlopende en kortlopende schulden |
| | 2 W&V | Netto-omzet, overige bedrijfsopbrengsten, kosten uitbesteed werk → brutomarge; personeelskosten; afschrijvingen; overige bedrijfskosten → bedrijfsresultaat → financiële baten en lasten → **resultaat (geen belastingen)** |
| | 3 Specificaties van balans | MVA-specificatie per activum plus **verloopoverzicht MVA** (boekwaarde begin: aanschafwaarde/cum. afschrijving → investeringen/afschrijvingen → boekwaarde eind, met afschrijvingspercentages van/tot). Vorderingen, liquide middelen (bankrekeningen met IBAN). **Verloop ondernemingsvermogen** (stand 1-1 + resultaat − privéonttrekkingen + privéstortingen ± overige → stand 31-12). FOR. Langlopende en kortlopende schulden |
| | 4 Specificaties van W&V | Per rubriek de grootboekrekeningen als specificatieregels |

**Voettekst (voorbeeld B):** paginanummer en **"Geen accountantscontrole toegepast"** plus de datum van het rapport. Er is geen samenstellingsverklaring en geen accountantsrapport. Dit is het opdrachttype *Winstaangifte opstellen* (zie [§2.7](02-vaktechnisch-kader.md#27-opdrachttypen)). Voor de **VOF** komt later een variant met kapitaal en winstverdeling per vennoot, zodra er een voorbeeld is.

## 9.3 Catalogus van rapportbouwstenen

De rapportgenerator wordt opgebouwd uit herbruikbare **bouwstenen**. Een rapportsjabloon is een geordende lijst bouwstenen, met voorwaarden (bijv. "alleen bij BV", "alleen als de rubriek een saldo heeft").

| # | Bouwsteen | Parameters | Gebruikt in |
|---|---|---|---|
| B01 | Voorblad | titeltekst, ondertitel, achtergrond/band | A, B |
| B02 | Inhoudsopgave | delen, diepte | A, B |
| B03 | Tussenblad | titel | A, B |
| B04 | Briefhoofd + aanhef | geadresseerde, plaats/datum, openingszin | A |
| B05 | Hoofdstuktitel (genummerd) | nummering doorlopend over delen | A, B |
| B06 | Tekstblok | standaardtekst-id (Liquid), dossierspecifieke override | A, B |
| B07 | Handtekeningblok accountant | accountant, handtekeningafbeelding, "namens deze" | A |
| B08 | Financieel overzicht (balans/W&V) | rubriekenschema, presentatievariant (micro/klein/IB), **tweekoloms** detail + totaal, vergelijkend jaar | A, B |
| B09 | Specificatietabel | rubriek, bron (grootboekrekeningen of handmatige specificatieregels), groepering, subtotalen | A, B |
| B10 | Verlooptabel enkel | rubriek/specificatie, regels (stand 1-1, mutaties, stand 31-12), 1 of 2 jaren | A, B |
| B11 | Verlooptabel meerkoloms | kolommen (componenten EV, categorieën MVA/IVA), blokken (boekwaarde begin, mutaties, boekwaarde eind, percentages) | A, B |
| B12 | Analyse met % van omzet | groepering, jaren | A |
| B13 | Verschillenanalyse "hoger/lager door" | groepen, tekenregels | A |
| B14 | Kengetallentabel | kengetallenset, aantal jaren (5), eenheid (%/dagen/kengetal), uitlegteksten | A |
| B15 | Meerjarenoverzicht | W&V/balans verkort, 5 jaar | A |
| B16 | Werkkapitaaloverzicht | definitie (lang vermogen − vastgelegd; vlottende activa − kortlopende schulden) | A |
| B17 | Fiscale berekening Vpb | correctieregels, tarieftabel per jaar, fiscale eenheid-variant | A |
| B18 | Belastingpositie | verschuldigd, betaald/verrekend, saldo | A |
| B19 | Werknemerstabel | fte per jaar, binnen/buiten NL | A |
| B20 | Resultaatbestemming + voorstel | percentage of bedrag, dividend | A |
| B21 | Ondertekeningstabel | functionarissen (naam, functie, rechtspersoon), plaats/datum | A |
| B22 | Grondslagenblok | toont alleen de grondslagen voor posten met saldo (huidig of vorig) | A |

**Opmaakregels** (afgeleid uit beide voorbeelden):
- **Bedragen**: hele euro's, duizendtalpunt, negatief als `-1.234`, nul als `-`. Kolomkop "31 dec {jaar}" of "{jaar}" met daaronder "€".
- **Lijnen**: enkele lijn boven een subtotaal, **dubbele lijn onder een eindtotaal**.
- **Koppen**: hoofdstuktitel in kapitalen en huisstijlkleur met nummer; subkoppen vet in kapitalen; groepskoppen vet.
- **Kop- en voettekst** per deel instelbaar. Voettekst met **statustekst per opdrachttype** ("Samenstellingsverklaring afgegeven", "Geen accountantscontrole toegepast" + datum, en "CONCEPT" zolang niet definitief).
- **Paginering**: hoofdstuk op een nieuwe pagina, tabellen niet splitsen waar dat kan, **koppen nooit los onderaan een pagina** (keep-with-next).

## 9.4 Gegevens die we nodig hebben, naast de proefbalans

| Gegeven | Waar vastleggen | Doorrollen? |
|---|---|---|
| Oprichtingsdatum, KvK-nummer, statutaire en feitelijke vestiging, **doelstelling (statutentekst)** | Klant › permanent dossier | Ja |
| Bestuur (ook rechtspersoon-bestuurder met vertegenwoordigers), functies, ondertekenaars | Klant › functionarissen | Ja |
| Groepsverhoudingen, **fiscale eenheid** (moeder, leden) | Klant | Ja |
| Aandelenkapitaal: maatschappelijk/geplaatst, aantal, nominale waarde, aandeelhouder | Klant | Ja |
| **Gemiddeld aantal werknemers (fte)** per jaar, binnen/buiten NL | Dossier | Historie |
| **Historische cijfers (5 jaar)** voor kengetallen en meerjarenoverzicht | Afgesloten dossiers, of bij de start via het **standaard importsjabloon** per boekjaar ([§3.3](03-gegevensimport-xaf-excel-rgs.md#33-standaard-importsjabloon-excelcsv)) | – |
| Voorstel resultaatbestemming (% of bedrag) en dividend | Dossier | Nee |
| Vpb-tarieven en schijven per jaar, fiscale correcties (niet-aftrekbare kosten, boetes, …) | Kantoorparameters + dossier | Parameters wel |
| Rente rekening-courant %, "niet ter vrije beschikking"-teksten, niet in de balans opgenomen verplichtingen | Dossier (tekstblokken met vorig-jaarversie als voorstel) | Ja, als voorstel |
| Accountant: naam, titel (AA/RA), **handtekeningafbeelding** | Gebruikersprofiel | – |
| Huisstijl: logo, kleuren, lettertype, briefhoofdgegevens, voettekst met handelsnaam/KvK/voorwaarden | Kantoorinstellingen | – |
| Privéonttrekkingen/-stortingen (IB), FOR-verloop | Uit rubricering (privérekeningen) + dossier | – |

## 9.5 Wat de voorbeelden leren over kwaliteit (en waar wij het beter doen)

1. **Een losse kop onderaan of op een eigen pagina**: in voorbeeld A staat een hoofdstukkop met subkop alleen op een pagina, met de tabel pas op de volgende. → *Keep-with-next* op koppen en een minimaal aantal regels na een kop.
2. **Een onvolledige verlooptabel**: in voorbeeld B toont het verloop van de FOR alleen "Stand 1 januari", zonder mutaties en eindstand. → Een verlooptabel heeft altijd een eindstand, ook zonder mutaties.
3. **Een mogelijke misrubricering**: in voorbeeld B staat een rekening met een balansachtige naam onder een kostenrubriek. → Waarschuwing "omschrijving/RGS past niet bij rubriek" ([§4.6](04-rubricering-ontwerp.md#46-valkuilen-en-hoe-we-ze-afvangen)). De medewerker beoordeelt, het systeem dwingt niets af.
4. **Afronding bij verdelingen**: bij een 50/50-resultaatbestemming worden ingehouden resultaat en dividend afzonderlijk afgerond (verschil € 1). → Een vaste afrondingsregel: het restant gaat naar de reserves, zodat de som altijd klopt.
5. **Het belastbaar bedrag** wordt in voorbeeld A voor de tariefberekening naar beneden afgerond (een paar euro lager dan het belastbaar bedrag). → De afrondingsregel voor de Vpb-berekening moet vaktechnisch worden bevestigd en als parameter vastgelegd.

## 9.6 Gevolgen voor scope en planning

De MVP wordt groter dan in [hoofdstuk 8](08-roadmap-risico-s-en-beslispunten.md) aangenomen. Het voorbeeldrapport BV klein bevat namelijk kengetallen, een meerjarenoverzicht, de fiscale positie (Vpb) en een verschillenanalyse. Die zaten eerst in "fase 5" en horen nu in de MVP, omdat het rapport zonder die onderdelen het huidige rapport niet kan vervangen. Zie de bijgewerkte roadmap.
