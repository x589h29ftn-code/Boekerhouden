# 1. Hoe werkt Visionplanner Compilation?

> **Bronnen en betrouwbaarheid.** Tijdens dit onderzoek konden we de helpdesk-pagina's
> (support.reporting.nmbrs.com, hs.visionplanner.com) niet rechtstreeks openen. We hebben ze
> gevonden en samengevat via een zoekmachine. Elke bewering heeft een bronnummer
> ([lijst onderaan](#bronnen)). Wat we zelf afleiden uit de schermafbeeldingen staat als
> **[afgeleid]**, wat we niet konden bevestigen als **[onbevestigd]**. Die punten staan ook bij de
> [open vragen](#open-vragen). Ze zijn het best te verifiëren in een demo- of proefomgeving.

## 1.1 Eerst: welk product is het precies?

- **Visionplanner Compilation** is de cloud-opvolger van *MLE Samenstel*. Het is geschikt voor alle groottes, van IB-ondernemer ("IB-plus") tot grote rechtspersonen [1][3][4].
  - Visionplanner Samenstel (kleine entiteiten) en MLE Samenstel zijn hierin samengevoegd.
- **Compilation First Edition** is de oudere cloudmodule (voorheen *Samenstelpraktijk*). Die is alleen bedoeld voor micro- en kleine rechtspersonen [1][2].
- **Naam en eigenaar:** sinds 1 september 2026 heet Visionplanner **Nmbrs Reporting**, na de samenvoeging met Yuki, WeFact en Nmbrs [5][6]. Daarom staat er in je schermafbeeldingen "nmbrs REPORTING / COMPILATION".
- **Modules:** Core (klanten, administraties, koppelingen), Compilation, Compilation First Edition, Insights (dashboards), VAIA (AI) en Audit [2].
- **Abonnementen:** *Compilation Professional* (zzp, eenmanszaak, vof) en *Compilation Premium* (kleine bv en stichting) [2]. Dat verklaart de kolom *Abonnement* in de klantenmonitor **[afgeleid]**.
- **Jouw schermafbeeldingen tonen de nieuwe Compilation**, niet First Edition. De menupaden *Profiel › Opdracht*, *Profiel › Rapport* en de knop *Specificatie post* komen uit de Compilation-artikelen [10][11][33] **[afgeleid]**.

## 1.2 De architectuur in het kort

```
                    ┌──────────────────────── Core (klantniveau) ─────────────────────────┐
 Boekhoudpakket ──► │ Administratie: rekeningschema + saldi per periode per boekjaar      │
 (XAF / Excel /     │ Koppelscherm: grootboekrekening → rubriek (sjabloon + klant-override)│
  API-koppeling)    │ Klantenmonitor: alerts, import, % gekoppeld, …                       │
                    └───────────────┬─────────────────────────────────────────────────────┘
                                    │ Importeren › "Cijfers huidig" / "Cijfers vorig"
                    ┌───────────────▼───────── Compilation (dossierniveau) ───────────────┐
                    │ Dossier per boekjaar + aard opdracht, op basis van werkprogramma     │
                    │ Profiel → Voorbereiding (Acceptatie, Planning) → Uitvoering per post │
                    │ → Rapport → Afronding; per onderdeel tab "Dossier" en tab "Rapport"  │
                    │ Rapportsjabloon + standaardteksten → PDF / SBR-instance              │
                    └──────────────────────────────────────────────────────────────────────┘
```

- **Import en rubricering gebeuren op administratieniveau in Core, niet in het dossier.** Het dossier haalt de cijfers op via *Importeren › Cijfers huidig* en *Importeren › Cijfers vorig* [10][41][47][82] **[afgeleid uit meerdere artikelen]**.
- **Documenten** staan bij Visionplanner in SharePoint (via een Azure-app-registratie). Het standaardpad is `{adviser.site}/{adviser.library}/{client.name}/{dossier.name}`, met de submappen *Bijlagen* en *Rapporten* [35][36]. Wij gebruiken een lokale bestandsopslag.

## 1.3 De werkstroom van een samenstellingsdossier

### Aanmaken en inrichten (fase *Profiel*)
1. **Klant en administratie** aanmaken in Core (rol beheerder) [82].
2. **Dossier** aanmaken via *Apps › Compilation › Dossiers* [11].
3. **Profiel › Cliënt**: klantgegevens.
4. **Profiel › Opdracht**:
   - sectie B "Opdrachtverantwoordelijken": rollen aan medewerkers toewijzen. Na een wijziging past de software het goedkeuringsproces van het dossier direct aan [11];
   - sectie C "Boekjaar": begindatum, balansdatum, balansdatum vorige periode;
   - instellingen voor een verkort, verlengd of gebroken boekjaar [10].
5. **Profiel › Rapport**, sectie B "Financiële rapportage": rapportinstellingen zoals "Vergelijkende cijfers verbergen", "Inbrengbalans tonen" en mutatiestaten met vergelijkende cijfers [10][33].

### Aard opdracht en werkprogramma
- Het **SRA-Werkprogramma Samenstellen** zit geïntegreerd in Compilation. Het is alleen voor SRA-leden en kost € 0,69 per dossier per maand [21].
  - Het werkt met *stuurvragen en gedocumenteerde beslissingen* die naar het passende werkprogramma leiden.
  - Het is risicogericht en scheidt *administratieve dienstverlening* van *samenstellen*.
  - Het bevat fiscale werkprogramma's voor loonheffing, btw en vpb [22].
- SRA kent drie varianten [23]:
  1. Volledig.
  2. Zonder het administratieve deel.
  3. *Risicogericht samenstellen* zonder voorgeschreven uitvoeringsfase. De accountant formuleert de werkstappen per klant.
- Er is een apart SRA-werkprogramma voor de **aangifte-plus-opdracht** [24]. Daar komt "TAX – Aangifte plus" als *aard opdracht* in je schermafbeelding vandaan **[afgeleid]**.
- Kantoren beheren sinds de *Zomer Boost 2026* de beschikbare **opdrachttypen** centraal [28][29].

### Fasen
- Visionplanner noemt vier hoofdfasen [7][8]:
  1. **Acceptatie**: opdracht accepteren, Wwft-checks.
  2. **Planning**: bedrijfsverkenning, initiële cijferbeoordeling, risico-identificatie, grondslagen.
  3. **Uitvoering**: risico's uitwerken en het financieel rapport opstellen.
  4. **Afronding**: met een afrondingsmemorandum.
- In de schermafbeeldingen zijn Acceptatie en Planning gegroepeerd onder **Voorbereiding**. Daaromheen staan **Profiel** en **Rapport**. Volledige boom uit de schermafbeelding:

```
Profiel ─ Cliënt · Opdracht · Rapport
Voorbereiding ─ Acceptatie (Scope opdracht*, Akkoord acceptatie*) · Planning
Uitvoering ─ Balans activa (Overzicht activa, Immateriële vaste activa, Verloopoverzicht IVA,
             Materiële vaste activa, Verloopoverzicht MVA, Voorraden, Vorderingen, Liquide middelen)
           · Balans passiva · Resultatenrekening
Rapport ─ Financiële rapportage
Afronding ─ Evaluatie uitvoering · Bevestiging klant · Uitbrengen rapport · Afsluiting
```

### Werken per post (de kern van *Uitvoering*)
- Volgens de SRA-beschrijving [25]:
  1. Eerst bepaal je de **materialiteit**.
  2. Dan classificeer je de posten. Posten met een mogelijk materiële afwijking worden *belangrijke posten* met uitgebreidere werkzaamheden.
  3. **Per post** doe je vervolgens alles in één keer: correcties en herrubriceringen doorvoeren, het werkprogramma afwerken, grondslagen en toelichting toevoegen.
- Dat zie je terug in de schermafbeelding per post [afgeleid]:
  - **Aanpak en cijferbeoordeling**: tabel *Post/Grootboekrekening × Planning, Huidig, Vorig, Delta*, met per post een vraag en notitie ("Zijn de afschrijvingen beoordeeld en juist geboekt?").
  - **Werkprogramma**: stappen (bijv. "Verloopoverzicht") met instructie, antwoord, initialen, datum en een afvinkvak.
  - **Tab Rapport**: hoe de post in het rapport verschijnt, met specificaties.
- **Risico's** leg je alleen vast in de planningsfase. Ziet een assistent later nog een risico, dan legt hij dat terug bij de opdrachtverantwoordelijke [19].
  - First Edition maakt onderscheid tussen een *risico* (vooruitkijkend, alleen in de planning) en een *issue* (al opgetreden, in elke fase) [70].
  - Een *afsluitende* cijferbeoordeling is onder NV COS 4410 niet verplicht en staat daarom niet standaard in het programma [19].
- Sinds de *Herfst Boost 2025* kun je de **initiële cijferbeoordeling bevriezen**. Daarna zie je welke correcties tijdens het samenstellen zijn doorgevoerd [30][31]. Correcties zitten dus in het dossier, los van de import **[afgeleid]**.

### Rollen en goedkeuren
- Rollen wijs je toe in *Profiel › Opdracht*. Je mag alleen je eigen rol wijzigen of een rol die onder de jouwe valt; een beheerder mag alles [11].
- Een review-opmerking mag alleen worden verwijderd door wie hem maakte (vakgroepnotulen 2020) [18].
- **[onbevestigd]**:
  - de precieze rolnamen (bijv. uitvoerder, reviewer, opdrachtverantwoordelijke);
  - het mechaniek "Goedgekeurd door X op datum" per onderdeel. Dat zie je wel in de schermafbeeldingen;
  - de betekenis van het `*`. Waarschijnlijk betekent het *verplicht onderdeel*.

### Sluiten, heropenen en volgend jaar
- Een dossier **sluit automatisch 60 dagen na de datum van de samenstellingsverklaring** [9][14]. Dat sluit aan op de beroepsregels voor dossierafsluiting, zie [hoofdstuk 2](02-vaktechnisch-kader.md).
- Een gesloten Compilation-dossier kun je **niet heropenen**; je kunt wel een **kopie** maken [12]. In First Edition kan heropenen wel [13].
- In First Edition kies je bij een nieuw dossier een **basisdossier** (vorig jaar). "Vergelijkend boekjaar" staat dan standaard aan [64].
  - Grondslagen worden *niet* overgenomen, omdat de standaardteksten jaarlijks worden geactualiseerd [64].
- Hoe Compilation precies doorrolt (behalve *Cijfers vorig* importeren en kopiëren) is **[onbevestigd]**.

## 1.4 Administraties importeren

| Bron | Details |
|---|---|
| **XML Auditfile Financieel (XAF)** | Inlezen via een Windows-connector of als upload. Volgens de helpdesk worden XAF 2.0, 3.0 en 3.1 ondersteund, met als advies 3.1 [37][38][39][40]. Wij moeten 3.1, 3.2 én 4.0 ondersteunen, want 4.0 is vanaf 1-1-2027 verplicht bij de Belastingdienst (zie [hoofdstuk 3](03-gegevensimport-xaf-excel-rgs.md)) |
| **Excel-upload** | Vast Excel-sjabloon in een variant voor 12 of 13 perioden. Klantnaam in B3, boekjaren op rij 4, perioden op rij 5. Blokken *BALANS* en *WINST & VERLIES* zonder lege regels. **Balansregels zijn standen** (per periode of alleen de laatste periode), **W&V-regels zijn mutaties** die de software optelt. Maximaal 23 MB [41][42] |
| **Standaard rekeningschema** (op sjabloonniveau) | Excel met per rekening "Balans of V&W". Een rekening wordt alleen ingelezen als er een bedrag op staat [43] |
| *API-koppelingen (buiten scope)* | Exact Online, Visma.net, AFAS SB, AccountView, Yuki, SnelStart, Business Central, e-Boekhouden en meer [44][45][46][48] |

- **Wat er wordt ingelezen:**
  - bevestigd: rekeningnummer, omschrijving, saldi per periode, boekjaar en perioden;
  - bij sommige koppelingen ook kostenplaatsen en openstaande debiteurenposten [44].
- **Opnieuw importeren:**
  - eerder geïmporteerde boekjaren blijven bewaard; je kiest welke boekjaren je (opnieuw) importeert [45][46];
  - bij Excel steeds hetzelfde bestand gebruiken, anders overschrijf je de cijfers van vorig jaar [42];
  - in de oudere Visionplanner kon je perioden "uitzetten". Die worden dan niet meer overschreven en zijn niet meer te journaliseren [61].
- **[onbevestigd]**:
  - wat er bij een herimport met dossiercorrecties gebeurt;
  - of transacties worden gebruikt voor drill-down.

## 1.5 Rubricering: koppelen van grootboekrekeningen

1. **Structuur:** groep › subgroep › rubriek. Je koppelt een rekening altijd aan een **rubriek, het laagste niveau**, nooit aan een groep [47][49].
2. **Koppelscherm:**
   - links de grootboekrekeningen, rechts de rubriekenstructuur;
   - slepen of pijlknop, meervoudige selectie met Ctrl/Shift, ontkoppelen met het kruisje [47][48];
   - **alleen rekeningen met boekingen worden getoond** [48][49].
3. **Sjabloon versus administratie, met overrides:**
   - een koppeling in het **sjabloon** geldt voor alle administraties die aan dat sjabloon hangen [47];
   - een nieuwe klant neemt de sjablooninstellingen over, inclusief signalen en alerts [53];
   - een koppeling die je **in de administratie wijzigt, wordt daarna niet meer overschreven** door sjabloonwijzigingen [48];
   - rubrieken toevoegen of verplaatsen doe je bij voorkeur in het sjabloon [51];
   - de *omschrijving* van een rubriek kun je per klant aanpassen [52].
4. **RGS:**
   - rekeningen met een RGS-code worden automatisch gekoppeld;
   - onbekende of ongekoppelde rekeningen worden eerst via RGS geprobeerd, de rest koppel je handmatig [50].
5. **VAIA Mapper (AI, 2025):**
   - kent RGS-codes toe aan rekeningen die er geen hebben, zodat ze via de RGS-route automatisch in de rapportagestructuur landen;
   - de leverancier belooft "altijd 100% gekoppeld" en een besparing tot een uur per administratie [56][57][58][60];
   - deels nog als pilot [59].
6. **Gekoppeld %**:
   - het percentage gekoppelde rekeningen per klant in de klantenmonitor;
   - een klik opent het koppelscherm met alleen de ongekoppelde rekeningen [54].
7. Een rekening **splitsen** over meerdere rubrieken: **[onbevestigd]**.

> **Conclusie voor ons ontwerp:** de koppeling rekening → rubriek is een **klant-eigenschap die
> doorloopt over de jaren**. Die wordt gevoed door een kantoorsjabloon, de RGS-code of een
> handmatige keuze, waarbij een handmatige keuze voorgaat. Dat is precies het model in
> [hoofdstuk 4](04-rubricering-ontwerp.md) en [hoofdstuk 6](06-datamodel.md).

## 1.6 Rapport en jaarrekeninggenerator

- **Rapportsjablonen** stel je per kantoor in. Sinds juli 2026 kunnen ook medewerkers kiezen als er meerdere zijn [32]. Rapportonderdelen zijn genummerd (bijv. *Rapport › 90.60 Toelichting op balans*) [33].
- **Specificatie post** (de knop uit je schermafbeelding):
  - opent een formulier met specificatieregels;
  - bovenaan staan de grootboekrekeningen van de post die nog niet in de specificatie zijn verwerkt. Die sleep je naar een specificatieveld [34];
  - de specificatie kan in het dossier blijven zonder in het rapport te verschijnen, vergelijk de schakelaar *Toon in rapport* [34].
- **Mutatie- en verloopoverzicht** [33]:
  - toevoegen via het driepuntjesmenu op een specificatieregel;
  - standaard vier regels, uit te breiden;
  - tekstvakken boven en onder;
  - begin- en eindstand komen automatisch uit de balans (parameters `[Beginsaldo]` en `[Saldo]`), met een correctieregel voor de beginstand;
  - sluit het overzicht niet aan, dan staat er een **rood "verschil"**.
- **Inbrengbalans**: de balansdatum ligt één dag vóór de start van de periode. Te combineren met "Vergelijkende cijfers verbergen" [10].
- **Standaardteksten met variabelen** (bevestigd voor First Edition) [64][66]:
  - referentievelden zoals KvK-nummer, kantoornaam en datum;
  - verwijzingen naar balans- en W&V-posten met hun bedrag.
- **Samenstellingsverklaring** (First Edition): keuze uit de NBA-tekst, de NOAB-tekst of een eigen tekst [64].
- **Huisstijl**: marges, lettertype, regelafstand, kleuren en achtergrondafbeelding. Uitvoer is een PDF van het financieel verslag [64][78]. Of er een Word-export is, is **[onbevestigd]**.
- **Omvang**:
  - sinds januari 2026 ook grote rechtspersonen [74];
  - sinds juni 2026 stichtingsmodellen (RJ 640 / RJk C2) [28];
  - sinds augustus 2025 micro-zorgaanbieders [86].
- **Accorderen en ondertekenen** door de klant, digitaal via PKIsigning [65][79][80]. Dit valt voor ons buiten scope.

## 1.7 Overige functies

- **Klantenmonitor** [54]:
  - alerts (rode stip), datum laatste import, abonnementen, % gekoppeld, checklists en dossiers;
  - **Bekeken** is de datum waarop een **klantgebruiker** (de ondernemer zelf, in het klantportaal of dashboard) de klant voor het laatst opende. In onze standalone app zonder klantportaal heeft die kolom geen functie;
  - statussymbolen voor consolidatie, archief, privé en gestopt;
  - signalen en alerts stel je per sjabloon in [55]. De precieze drempelregels zijn **[onbevestigd]**.
- **Checklists** [69]:
  - groepen met vragen;
  - keuzevraag (Ja/Nee/N.v.t.) of open vraag, waarvan je het antwoord kunt overnemen in het dossier;
  - voorwaarden voor zichtbaarheid per groep of vraag;
  - de geschiktheidschecklist komt van Visionplanner of van Auxilium, Extendum, Fiscount, Novak of SRA [64].
- **Afrondingsmemorandum**: per kantoor aan te zetten, onder *Afronding*, vat het dossier samen op één pagina voor de opdrachtverantwoordelijke [68].
- **Prepared By Client (PBC)**: lijsten voor documentaanlevering door de klant, met AI-validatie [14][76][77]. Buiten scope.
- **Labels**, dossiermonitoring en achtergrondsynchronisatie tussen gelijktijdige gebruikers [14][32][81].
- **Fiscale export** (First Edition): XML naar AFAS Fiscaal, Avanzer, Fiscaal Gemak, Nextens en Fiscaal-online [65][72]. Buiten scope.
- **VAIA**: financiële analyse, vragen over cijfers, automatische bedrijfsomschrijving [59][75]. Buiten scope; eventueel later.

## 1.8 Tijdlijn releases (relevant voor functionaliteit)

| Moment | Wat |
|---|---|
| jan 2024 | VAIA gelanceerd [60] |
| apr–jul 2025 | VAIA Mapper / RGS-mapping [56][57] |
| herfst 2025 | Initiële cijferbeoordeling bevriezen; ruimere opties voor beknopte risicogerichte opdrachten [30][31] |
| jan 2026 | Jaarrekening voor grote rechtspersonen [74] |
| winter 2026 | Dossier sluit automatisch na 60 dagen; PBC met AI [14][15] |
| zomer 2026 | Opdrachttypen centraal beheren, stichtingsmodellen [28][29] |
| jul 2026 | Keuze uit rapportsjablonen voor medewerkers, achtergrondsynchronisatie [32] |
| sep 2026 | Rebranding naar Nmbrs Reporting [5] |

## 1.9 Wat nemen we over en wat niet?

| Overnemen (kern) | Vereenvoudigen | Niet (buiten scope) |
|---|---|---|
| Klantenmonitor met alerts en % gekoppeld | Kolom *Abonnement* wordt *Dossierstatus*; *Bekeken* vervalt | SBR-instance, KvK-deponering |
| Koppeling op klantniveau, gevoed door sjabloon, RGS en handmatig, met handmatige overrides | Opslag lokaal in plaats van SharePoint | API-koppelingen met boekhoudpakketten |
| Dossier per boekjaar en opdrachttype met fasenboom, verplichte onderdelen en goedkeuring | AI-mapping (VAIA) eventueel later als *voorstel* | Klantportaal, PBC, e-ondertekening |
| Per post: cijferbeoordeling, werkprogramma, specificaties, verloopoverzichten | | Fiscale XML-export |
| Rapportsjablonen, standaardteksten met variabelen, PDF | | |
| Dossier automatisch alleen-lezen na de sluittermijn; kopie in plaats van heropenen | | |

> **Let op met intellectueel eigendom.** De SRA-werkprogramma's zijn gelicentieerde content
> (alleen voor SRA-leden). Ook de teksten en vormgeving van Visionplanner/Nmbrs mogen we niet
> kopiëren. We bouwen de *functionaliteit* na en vullen die met eigen inhoud of met inhoud
> waarvoor we een licentie hebben. Zie [hoofdstuk 8](08-roadmap-risico-s-en-beslispunten.md).

## Open vragen

Deze punten zijn het best te verifiëren in een demo-omgeving van Nmbrs Reporting:

1. Rolnamen, het goedkeuringsmechaniek per onderdeel en de betekenis van het `*`.
2. Waar en hoe je journaalposten (correcties) in Compilation invoert, of er terugkerende posten zijn, en wat er bij een herimport mee gebeurt.
3. Of je een grootboekrekening over meerdere rubrieken kunt verdelen.
4. Hoe het dossier van vorig jaar precies wordt doorgerold.
5. Of je vanuit een post kunt doorklikken naar de transacties.
6. De drempelregels van de alerts.
7. Het volledige nummeringsschema van de rapportonderdelen, Word-export, kengetallen en fiscale positie in het rapport.
8. Audit trail en vergrendeling na ondertekening.

## Bronnen

[1] https://support.reporting.nmbrs.com/compilation ·
[2] https://support.reporting.nmbrs.com/welke-modules-en-abonnementen-heeft-nmbrs-reporting ·
[3] https://www.visionplanner.com/blog/visionplanner-en-mle-lanceren-visionplanner-compilation ·
[4] https://www.accountancyvanmorgen.nl/2023/07/04/ict-en-kengetallen-2023-met-visionplanner-compilation-je-hele-klantportfolio-bedienen/ ·
[5] https://www.accountant.nl/nieuws/2026/8/wefact-yuki-nmbrs-en-visionplanner-gaan-samen-verder-als-nmbrs/ ·
[6] https://support.yuki.nl/en/support/solutions/articles/80001220560-what-does-the-change-effective-september-1-entail- ·
[7] https://www.visionplanner.com/product/compilation ·
[8] https://www.visionplanner.com/product/samensteldossier ·
[9] https://www.visionplanner.com/compilation-accountantskantoor ·
[10] https://support.reporting.nmbrs.com/instellen-verkort-of-verlengd-boekjaar-in-compilation ·
[11] https://support.reporting.nmbrs.com/wijzigen-opdrachtverantwoordelijke-in-compilation ·
[12] https://support.reporting.nmbrs.com/kan-ik-een-gesloten-dossier-opnieuw-openen-in-compilation ·
[13] https://support.reporting.nmbrs.com/kan-ik-een-gesloten-dossier-opnieuw-openen-in-compilation-first-edition ·
[14] https://www.accountancyvanmorgen.nl/2026/02/09/partner-visionplanner-winter-boost-2026-meer-grip-op-processen-en-doorlooptijden-voor-accountantskantoren/ ·
[15] https://www.visionplanner.com/blog/visionplanner-winter-boost-2026-effici%C3%ABntie-voor-accountants ·
[18] https://www.visionplanner.com/hubfs/Docs/Visionplanner/Notulen%20VAV/Notulen%20Vakgroep%20Accountancy%20Visionplanner%20Bijeenkomst%2014%20d.d.%2010februari%202020.pdf ·
[19] https://www.visionplanner.com/hubfs/Docs/Visionplanner/Notulen%20VAV/Notulen%20Vakgroep%20Accountancy%20Visionplanner%20Bijeenkomst%2026%20(22%20januari%202024).pdf ·
[21] https://www.sra.nl/vaktechniek/tooling/werkprogrammas/samenstellen/sra-nmbrs-samenstellen ·
[22] https://www.sra.nl/kantoorondersteuning/vaktechniek/werkprogrammas/samenstellen/kenmerken-sra-werkprogrammas-samenstellen ·
[23] https://www.sra.nl/vaktechniek/accountancy/samenstellen/handboek/werkprogrammas ·
[24] https://www.sra.nl/vaktechniek/accountancy/samenstellen/handboek/handboek-samenstellen-online/6-de-belastingaangifte-en-de-aangifte-plus-opdracht ·
[25] https://www.sra.nl/advisering/vaktechniek/werkprogrammas/samenstellen/sra-visma-mle-samenstellen ·
[28] https://www.accountancyvanmorgen.nl/2026/06/25/partner-visionplanner-visionplanner-zomer-boost-2026-slimmer-werken-met-meer-overzicht-en-minder-gedoe/ ·
[29] https://www.visionplanner.com/blog/visionplanner-zomer-boost-2026-slimmer-werken-met-meer-overzicht-en-minder-gedoe ·
[30] https://www.accountancyvanmorgen.nl/2025/10/30/partner-visionplanner-visionplanner-herfst-boost-2025-meer-grip-minder-handwerk/ ·
[31] https://www.visionplanner.com/blog/visionplanner-herfst-boost-2025 ·
[32] https://support.reporting.nmbrs.com/releasenotes-21-juli-2026 ·
[33] https://support.reporting.nmbrs.com/toevoegen-mutatieoverzicht-in-compilation ·
[34] https://hs.visionplanner.com/nl/hoe-maak-ik-een-specificatie-aan ·
[35] https://support.reporting.nmbrs.com/instellen-sharepoint-koppeling-voor-nmbrs-compilation ·
[36] https://support.visionplanner.com/releasenotes-16-september-2025 ·
[37] https://support.reporting.nmbrs.com/auditfile-xaf ·
[38] https://hs.visionplanner.com/nl/auditfile-.xaf-algemene-koppeling-voor-veel-boekhoudpakketten ·
[39] https://www.visionplanner.com/nl/product/integraties/auditfile-xml-xaf ·
[40] https://support.visionplanner.nl/welke-auditfile-moet-ik-exporteren-vanuit-het-financi%C3%ABle-pakket ·
[41] https://support.reporting.nmbrs.com/excel ·
[42] https://support.reporting.nmbrs.com/wat-doe-ik-als-het-importeren-van-excel-niet-lukt ·
[43] https://support.reporting.nmbrs.com/toevoegen-standaard-rekeningschema ·
[44] https://cdn.e-boekhouden.nl/handleiding/Handleiding_Visionplanner.pdf ·
[45] https://support.reporting.nmbrs.com/exact-online ·
[46] https://support.reporting.nmbrs.com/davilex ·
[47] https://support.reporting.nmbrs.com/koppelen-grootboekrekeningen-in-sjabloon ·
[48] https://support.reporting.nmbrs.com/koppelen-grootboekrekeningen-in-administratie ·
[49] https://hs.visionplanner.com/nl/grootboekrekeningen-koppelen-aan-de-rapportage-structuur ·
[50] https://hs.visionplanner.com/nl/gebruik-van-rgs-in-visionplanner ·
[51] https://support.reporting.nmbrs.com/wijzigen-rubriekenstructuur ·
[52] https://support.reporting.nmbrs.com/wijzigen-omschrijving-rubriek-bij-klant ·
[53] https://support.reporting.nmbrs.com/toevoegen-klant ·
[54] https://support.reporting.nmbrs.com/wat-is-de-klantenmonitor ·
[55] https://support2.visionplanner.com/nl/een-sjabloon-aanmaken ·
[56] https://www.accountancyvanmorgen.nl/2025/04/04/partner-visionplanner-update-vaia-van-visionplanner-drie-nieuwe-functies/ ·
[57] https://www.accountancyvanmorgen.nl/2025/07/10/partner-visionplanner-visionplanner-breidt-software-uit-met-ai-betere-consolidatie-en-zorgfunctionaliteit/ ·
[58] https://hs.visionplanner.com/nl/hoe-kan-vaia-mij-helpen ·
[59] https://support.reporting.nmbrs.com/wat-is-vaia-en-wat-kan-ik-ermee ·
[60] https://www.accountancyvanmorgen.nl/2024/01/18/partner-visionplanner-visionplanner-lanceert-vaia-tijdens-winter-boost-update/ ·
[61] https://support.visionplanner.nl/journaalposten-actuele-cijfers ·
[64] https://hs.visionplanner.com/nl/samenstelpraktijk-in-visionplanner-cloud ·
[65] https://hs.visionplanner.com/compilation-first-edition ·
[66] https://support2.visionplanner.com/migration/nl/compilation ·
[68] https://support2.visionplanner.com/nl/afrondingsmemorandum ·
[69] https://support.reporting.nmbrs.com/bouwen-eigen-checklist ·
[70] https://hs.visionplanner.com/nl/verschil-issue-en-risico ·
[72] https://support.reporting.nmbrs.com/maken-en-downloaden-fiscale-export-in-compilation-first-edition ·
[74] https://www.accountancyvanmorgen.nl/2026/01/27/partner-visionplanner-sbr-jaarrekening/ ·
[75] https://www.accountancyvanmorgen.nl/2026/04/23/partner-visionplanner-lente-boost-2026-ruimte-voor-groei-door-slimme-werkprocessen/ ·
[76] https://www.accountancyvanmorgen.nl/2026/03/06/partner-visionplanner-nieuwe-pbc-standaard-van-visionplanner-vereenvoudigt-documentaanlevering/ ·
[77] https://support.reporting.nmbrs.com/maken-en-versturen-pbc-lijst ·
[78] https://www.visionplanner.com/product/administratiekantoren/jaarrekening-samenstellen-met-visionplanner ·
[79] https://support.reporting.nmbrs.com/goedkeuren-en-ondertekenen-jaarrekening-in-compilation-first-edition ·
[80] https://hs.visionplanner.com/nl/elektronisch-ondertekenen-in-visionplanner-compilation ·
[81] https://support.visionplanner.com/releasenotes-30-september-2025 ·
[82] https://support.reporting.nmbrs.com/toevoegen-administratie ·
[86] https://support.visionplanner.com/releasenotes-19-augustus-2025
