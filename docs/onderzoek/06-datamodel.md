# 6. Datamodel

> **Herzien 23-10-2026: RGS is het rubriekenschema** (B23, [hoofdstuk 12](12-rgs-als-rubriekenschema.md)). De entiteiten *Rubriekenschema*, *Rubriek* en *RgsRubriekKoppeling* vervallen; een koppeling wijst naar een RGS-code.

> **Aanvulling 23-10-2026:** het datamodel voor materiële posten, stappen en rapportpresentatie staat in [§10.6](10-werkprogramma-en-materialiteit.md#106-uitbreiding-datamodel). De gegevens die de rapporten extra nodig hebben (statuten, fiscale eenheid, aandelenkapitaal, fte en historie) staan in [§9.4](09-analyse-voorbeeldrapporten.md#94-gegevens-die-we-nodig-hebben-naast-de-proefbalans).

> Dit is een logisch model: de entiteiten en hun relaties, op het niveau dat nodig is om te
> bouwen. Veldnamen zijn Nederlands, zodat ze aansluiten op de vaktaal. Het fysieke model
> (EF Core) volgt hieruit.

## 6.1 Drie niveaus

Het model kent drie niveaus. Koppelingen "rollen door" naar het volgende jaar en sjablonen gelden voor alle klanten:

| Niveau | Wat leeft hier | Levensduur |
|---|---|---|
| **Kantoor** (beheer) | Gebruikers, RGS-versies (codes, filters, omslag), kantoorinstellingen per RGS-code, koppelsjablonen, werkprogrammasjablonen, standaardteksten, rapportsjablonen, huisstijl | Versies; wijzigingen gelden voor *nieuwe* dossiers |
| **Klant** | Klantgegevens, functionarissen en vennoten, het rekeningschema van de klant (grootboekrekeningen) en de **koppelingen** rekening→RGS-code | Doorlopend over alle boekjaren |
| **Dossier** (klant × boekjaar × opdracht) | Imports en saldi, correctieboekingen, werkprogramma-uitvoering, specificaties, verloopoverzichten, opmerkingen, bijlagen, rapportteksten en rapportversies | Eén boekjaar. Wordt gesloten en daarna alleen-lezen |

## 6.2 Diagram

```mermaid
erDiagram
    KLANT ||--o{ GROOTBOEKREKENING : heeft
    KLANT ||--o{ DOSSIER : heeft
    KLANT ||--o{ FUNCTIONARIS : heeft
    GROOTBOEKREKENING ||--o{ KOPPELING : "wordt gekoppeld via"
    RGSCODE ||--o{ KOPPELING : ontvangt
    RGSVERSIE ||--o{ RGSCODE : bevat
    RGSCODE ||--o{ RGSCODE : "is ouder van"
    RGSCODE }o--o| RGSCODE : "omslag bij tegengesteld saldo"
    RGSCODE ||--o| RGSCODEINSTELLING : "kantoorinstelling"
    KOPPELSJABLOONREGEL }o--|| RGSCODE : "wijst naar"
    KLANT ||--o{ IMPORTBATCH : "heeft per boekjaar"
    IMPORTBATCH ||--o{ SALDO : levert
    GROOTBOEKREKENING ||--o{ SALDO : ""
    DOSSIER ||--o{ CORRECTIEBOEKING : heeft
    CORRECTIEBOEKING ||--|{ CORRECTIEREGEL : bevat
    DOSSIER }o--|| WERKPROGRAMMASJABLOON : "gebaseerd op"
    WERKPROGRAMMASJABLOON ||--o{ SJABLOONONDERDEEL : bevat
    SJABLOONONDERDEEL ||--o{ SJABLOONBLOK : bevat
    DOSSIER ||--o{ DOSSIERONDERDEEL : heeft
    SJABLOONONDERDEEL ||--o{ DOSSIERONDERDEEL : "instantie van"
    DOSSIERONDERDEEL ||--o{ ANTWOORD : bevat
    DOSSIERONDERDEEL ||--o{ OPMERKING : bevat
    DOSSIERONDERDEEL ||--o{ BIJLAGE : bevat
    DOSSIER ||--o{ SPECIFICATIEREGEL : heeft
    RGSCODE ||--o{ SPECIFICATIEREGEL : specificeert
    DOSSIER ||--o{ VERLOOPREGEL : heeft
    DOSSIER ||--o{ RAPPORTVERSIE : heeft
    DOSSIER }o--o| DOSSIER : "vorig jaar"
```

## 6.3 Entiteiten

### Kantoorniveau

**Gebruiker** (ASP.NET Core Identity): Id, Gebruikersnaam, Naam, Initialen, Titel (bijv. AA/RA), Wachtwoordhash, Lockout, 2FA-gegevens, Rollen (`Medewerker`, `Reviewer`, `Accountant`, `Beheerder`), HandtekeningAfbeelding (alleen tekenbevoegden), Actief, MoetWachtwoordWijzigen.

**RgsVersie**: Id, Versie (bijv. `3.8`), Status (`Definitief`/`Bèta`), Bestandsnaam, SHA-256, IngelezenOp/Door, Actief (precies één versie is actief).

**RgsCode** (per versie, uit het hoofdblad van het officiële werkboek): Referentiecode (sleutel, bijv. `BMvaBegVvp`), Niveau (1–5), OuderCode (afgeleid uit de code), Omschrijving (kort en lang), D/C, **Omslagcode**, Sortering, Referentienummer (alleen ter informatie), Inactief, **Kiesfilters** (`Basis`, `Uitgebreid`, `EZ/VOF`, `ZZP`, `WoCo`, later `Zorg`) en **wegfilters** (`BV`, `EZ/VOF`, `Agro`, `WKR`, `Bank`, …). Zie [§12.4](12-rgs-als-rubriekenschema.md#124-het-werkboek).

**RgsFilter** (per rechtsvorm): het kiesfilter, de wegfilters en of de `Uitgebreid`-set standaard aan staat. Zie [§12.5](12-rgs-als-rubriekenschema.md#125-welke-codes-per-rechtsvorm).

*Gebouwd in plakje 3 (tabellen):*
- `RgsVersies` en `RgsCodes`: het ingelezen werkboek;
- `Koppelingen`: rekening → code (+ eigen post, versie, bron `Handmatig`/`Voorstel`/`Sjabloon`/`Bestand`);
- `EigenPosten` (per klant, onder een RGS-code van niveau 3 of 4);
- `KlantPostNamen` en `KantoorPostNamen` (eigen namen voor de jaarrekening);
- `SjabloonRegels` (kantoorsjabloon: rekeningnummer + omschrijving → code).

Het filter per rechtsvorm staat (nog) in code (`RgsSelectie`).

**RgsCodeInstelling** (kantoor, los van RGS): Referentiecode, SpecificatieVerplicht, VerloopoverzichtType (`Geen`/`VasteActiva`/`EigenVermogen`/`Voorziening`/`LanglopendeSchuld`/`RekeningCourant`), afwijkend RapportLabel per rapportvariant.

**Koppelsjabloon** (kantoorregels voor een rekeningschema dat bij meerdere klanten voorkomt): Id, Naam, regels: RekeningnummerVan/Tot of exact nummer, optioneel een omschrijvingspatroon, **Referentiecode**. Wordt aangevuld vanuit handmatige koppelingen ("opnemen in sjabloon").

**Werkprogrammasjabloon**: Id, Naam, AardOpdracht (bijv. `Samenstellen`, `Aangifte plus`), Versie, Status.

**SjabloonOnderdeel**: Id, SjabloonId, OuderId (fase → sectie → onderdeel), Titel, Fase (`Profiel`, `Voorbereiding`, `Uitvoering`, `Rapport`, `Afronding`), **Verplicht** (weergegeven als `*`), Volgorde, **Conditie** (expressie, bijv. `klant.rechtsvorm == "BV"`, `rubriek("MVA").saldo != 0`), Gekoppelde rubrieken (voor de cijfertabel), Verwijzing naar regelgeving (bijv. NV COS 4410 alinea).

**SjabloonBlok**: Id, OnderdeelId, Type (`Toelichting`, `Vraag`, `Keuzevraag`, `Werkprogrammastap`, `Cijferbeoordeling`, `Verloopoverzicht`, `Specificatie`, `Bijlageverzoek`), Tekst, Standaardantwoord(en), Volgorde, Conditie.

**Standaardtekst**: Id, Categorie (samenstellingsverklaring, grondslagen, toelichting, brieven, …), Titel, Inhoud (Liquid-sjabloon met variabelen en voorwaarden), Versie, Geldig voor (rechtsvorm/grootte/opdracht).

**Rapportsjabloon / Rapportsectie**: Id, Naam, Versie; secties met Type (`Tekst`, `Balans`, `WinstEnVerlies`, `Toelichting per rubriek`, `Specificaties`, `Kengetallen`, `Fiscale positie`, `Inhoudsopgave`, `Voorblad`), Volgorde, Conditie, verwijzing naar standaardtekst(en).

### Klantniveau

**Klant**: Id, Klantnummer, Naam, Rechtsvorm (`BV`, `NV`, `Eenmanszaak`, `VOF`, `Maatschap`, `CV`, `Stichting`, `Vereniging`, `Coöperatie`), KvK-nummer, RSIN/fiscaal nummer, Adres, SBI-code/activiteit, **Grootte** (`Micro`/`Klein`/`Middelgroot`, met de criteria uit BW2 als controle), BoekjaarEindeMaand, Verantwoordelijke (accountant), Medewerker, Wwft-risicoprofiel, Status.

**Functionaris**: Naam, Rol (`Bestuurder`, `Commissaris`, `Vennoot`, `UBO`, `Contactpersoon`), Winstaandeel % (bij VOF/maatschap), Van/Tot.

**Grootboekrekening**: Id, KlantId, Nummer, Omschrijving, BalansOfWV (uit de import), RgsCode (als de bron die meelevert), Actief. *Sleutel = (KlantId, Nummer).* Bij een herimport koppelen we hierop terug.

**Koppeling**: Id, KlantId, GrootboekrekeningId, **Referentiecode** (+ RGS-versie waaruit de code komt), **Bron** (`Handmatig`, `Koppelsjabloon`, `Bestand`, `Voorstel`), **Status** (`Voorgesteld`/`Bevestigd`), GeldigVanafBoekjaar, BevestigdDoor/Op.
Optioneel *KoppelingSplitsing*: een rekening verdeeld over meerdere RGS-codes (percentage of vast bedrag). Gebruik dit spaarzaam; een correctieboeking (herrubricering) is meestal transparanter.

### Dossierniveau

**Dossier**: Id, KlantId, Naam (bijv. "Financieel rapport 2024"), Boekjaar, Begindatum, Einddatum, AardOpdracht, WerkprogrammasjabloonId + versie, RapportsjabloonId + versie, RubriekenschemaId + versie, **Status** (`Nieuw` → `In uitvoering` → `Ter review` → `Rapport uitgebracht` → `Gesloten`), VorigDossierId, UitgebrachtOp, GeslotenOp, GeslotenDoor.

**ImportBatch**: Id, KlantId, Boekjaar, Bestandsnaam, SHA-256, Type (`Sjabloon`, `XAF`, `Handmatig`; later `Excel-profiel`), Boekjaar, Pakket (uit XAF-header), Versie, Periode van/tot, GeïmporteerdOp/Door, Controletotalen (debet = credit, aantallen), Actief (de nieuwste batch is leidend; oudere blijven bewaard voor de historie).
*Gebouwd in plakje 2:* een importbatch hoort bij **klant en boekjaar**, niet bij een dossier. Zo delen een jaarrekening- en een aangiftedossier van hetzelfde jaar één saldibalans, en kunnen historische jaren (vergelijkende cijfers, meerjarenoverzicht) worden ingelezen zonder eigen dossier. Per klant en boekjaar is hooguit één batch actief; de database dwingt dat af.

**Saldo**: ImportBatchId, GrootboekrekeningId, Periode (`0` = beginbalans, `1..12`, eventueel `13` = afsluiting; bij het importsjabloon één regel `Jaar` = eindsaldo), Debet, Credit. Het eindsaldo is afgeleid.

**Mutatie** (optioneel, voor drill-down vanuit een rubriek naar boekingen): ImportBatchId, GrootboekrekeningId, Dagboek, Boekstuknummer, Datum, Omschrijving, Bedrag, D/C, Relatie.

**Correctieboeking** (memoriaal in het dossier): Id, DossierId, Nummer, Datum, Omschrijving, Soort (`Correctie`, `Herrubricering`, `Fiscale correctie`, `Terugkerend`), Status (`Concept`/`Verwerkt`), DoorgevoerdInAdministratieKlant (ja/nee, zodat je kunt bijhouden of de klant de boeking zelf ook verwerkt). Regels: GrootboekrekeningId, Bedrag (+ debet / − credit), Omschrijving. **Validatie: de som van de regels is altijd 0.**
*Gebouwd (plakje 4):* `Correctieboeking` (Nummer per dossier, Omschrijving, Soort `Correctie`/`Herrubricering`, Terugkerend, VerwerktBijKlant, OvergenomenVanId) en `Correctieregel` (Volgorde, GrootboekrekeningId, Bedrag, Omschrijving). Geen datum (het boekjaar volgt uit het dossier) en geen status; *Terugkerend* is een vinkje in plaats van een soort. Een grootboekrekening die voor een correctie is aangemaakt, heeft `AangemaaktBijCorrectie`.

*(Vervallen: een aparte planningskolom en materialiteitsdrempel. De keuze van materiële posten ligt bij de gebruiker; zie DossierPost in [§10.6](10-werkprogramma-en-materialiteit.md#106-uitbreiding-datamodel).)*

**DossierOnderdeel**: Id, DossierId, SjabloonOnderdeelId, Status (`Open`, `Gereed`, `Goedgekeurd`, `Niet van toepassing`), UitgevoerdDoor/Op, **GoedgekeurdDoor/Op**, Vergrendeld.

**Antwoord**: DossierOnderdeelId, SjabloonBlokId, Tekst (rich text), Keuze, Afgevinkt, Initialen/Datum, VrijeNotitie.

**Opmerking** (reviewpunt): DossierOnderdeelId, Tekst, Door/Op, Status (`Open`/`Opgelost`), OpgelostDoor/Op. *Een onderdeel kan pas worden goedgekeurd als er geen open reviewpunten meer zijn.*

**Bijlage**: Id, DossierId, DossierOnderdeelId (optioneel), Hash, OrigineleNaam, Omschrijving, Door/Op.

**Specificatieregel**: DossierId, RubriekId, Omschrijving, BedragHuidig, BedragVorig, **ToonInRapport**, Volgorde, Subkop. *Controle: de som van de specificatieregels moet gelijk zijn aan het rubrieksaldo.*

**Verloopregel** (verloopoverzicht IVA, MVA, FVA, eigen vermogen, voorzieningen, langlopende schulden): DossierId, RubriekId, Omschrijving/Activum, Kolommen (Aanschafwaarde begin, Cumulatieve afschrijving begin, Investeringen, Desinvesteringen, Afschrijvingen, Afschrijvingspercentage, …). *Controle: het eindsaldo van het verloopoverzicht moet gelijk zijn aan het balanssaldo.*

**RapportTekst**: DossierId, RapportsectieId, Inhoud (dossierspecifieke aanpassing van een standaardtekst), Bron (standaardtekst-id en -versie).

**Rapportversie**: DossierId, Volgnummer, Status (`Concept`/`Definitief`), PDF-hash, AangemaaktDoor/Op.

**Taak**: KlantId/DossierId, Omschrijving, Toegewezen aan, Deadline, Status. Voedt de kolom *Open taken* in de klantenmonitor.

**AuditLog**: Tijdstip, Gebruiker, Entiteit, EntiteitId, Actie, OudeWaarde, NieuweWaarde (JSON).

## 6.4 Tekenconventie en afgeleide gegevens

- **Opslag:** debet = positief, credit = negatief, in eurocenten.
- **Proefbalans in evenwicht:** Σ alle saldi = 0.
- **Resultaat** = −Σ saldi van W&V-rekeningen (een creditsaldo is winst).
- **Weergave per rubriek** volgt het *NormaalSaldo*: activa en kosten als debet-positief, passiva en opbrengsten als credit-positief.

**Afgeleide proefbalans per dossier** (de tabel uit de schermafbeelding *Aanpak en cijferbeoordeling*):

| Kolom | Berekening |
|---|---|
| Saldo administratie | Beginsaldo + Σ mutaties t/m laatste periode (actieve importbatch) |
| Correcties | Σ correctieregels op deze rekening |
| **Huidig** | Saldo administratie + Correcties |
| **Vorig** | *Huidig* uit het (gesloten) dossier van vorig jaar, of bij het eerste jaar een aparte import of handmatige invoer. *Gebouwd:* de saldibalans van vorig jaar plus de correcties uit het dossier van vorig jaar (zelfde soort opdracht) |
| **Verschil** | Huidig − Vorig, absoluut en in % |

Rubrieksaldi zijn de som van de rekeningen per rubriek, opgeteld via de boom (rollup).
Een rekening met een **omslagrubriek** wordt per dossier toegewezen op basis van het teken van het saldo (debet of credit). Dat gebeurt op rekeningniveau, of als het rubriekenschema dat voorschrijft, op het niveau van het rubriektotaal.
