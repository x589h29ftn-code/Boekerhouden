# 2. Vaktechnisch kader: wat moet de software ondersteunen?

> Dit hoofdstuk beschrijft de beroepsregels en jaarrekeningvoorschriften waarop het
> werkprogramma, de checklists, de standaardteksten en de rapportopbouw moeten aansluiten.
> Notatie: **[n]** = bron (lijst onderaan), **(EK)** = vakkennis zonder directe bron,
> **(O)** = onbevestigd, eerst verifiëren. **Dit is geen juridisch advies.** De definitieve
> inhoud van werkprogramma en teksten moet een vaktechnisch verantwoordelijke binnen het
> kantoor vaststellen.

## 2.1 Standaard 4410 "Samenstellingsopdrachten"

### Welke versie geldt
- Geldend is de **herziene Standaard 4410** (Nederlandse versie van ISRS 4410 Revised). Die is in werking sinds 1-1-2016 en sinds 1-4-2016 verplicht voor alle BW2-jaarrekeningen, ook die van micro-rechtspersonen [2][3].
- Per 1-1-2019 is de standaard aangepast voor de **NV NOCLAR** (vermoedens van fraude en niet-naleving van wet- en regelgeving) [4].
- **Handreiking:** NBA-handreiking **1136** (versie 19-3-2019, niet dwingend) [1].
  - Handreiking 1124 gaat over iets anders: dat zijn de Wwft-richtsnoeren [18].
- **In beweging:** de NBA consulteerde tot 14-9-2026 over aanpassing van de NV COS (dus ook 4410) aan ISQM/SKM1 [17]. Standaardteksten en checklists moeten dus **versiebeheer per regelgevingsversie** krijgen.
- **Niet onder 4410 valt:**
  - administratieve verwerking tot en met de saldibalans [25];
  - samenstellen als dezelfde accountant ook de controle of beoordeling doet [50].
- **Hoofdregel:** bij een door de accountant samengesteld financieel overzicht hoort een samenstellingsverklaring [56].

### Kernvereisten per fase → wat de software afdwingt

| Fase | Vereiste | Ondersteuning in software |
|---|---|---|
| **Aanvaarding/continuering** | Opdrachtvoorwaarden schriftelijk vastgelegd vóór uitvoering, en jaarlijks nagaan of ze nog passen [5][7]. Inhoud (EK, ISRS 4410 §24): beoogd gebruik, stelsel (verwijzing naar fiscaal stelsel volstaat [6]), doel en reikwijdte, verantwoordelijkheden van accountant en management, vorm van de verklaring. Daarnaast integriteit van de cliënt, VGBA-bedreigingen en Wwft-cliëntenonderzoek | Verplichte onderdelen *Scope opdracht* en *Akkoord acceptatie*. **Uitvoering blijft geblokkeerd** zolang acceptatie, Wwft en opdrachtbevestiging niet gereed zijn. Generator voor de opdrachtbevestiging, met "getekend op" en bijlage |
| **Kennis van de entiteit** | Activiteiten, administratie en stelsel begrijpen (EK); bedrijfsbeschrijving [13] | Profiel › Cliënt, met een vrij tekstveld *bedrijfsbeschrijving* dat doorrolt naar volgend jaar |
| **Planning** | Materialiteit, initiële cijferanalyse, classificatie in belangrijke en niet-belangrijke posten [23][24] | Planningsonderdeel met materialiteitsveld, cijferanalyse (huidig, vorig, planning, delta met drempel) en per rubriek een vlag "belangrijke post" die het werkprogramma per post aanstuurt |
| **Samenstellen** | Opstellen op basis van informatie van het management. Significante aangelegenheden bespreken en vastleggen [8][9] | Correctieboekingen. **Logboek significante aangelegenheden** (wat, impact, besproken met wie en wanneer, afloop) |
| **Lezen en evalueren** | De accountant leest de samengestelde informatie. Is iets onjuist of onvolledig, dan vraagt hij aanvullende informatie, stelt hij aanpassingen voor of geeft hij de opdracht terug (EK) | Onderdeel *Evaluatie uitvoering*, correctievoorstellenlijst en een "teruggave"-status |
| **NOCLAR** | Stappen volgen bij een vermoeden van fraude of niet-naleving [4][52] | Vraag in *Evaluatie*, met een vastleggingsformulier |
| **Afronding** | Erkenning door het management (in de praktijk een **bevestigingsbrief/LOR**) [13]. Verklaring gedateerd op de afrondingsdatum [5] | Onderdeel *Bevestiging klant* met LOR-generator en registratie "getekend op". Verklaringsdatum ≥ datum LOR |
| **Rapportering** | Schriftelijke verklaring met vaste elementen: titel, geadresseerde, identificatie van de samengestelde informatie, stelsel (verwijzing naar Titel 9 Boek 2 BW volstaat [6]), verantwoordelijkheden, geen verificatie, datum, naam en adres [5][43]. Een continuïteitsparagraaf in de verklaring is niet voorgeschreven [59] | Verklaringvarianten in de standaardtekstbibliotheek ([§2.6](#26-standaardteksten-bibliotheek)) |
| **Documentatie** | Significante aangelegenheden en hun afhandeling, **aansluiting van de samengestelde informatie op de onderliggende vastleggingen**, dossier tijdig samengevoegd na de verklaringsdatum [5]. De tijdelijke commissie 4410 kwam uit op **11 aspecten die altijd** en een aantal die **afhankelijk van omstandigheden** in het dossier moeten staan (Bijlage 1 van HR 1136) [10][11] | De aansluiting proefbalans → rubrieken → rapport is in de software aantoonbaar (rubriceringsaudit, controles C01–C07 uit [hoofdstuk 5](05-functioneel-ontwerp.md)). **Een dossiercheck tegen de lijst uit Bijlage 1 van HR 1136** wordt een verplicht onderdeel van *Afsluiting* |

## 2.2 Ethiek (VGBA) en Wwft

- **Fundamentele beginselen** (VGBA, EK): integriteit, objectiviteit, deskundigheid en zorgvuldigheid, vertrouwelijkheid, professioneel gedrag. Die staan letterlijk in je schermafbeelding *Scope opdracht › Fundamentele beginselen*.
  - De VGBA is in 2024 gewijzigd [53]; de huidige tekst is (O).
  - De ViO (onafhankelijkheid) geldt alleen voor assurance. Bij samenstellen gaat het om objectiviteit en een bedreigingenanalyse (EK).
- **Wwft** [18][19]:
  - **op kantoorniveau**: risicoanalyse en risicobeleid;
  - **per cliënt**: identificatie en verificatie (cliënt en vertegenwoordiger), UBO en zeggenschapsstructuur, doel en aard van de relatie, doorlopende monitoring, vereenvoudigd of verscherpt onderzoek (PEP's, hoog-risicolanden);
  - **meldplicht** van ongebruikelijke transacties aan FIU-Nederland (objectieve en subjectieve indicatoren);
  - **terugmeldplicht** bij een afwijking met het UBO-register (art. 10c);
  - **bewaartermijn 5 jaar na einde van de relatie** (art. 33);
  - sinds 1-8-2024 moet het UBO-uittreksel via de cliënt worden opgevraagd [20]. De huidige stand is (O).
- **Gevolg voor de software:** een Wwft-module per klant (niet per dossier), met risicoprofiel, UBO's, identificatiedocumenten (bijlage), datum van laatste herbeoordeling en een signaal in de klantenmonitor als die te oud is. Bij het dossier hoort een vraag: "Is het risicoprofiel onveranderd?". Vergelijk de schermafbeelding: *"Het risicoprofiel is onveranderd, op grond waarvan een verscherpt cliëntenonderzoek niet noodzakelijk wordt geacht"*.

## 2.3 Kwaliteitsmanagement, dossierafsluiting en bewaartermijn

- Voor kantoren zonder Wta-vergunning geldt nu nog de **NVKS**.
- **NV KM, SKM1 en SKM3N** zijn vastgesteld op 30-1-2025 en worden **per 1-1-2027 verplicht voor alle overige kantoren** [15][16].
- **Termijn dossierafsluiting:**
  - de NVKS (art. 12) noemt **uiterlijk twee maanden** na de datum van de verklaring [14];
  - Visionplanner sluit automatisch na **60 dagen** [9][14 in hfst. 1];
  - of NV KM/SKM1 voor samenstellen dezelfde termijn kent, is (O);
  - **in de software instelbaar maken**, standaard 60 dagen.
- **Bewaartermijn:**
  - minimaal **7 jaar** voor opdrachtdocumentatie [5];
  - fiscale bewaarplicht van 7 jaar (art. 52 AWR, EK);
  - Wwft-gegevens 5 jaar na einde relatie.
- **Na afsluiting** zijn wijzigingen alleen mogelijk als gedocumenteerd addendum [5]. Dat sluit aan op het Visionplanner-gedrag "niet heropenen, wel kopiëren".
- **Dossierreview**: de vragenlijst van de Raad voor Toezicht volgt aanvaarding, planning, uitvoering/evaluatie, rapportering/communicatie en documentatie [12]. Een review-checklist per dossier en een kantooroverzicht voor de SKM1-monitoring zijn nuttig, maar hebben lagere prioriteit.

## 2.4 Werkprogramma samenstellen

- Het **SRA-werkprogramma** is risicogericht en werkt met **sturende vragen en vastgelegde beslissingen** die naar een werkprogramma op maat leiden. Er is een apart spoor voor administratieve dienstverlening, en het wordt jaarlijks geactualiseerd [21][22][57].
  - Het is in licentie beschikbaar in Caseware, Exact, Infine, Nmbrs, Silverfin, Visma en andere pakketten. **De inhoud zit achter een SRA-login. Opnemen in eigen software vraagt een licentie van SRA (O).**
- De standaard schrijft geen stappen per post voor. Verplicht zijn de documentatie-elementen; de diepgang per post is een risicogerichte keuze die je vastlegt [26][23].
- **Consequentie:** de software levert een **werkprogramma-engine** (sjablonen, stappen, voorwaarden, verplichtingen). Het kantoor vult die met:
  - **(a)** een eigen kantoorwerkprogramma (de basisversie stellen we samen op basis van de standaard en de onderstaande stappen), of
  - **(b)** een gelicentieerd programma (SRA/NOAB/Novak), mits de licentiegever dat toestaat.

**Basisopzet eigen werkprogramma** (EK, laten valideren door de vaktechnisch verantwoordelijke):

| Fase | Onderdelen |
|---|---|
| Aanvaarding/continuering | Wwft-cliëntenonderzoek, integriteit, VGBA-bedreigingen, deskundigheid en capaciteit, opdrachtbevestiging (nieuw of nog actueel), NOCLAR-historie |
| Planning | Bedrijfsbeschrijving, stelsel (BW2 micro/klein, commercieel of fiscaal; IB), grootteclassificatie, materialiteit, initiële cijferanalyse, classificatie van posten, significante aangelegenheden |
| Uitvoering per post | **IVA/MVA**: verloopoverzicht aansluiten, investeringen en desinvesteringen met facturen, afschrijvingen, bijzondere waardevermindering. **FVA**: deelnemingen, leningen u/g. **Voorraden/OHW**: opnamestaat, waardering, incourantheid. **Debiteuren**: ouderdomsanalyse, voorziening, betalingen na balansdatum. **Liquide middelen**: aansluiting bankafschriften per balansdatum, kruisposten. **Eigen vermogen**: verloop, resultaatbestemming, kapitaal volgens statuten; bij IB de kapitaalsvergelijking. **Voorzieningen**. **Langlopende schulden**: overeenkomsten, aflossingen, kortlopend deel, zekerheden. **Kortlopende schulden**: crediteuren, aansluiting btw- en loonheffingaangiften, overlopende passiva. **Omzet en kosten**: cijferanalyse t.o.v. vorig jaar en planning, brutomarge, btw-omzetaansluiting, loonkosten t.o.v. loonjournaal, privégebruik auto. **Belastingen**: Vpb-berekening |
| Afronding | Gebeurtenissen na balansdatum, continuïteit, lezen van de conceptjaarrekening, significante aangelegenheden bespreken, correctievoorstellen, LOR, review, verklaring, datering, dossier sluiten |

## 2.5 Inhoud en opbouw van het financieel rapport

### Grootteclassificatie (boekjaren vanaf 1-1-2024)

| Categorie | Balanstotaal | Netto-omzet | Gem. aantal werknemers |
|---|---|---|---|
| **Micro** (art. 2:395a) | ≤ € 450.000 | ≤ € 900.000 | < 10 |
| **Klein** (art. 2:396) | ≤ € 7,5 mln | ≤ € 15 mln | < 50 |
| **Middelgroot** (art. 2:397) | ≤ € 25 mln | ≤ € 50 mln | < 250 (EK) |

- Een rechtspersoon valt in een categorie als hij op **twee opeenvolgende balansdata aan twee van de drie criteria** voldoet [27].
- Middelgroot betekent **controleplicht**. Er is tuchtrecht (2026) over samenstellers die meewerkten aan het ontduiken daarvan [51].
- **Gevolg voor de software:** de classificatie over meerdere jaren bewaken (gegevens uit vorige dossiers), met een waarschuwing. De grensbedragen staan als parameters per boekjaar in de beheertabellen.

### Per rechtsvorm en grootte

| Variant | Wat het rapport moet bevatten |
|---|---|
| **BV micro** (2:395a) | Balans en W&V **zonder toelichting**, met een beperkt aantal totaalposten [28][29]. Fiscale grondslagen zijn toegestaan (2:395a lid 7); dan wel vermelden welke [28][29], zie RJk D3.1. Vermeldingen onder de balans: niet in de balans opgenomen verplichtingen, voorschotten en leningen aan bestuurders (EK, O). Geen waardering tegen actuele waarde |
| **BV klein** (2:396) | Balans, W&V en toelichting. Fiscale grondslagen mogelijk via 2:396 lid 6 en het Besluit fiscale waarderingsgrondslagen, dan *alle* relevante fiscale grondslagen toepassen en vermelden [30], zie RJk D3.2 [33][35]. Modellen volgens het **Besluit modellen jaarrekening** (balans model A/B, W&V model E–H) [32]; welke modellen nu precies gelden voor klein is (O). **Toelichtingen** (EK, artikelen verifiëren): grondslagen, gemiddeld aantal werknemers (2:382), niet in de balans opgenomen verplichtingen (2:381), schulden met looptijd > 5 jaar en zekerheden (2:375), gebeurtenissen na balansdatum (2:380a), leningen aan bestuurders (2:383), resultaatbestemming (2:380c), verloop eigen vermogen (2:378), ondertekening door alle bestuurders (2:210 lid 5). **Verloopoverzicht vaste activa** (2:368): of kleine rechtspersonen hiervan zijn vrijgesteld is (O) [31][33] |
| **Eenmanszaak / VOF** (IB) | Geen BW2-inrichtingseisen; in de praktijk fiscale grondslagen (goed koopmansgebruik, EK). Er zijn **geen NBA-modelrapporten** voor IB [43]. Wel nodig: balans en W&V op fiscale grondslagen, **kapitaalsvergelijking per ondernemer of vennoot** (beginkapitaal, privéstortingen, privéopnamen inclusief bijtelling auto, resultaataandeel, eindkapitaal), **winstverdeling VOF** (arbeidsbeloning, rente over kapitaal, rest volgens verdeelsleutel) en een fiscale winstberekening als aansluiting met de aangifte (EK). Standaard 4410 is vrijwillig toe te passen op een "aangifte IB-plus"-bijlage [6][42] |
| **Stichting / vereniging** | RJk C1/C2 (en RJ 640 voor organisaties zonder winststreven) [hfst. 1, 28]. Pas in een latere fase |

**Referentiemateriaal:** de **NBA-voorbeeldrapporten 2025** (december 2025, voor leden via MijnNBA): micro, klein en middelgroot commercieel, klein fiscaal, plus voorbeelden van accountantsrapportages [34]. Voor 2023 bestaan "De Kleine B.V." commercieel en fiscaal als Word-bestand [33]. **Gebruik deze als basis voor onze rapportsjablonen en rubriekenschema's.**

### Voorbeeld-inhoudsopgave samengesteld financieel rapport (BV klein)

```
  Samenstellingsverklaring van de accountant
1 Algemeen            – opdracht, oprichting en activiteiten, bestuur, fiscale eenheid, continuïteit
2 Resultaten en financiële positie
  2.1 Resultaatvergelijking met vorig jaar (bedrag en %)
  2.2 Analyse brutomarge en kosten
  2.3 Financiële structuur
  2.4 Kengetallen     – current ratio, quick ratio, solvabiliteit, rentabiliteit EV, werkkapitaal
3 Fiscale positie     – berekening fiscale winst en Vpb, verrekenbare verliezen
4 Jaarrekening
  4.1 Balans per 31 december
  4.2 Winst-en-verliesrekening
  4.3 Grondslagen van waardering en resultaatbepaling
  4.4 Toelichting op de balans (met verloopoverzichten vaste activa en eigen vermogen)
  4.5 Niet in de balans opgenomen verplichtingen
  4.6 Toelichting op de winst-en-verliesrekening
  4.7 Overige toelichtingen (werknemers, gebeurtenissen na balansdatum, resultaatbestemming, ondertekening)
5 Overige gegevens    – statutaire winstbestemming
6 Bijlagen/specificaties – debiteuren, crediteuren, overlopende posten, btw-aansluiting
```

- Bij **micro** vervallen 4.3–4.7 grotendeels.
- Bij **IB** vervalt 5, en komen de kapitaalsvergelijking en winstverdeling in plaats van de resultaatbestemming (EK).
- Je schermafbeelding *"Specificaties van balans › Immateriële vaste activa"* hoort in hoofdstuk 6 (specificaties) of in 4.4.

## 2.6 Standaardteksten: bibliotheek

### Waar de officiële teksten staan
- de **NBA-verklaringengenerator** (generator.nba.nl), ook met Engelse vertaling [37];
- NBA *Voorbeeldteksten en verklaringen* [36];
- HRA deel 3 voorbeeldteksten (o.a. opdrachtbevestiging) [44];
- de FAQ Standaard 4410 [6] en de FAQ Continuïteit [46];
- de NBA-voorbeeldrapporten [33][34].

### Samenstellingsverklaring: opbouw (BW2-jaarrekening, klein)

> ⚠️ De tekst hieronder is **gereconstrueerd uit gepubliceerde jaarrekeningen en vakartikelen**,
> niet letterlijk overgenomen uit een NBA-bron. **Neem de definitieve tekst over uit de
> NBA-verklaringengenerator** [37] voordat je hem als standaardtekst gebruikt.

```
Samenstellingsverklaring van de accountant
Aan: {{ geadresseerde }}

De jaarrekening van {{ klant.naam }} te {{ klant.plaats }} is door ons samengesteld op basis van
de van u gekregen informatie. De jaarrekening bestaat uit de balans per {{ boekjaar.einddatum }}
en de winst-en-verliesrekening over {{ boekjaar.jaar }} met de toelichting, waarin een overzicht
van de gehanteerde grondslagen voor financiële verslaggeving en andere toelichtingen zijn opgenomen.
                                                               [bronnen: 38, 39; slot EK]
Deze samenstellingsopdracht is door ons uitgevoerd volgens het Nederlands recht, waaronder de
voor accountants geldende Standaard 4410, 'Samenstellingsopdrachten'. Volgens deze standaard
hebben wij u ondersteund bij het opstellen en presenteren van de jaarrekening in overeenstemming
met Titel 9 Boek 2 BW. …                                       [38][43][6]

Bij een samenstellingsopdracht bent u er verantwoordelijk voor dat de informatie klopt en dat u
ons alle relevante informatie aanlevert. … Daarom hebben wij geen controle- of
beoordelingswerkzaamheden uitgevoerd die ons in staat stellen een oordeel of een conclusie te
geven over de getrouwheid van de jaarrekening.                  [38][39][40]

Relevante ethische voorschriften – … (VGBA) …                  [40; formulering (O)]

{{ kantoor.plaats }}, {{ verklaring.datum }}
{{ kantoor.naam }} – {{ accountant.naam }} {{ accountant.titel }}
```

**Varianten:**
- *Micro*: zonder verwijzing naar de toelichting, met vermelding van de grondslagen [41][42].
- *IB-ondernemer*: de micro-tekst met "de in het rapport vermelde (fiscale) grondslagen" in plaats van "Titel 9 Boek 2 BW" [42] (EK/O).
- *Fiscale grondslagen BV*: met vermelding van 2:396 lid 6 en RJk D3 (EK).

### Overige standaardteksten (inhoud te laten opstellen en valideren)

| Categorie | Teksten |
|---|---|
| Grondslagen | Algemeen/stelsel (BW2-RJk of fiscaal), vergelijkende cijfers, schattingen, IVA, MVA, FVA/deelnemingen, voorraden, vorderingen, liquide middelen, voorzieningen, schulden, netto-omzet, kosten, personeelsbeloningen/pensioen, belastingen (EK) |
| Toelichting | Continuïteit (normaal en met onzekerheid) [46][59], gebeurtenissen na balansdatum, niet in de balans opgenomen verplichtingen, werknemers, resultaatbestemming, ondertekening |
| Brieven | **Opdrachtbevestiging samenstellen** (verplichte elementen, algemene voorwaarden, AVG-rol, Wwft-voorbehoud) [44][45]; **bevestigingsbrief/LOR** (verantwoordelijkheid, volledigheid, alle transacties verwerkt, gebeurtenissen na balansdatum, verplichtingen en claims, continuïteit, fraude/NOCLAR, verbonden partijen, ondertekening door alle bestuurders) [13] |
| Dossier | Standaardantwoorden voor acceptatie (fundamentele beginselen, integriteit, Wwft, opdrachtaanvaarding), zoals in je schermafbeelding *Scope opdracht* |

**Technische eisen aan de bibliotheek:**
- variabelen en voorwaarden (Liquid), zie [hoofdstuk 7](07-technische-architectuur.md);
- **versies per regelgevingsjaar** (een oud dossier houdt zijn oorspronkelijke tekst);
- per tekst het kenmerk "geldig voor" (rechtsvorm, grootte, stelsel, opdrachttype);
- een dossierspecifieke override, met de optie "terug naar standaard".

## 2.7 Opdrachttypen

| Type | Kader | Gevolg in de software |
|---|---|---|
| **Samenstellingsopdracht** | Standaard 4410 / HR 1136 | Volledig werkprogramma, verklaring BW2, micro of IB [5] |
| **Administratieve dienstverlening** | Geen standaard tot en met saldibalans | Licht werkprogramma, geen verklaring [21][25] |
| **Aangifte-plus / IB-plus** ("TAX – Aangifte plus" uit je schermafbeelding) | Geen 4410, maar tuchtrecht trekt het zorgvuldigheidskader van 4410 erbij [47] | Werkprogramma *aangifte plus* met minimale documentatie; rapport zonder of met vrijwillige 4410-verklaring |
| **Overeengekomen specifieke werkzaamheden** | Herziene **Standaard 4400** (sinds 1-1-2024) | Rapport van feitelijke bevindingen. Lagere prioriteit [49] |
| **Overige opdrachten** | **HR 1111** (herzien 11-2-2026, met beslisboom) | De opdrachtwizard volgt de beslisboom om het opdrachttype te bepalen [48] |
| Samenloop met controle of beoordeling door dezelfde accountant | 4410 niet verplicht | Geen samenstellingsverklaring [50] |

## 2.8 Samenvatting: functionele eisen uit dit hoofdstuk

1. **Opdrachtwizard**: opdrachttype en stelsel (BW2 micro/klein, commercieel of fiscaal; IB; 4400; overig). Die keuze bepaalt werkprogramma, checklists en rapportsjabloon [48].
2. **Grootteclassificatie** over twee jaar, met grensbedragen als parameters en een **waarschuwing bij controleplicht** [27][51].
3. **Acceptatie en Wwft** op klantniveau, met blokkade van de uitvoering zolang ze niet gereed zijn [18][19][7].
4. **Opdrachtbevestiging- en LOR-generator** met registratie van de ondertekening [5][13].
5. **Planning**: materialiteit, initiële cijferanalyse, classificatie van belangrijke posten [23].
6. **Configureerbaar werkprogramma** met sturende vragen, voorwaarden en verplichte stappen. Ruimte om een gelicentieerd programma in te laden [21].
7. **Logboek significante aangelegenheden** en een correctievoorstellenlijst [8][9].
8. **Aantoonbare aansluiting** proefbalans → rubrieken → rapport, met bijlagen [5].
9. **Afronding**: checklists voor gebeurtenissen na balansdatum, continuïteit en NOCLAR, aftekening per onderdeel (opsteller, reviewer, accountant).
10. **Rapportgenerator** met modellen per rechtsvorm en grootte, grondslagenbibliotheek, verloopoverzichten, kapitaalsvergelijking en winstverdeling, kengetallen, fiscale paragraaf en verklaringsvarianten.
11. **Dossierafsluiting** met een instelbare termijn (standaard 60 dagen, NVKS: uiterlijk 2 maanden), alleen-lezen na sluiten, addendum gelogd, bewaartermijn 7 jaar [14][5].
12. **Versiebeheer** van teksten en checklists per regelgevingsversie [17].

## Open vragen (vaktechnisch)

1. De letterlijke actuele NBA-modelteksten van de samenstellingsverklaring (BW2 klein, micro, IB, fiscaal): ophalen uit de verklaringengenerator [37].
2. De volledige recapitulatie van documentatievereisten in Bijlage 1 van HR 1136 [10]. Bronnen spreken elkaar tegen over het aantal conditionele aspecten.
3. Komt er een nieuwe HR 1136, en wat verandert de NV COS/ISQM-aanpassing (2026) aan 4410 [17]?
4. Afsluittermijn en bewaartermijn onder NV KM/SKM1 [14].
5. Is een kleine rechtspersoon vrijgesteld van het verloopoverzicht (2:368)? Welke modellen uit het Besluit modellen jaarrekening gelden nu voor klein en micro [31][32]?
6. Licentievoorwaarden van de SRA-, NOAB- en Novak-werkprogramma's voor gebruik in eigen software.
7. De actuele VGBA-tekst en de UBO-registertoegang voor accountants [20][53].

## Bronnen

1. https://www.nba.nl/siteassets/wet--en-regelgeving/nba-handreikingen/1136/nba-handreiking-1136-standaard-4410-inzake-samenstellingsopdrachten-versie-19031.pdf
2. https://www.accountant.nl/vaktechniek/2015/1/herziene-standaard-4410-samenstellingsopdrachten/
3. https://www.accountancyvanmorgen.nl/2016/03/31/standaard-4410-definitief-verplicht-voor-micro-entiteiten/
4. https://www.nba.nl/globalassets/wet--en-regelgeving/noclar/standaard-4410---consultatiedocument--v-180209.pdf
5. https://www.nba.nl/wet--en-regelgeving/hra/1619/10430/10543/
6. https://www.nba.nl/tools-en-ondersteuning/faqs-en-helpdesk/faq-standaard-4410/
7. https://www.accountant.nl/vaktechniek/2015/11/nieuwe-opdrachtbevestiging-voor-doorlopende-samenstellingsopdrachten-bij-toepassing-standaard-4410h/
8. https://www.accountant.nl/vaktechniek/2026/9/hoe-ga-je-om-met-significante-aangelegenheden-bij-samenstellingsopdrachten/
9. https://www.accountancyvanmorgen.nl/2025/01/15/significante-aangelegenheid-een-interessante-samenstelcasus/
10. https://www.accountant.nl/siteassets/nba/diversen/rapportage_tijdelijke_commissie_over_toepassing_en_documentatie_standaard_4410.pdf
11. https://www.accountant.nl/nieuws/2016/3/commissie-4410-geeft-duidelijkheid-over-documentatie-eisen/
12. https://www.accountant.nl/siteassets/accountant.nl/diversen/vragenlijst.pdf
13. https://www.sra.nl/vaktechniek/accountancy/samenstellen/praktijkhandreikingen/praktijkhandreiking-ervaringen-met-risicogericht-samenstellen
14. https://www.nba.nl/globalassets/wet--en-regelgeving/noclar/consultatiedocument-wijzigingen-nvks---v-180209-002.pdf
15. https://www.sra.nl/nieuwsoverzicht/2025/van-kwaliteitsbeheersings-naar-kwaliteitsmanagementsysteem-begin-op-tijd
16. https://www.sra.nl/nieuwsoverzicht/2026/alle-kantoren-nu-aan-de-slag-met-skm1
17. https://www.nba.nl/siteassets/wet--en-regelgeving/consultaties-en-projecten/2026/consultatie-aanpassing-standaarden-naar-aanleiding-van-skm1/consultatiedocument-aanpassing-nv-cos-aan-isqm.pdf
18. https://www.nba.nl/siteassets/wet--en-regelgeving/nba-handreikingen/1124/nba-handreiking-1124-richtsnoeren-voor-de-interpretatie-van-de-wwft.pdf
19. https://www.bureauft.nl/media/yptl4rbn/stappenplan-wwft.pdf
20. https://www.accountant.nl/nieuws/2024/8/accountant-moet-ubo-uittreksel-voorlopig-opvragen-via-de-klant/
21. https://www.sra.nl/advisering/vaktechniek/werkprogrammas/samenstellen/kenmerken-sra-werkprogrammas-samenstellen
22. https://www.sra.nl/nieuwsoverzicht/2023/sra-handboek-samenstellings--en-adviespraktijk-herzien
23. https://www.sra.nl/vaktechniek/tooling/werkprogrammas/samenstellen/sra-nmbrs-samenstellen
24. https://www.accountant.nl/nieuws/2016/6/handreiking-over-samenstellingsopdrachten-gepubliceerd/
25. https://www.accountancyvanmorgen.nl/2015/03/11/een-analyse-van-de-wijzigingen-in-de-herziene-standaard-4410/
26. https://www.nba.nl/tools-en-ondersteuning/publicaties/2015/risicogerichte-aanpak-samenstellingsopdracht/
27. https://zoek.officielebekendmakingen.nl/stb-2024-52.html
28. https://www.sdu.nl/content/commentaar-op-burgerlijk-wetboek-boek-2-art-395a-or-algemeen
29. https://www.advocatenorde.nl/wetgevingsadviezen/jaarrekening-van-kleine-rechtspersonen
30. https://zoek.officielebekendmakingen.nl/kst-31136-3.pdf
31. https://wetboekplus.nl/burgerlijk-wetboek-boek-2-artikel-368-sluitend-overzicht/
32. https://zoek.officielebekendmakingen.nl/stb-1983-666.pdf
33. https://www.nba.nl/siteassets/tools-en-ondersteuning/tools-en-modellen/voorbeeldrapporten/2023/nba-klein-nl-fiscaal-inrichtingsjaarrekening-20232.docm
34. https://www.nba.nl/nieuws/2025/december/voorbeeldrapporten-2025-gepubliceerd/
35. https://www.accountancyvanmorgen.nl/2020/01/16/rj-uiting-handreiking-bij-de-toepassing-van-fiscale-waarderingsgrondslagen-aangepast/
36. https://www.nba.nl/tools-en-ondersteuning/voorbeeldteksten/voorbeeldteksten-en-verklaringen/
37. https://www.nba.nl/tools-en-ondersteuning/voorbeeldteksten/informatie-over-de-verklaringengenerator/
38. https://privacyfirst.nl/wp-content/uploads/Jaarrekening_Privacy_First_2025.pdf
39. https://stichtingpresent.nl/veenendaal/wp-content/uploads/sites/71/2024/04/Stichting-Present-Veenendaal-jaarrekening-2023.pdf
40. https://preproduction.accountant.nl/siteassets/accountant.nl/blad/2014-nr1.-12/acc_2014_12_samenstellingsverklaring.pdf
41. https://www.accountant.nl/nieuws/2016/5/nba-komt-met-samenstelverklaring-bij-micro-entiteiten/
42. https://www.accountancyvanmorgen.nl/?p=755
43. https://www.visionplanner.com/hubfs/Notulen%20Vakgroep%20Accountancy%20Visionplanner%20Bijeenkomst%2031.pdf
44. https://www.sra.nl/~/media/srawebsite/documenten/openbaar/vaktechniek/hra-deel-3-voorbeeldteksten-versie-23-februari-2015.pdf
45. https://www.accountancyvanmorgen.nl/2015/11/04/nba-publiceert-engelstalige-opdrachtbevestiging-volgens-standaard-4410h/
46. https://www.nba.nl/tools-en-ondersteuning/faqs-en-helpdesk/faqs-continuiteit/
47. https://www.accountancyvanmorgen.nl/2019/07/02/een-aangifte-plus-opdracht-is-toch-geen-samenstelopdracht/
48. https://www.nba.nl/wet--en-regelgeving/handreikingen/nba-handreiking-1111/
49. https://www.accountancyvanmorgen.nl/2024/09/26/belangrijke-veranderingen-in-de-herziene-standaard-4400/
50. https://www.accountant.nl/nieuws/2016/1/geen-samenstelverklaring-bij-samenloop-met-controle--of-beoordelingsopdracht-door-zelfde-accountant/
51. https://www.accountant.nl/tucht/2026/7/samenstellers-werken-mee-aan-ontduiken-controleplicht/
52. https://www.accountant.nl/tucht/2023/10/samensteller-volgt-noclar-stappen-na-melding-over-mogelijke-fraude/
53. https://nba.nl/siteassets/wet--en-regelgeving/consultaties-en-projecten/2024/wijzigingsvoorstellen-vio--vgba/consultatiedocument-wijzigingsvoorstellen-vio---vgba-240705.pdf
56. https://www.accountant.nl/vaktechniek/2012/10/samenstellingsverklaringen/
57. https://www.sra.nl/vaktechniek/accountancy/samenstellen/handboek/werkprogrammas
59. https://www.accountancyvanmorgen.nl/2016/06/13/uitdagingen-jaarrekening-microrechtspersoon/
