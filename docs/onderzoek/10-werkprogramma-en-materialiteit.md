# 10. Werkprogramma en materiële posten

> **Besluiten (23-10-2026):**
> 1. Het kantoor maakt een **eigen werkprogramma** in een sjabloon.
> 2. **De gebruiker kiest welke posten materieel zijn.** Het systeem rekent niets uit en doet geen voorstel.
> 3. Materieel aanvinken gebeurt **per hoofdpost**. Binnen de hoofdpost kun je **doorklikken naar de subposts** (en verder naar de grootboekrekeningen).
> 4. Een **materiële post** krijgt zijn werkprogramma in het uitvoeringsdeel. Stappen zijn per dossier **toe te voegen en te verwijderen**.
> 5. Een **niet-materiële post** heeft **geen vragen en geen werk**. Hij is alleen zichtbaar in het **rapport**, waar je de presentatie en opmaak kunt aanpassen.

## 10.1 Begrippen

| Begrip | Betekenis |
|---|---|
| **Werkprogrammasjabloon** | Het kantoorwerkprogramma per opdrachttype: *Samenstellen jaarrekening* (BV) en *Winstaangifte opstellen* (eenmanszaak; VOF later). Het heeft versies en wordt beheerd door de vaktechnisch beheerder |
| **Hoofdpost** | Een post op het niveau waarop materieel wordt gekozen, bijv. *Materiële vaste activa*, *Vorderingen*, *Liquide middelen*, *Kortlopende schulden*, *Netto-omzet*, *Personeelskosten*. Gekoppeld aan een tak van het rubriekenschema |
| **Subpost** | Een onderdeel van een hoofdpost, bijv. bij *Vorderingen*: *Handelsdebiteuren*, *Groepsmaatschappijen*, *Overlopende activa*. Dit volgt de rubriekenboom |
| **Postprogramma** | De standaardstappen voor een hoofdpost in het sjabloon. Ze worden alleen geactiveerd als de post materieel is |
| **Stap** | Eén werkprogramma-regel: instructie, antwoord, uitgevoerd door/op, eventueel bijlage, status |

## 10.2 Werking in het dossier

```
PLANNING – keuze materiële posten                UITVOERING (alleen materiële posten)
┌────────────────────────────────────────────┐   ┌──────────────────────────────────────────────┐
│ Hoofdpost            Huidig    Vorig  Mat. │   │ ● Vorderingen                                 │
│ ▸ Mat. vaste activa   85.000   92.000 [x]  │   │   ▾ Cijfers (doorklikken)                     │
│ ▾ Vorderingen        160.000  140.000 [x]  │──►│       Handelsdebiteuren      120.000   95.000 │
│     Handelsdebiteuren 120.000  95.000      │   │         1300 Debiteuren      135.000  105.000 │
│     Groepsmaatsch.    10.000   25.000      │   │         1310 Voorz. dubieus  -15.000  -10.000 │
│     Overlopende act.  30.000   20.000      │   │       Groepsmaatschappijen    10.000   25.000 │
│ ▸ Liquide middelen    64.000   81.000 [ ]  │   │       Overlopende activa      30.000   20.000 │
│ ▸ Kortl. schulden    110.000   98.000 [x]  │   │   Werkprogramma (5 stappen)  + Stap toevoegen │
│ …                                          │   │ ● Kortlopende schulden                        │
│ Toelichting keuze (optioneel): …           │   │   …                                           │
└────────────────────────────────────────────┘   └──────────────────────────────────────────────┘
                                                  RAPPORT (alle posten)
                                                  └ Presentatie en opmaak per post aanpassen
```
*(Fictieve bedragen.)*

1. **Keuze materiële posten** (planning).
   - Een tabel toont alle hoofdposten met huidig en vorig saldo. Met ▸/▾ klap je een hoofdpost open naar de subposts, en een subpost naar de grootboekrekeningen.
   - De gebruiker zet per hoofdpost het vinkje **Materieel**. Er is geen berekening en geen voorstel.
   - Optioneel een vrij tekstveld *Toelichting op de keuze*, voor de dossierdocumentatie.
2. **Vinkje aan** → het postprogramma uit het sjabloon wordt als **kopie** onder die hoofdpost in *Uitvoering* gezet.
   - Latere sjabloonwijzigingen veranderen een lopend dossier niet.
   - De post krijgt daar ook de cijfers met **doorklikken** (hoofdpost → subposts → grootboekrekeningen, huidig, vorig en verschil) en een notitieveld per post.
3. **Vinkje uit** →
   - zijn er nog geen stappen ingevuld, dan verdwijnt het programma;
   - zijn er wel stappen ingevuld, dan vraagt het systeem of die bewaard moeten blijven (als "niet van toepassing") of verwijderd. Ingevulde documentatie verdwijnt nooit ongemerkt.
4. **Niet-materiële posten** verschijnen **niet** in *Uitvoering*: geen vragen, geen stappen, geen cijferbeoordeling. Ze zijn alleen zichtbaar in **Rapport**, waar de presentatie kan worden aangepast ([§10.4](#104-rapport-presentatie-en-opmaak-per-post)).
5. **De systeemcontroles gelden altijd voor alle posten**, materieel of niet: alles gekoppeld, balans sluit, specificatie sluit aan op saldo, verloop sluit aan. Dat zijn automatische aansluitingen, geen werk voor de gebruiker.

## 10.3 Stappen toevoegen en verwijderen (materiële posten)

| Actie | Gedrag | Vastlegging |
|---|---|---|
| **Stap toevoegen** | Vrije stap onder de hoofdpost, eventueel gekoppeld aan een subpost: titel, instructie | Gemarkeerd als *eigen stap*; wie en wanneer |
| **Standaardstap verwijderen** | De stap verdwijnt uit de weergave, maar blijft bewaard en is zichtbaar via "toon verwijderde stappen" | Wie, wanneer en (instelbaar verplicht) een reden. Zichtbaar in het review-overzicht |
| **Eigen stap verwijderen** (nog niet ingevuld) | Echt verwijderen | Alleen in de audit log |
| **Volgorde wijzigen** | Slepen | – |
| **Stap voorstellen voor sjabloon** | Een eigen stap aanbieden aan de vaktechnisch beheerder | Opname in een nieuwe sjabloonversie |

**Rechten** (zie vraag C5 in de [vragenlijst](11-vragenlijst.md)):
- iedereen mag stappen toevoegen;
- standaardstappen verwijderen volgens het rechtenbesluit;
- stappen die in het sjabloon **verplicht** zijn, kunnen niet verwijderd worden, alleen op "n.v.t." met een reden.

## 10.4 Rapport: presentatie en opmaak per post

Voor **elke** post, materieel of niet, is er in *Rapport* een presentatie-editor. Dat is de tab *Rapport* uit de schermafbeelding.

| Wat | Mogelijkheden |
|---|---|
| **Specificatieregels** | Standaard de grootboekrekeningen van de post, zoals in de voorbeeldrapporten ([§9.1](09-analyse-voorbeeldrapporten.md#91-opbouw-voorbeeld-a-bv-klein)). Per regel: omschrijving aanpassen, regels **samenvoegen**, **verbergen** of **volgorde** wijzigen, subkoppen en subtotalen toevoegen, of handmatige specificatieregels (die moeten aansluiten op het saldo) |
| **Toon in rapport** | Specificatie aan of uit in het rapport. Hij blijft wel in het dossier |
| **Verloopoverzicht** | Aan of uit (bijv. MVA, rekening-courant, voorzieningen, eigen vermogen); kolommen en regels kiezen |
| **Toelichtende tekst** | Tekstblok boven of onder de specificatie (standaardtekst met variabelen, of vrije tekst). Doorrollen naar volgend jaar als voorstel |
| **Opmaak** | Kop en omschrijving van de post, nieuwe pagina ervoor ja/nee, tabellen bij elkaar houden. Huisstijl (lettertype, kleuren) komt centraal uit het rapportsjabloon, zodat rapporten uniform blijven |

Aanpassingen worden **per klant onthouden** en rollen door naar het volgende jaar. Zo hoef je
samenvoegingen en omschrijvingen maar één keer in te stellen.

## 10.5 Het sjabloon (beheer)

```
Werkprogrammasjabloon "Samenstellen jaarrekening" (BV) v2027.1
├─ Profiel ─ Cliënt · Opdracht · Rapport
├─ Voorbereiding
│  ├─ Acceptatie: Scope opdracht* · Akkoord acceptatie*      (vragen + standaardantwoorden)
│  └─ Planning: Keuze materiële posten* · Bijzonderheden
├─ Uitvoering  (alleen materiële hoofdposten worden getoond)
│  ├─ Hoofdpost "Materiële vaste activa"   rubrieken: MVA*
│  │    stappen: 1 Verloopoverzicht aansluiten op grootboek …  2 Investeringen aansluiten op facturen …
│  │             3 Afschrijvingen narekenen …  4 Desinvesteringen en boekresultaat …
│  ├─ Hoofdpost "Vorderingen"   rubrieken: Vorderingen*
│  │    stappen: 1 Debiteurenlijst aansluiten … (subpost Handelsdebiteuren)  2 Voorziening dubieuze debiteuren …
│  └─ …
├─ Rapport ─ Financiële rapportage (presentatie per post, alle posten)
└─ Afronding ─ Evaluatie uitvoering · Bevestiging klant* · Uitbrengen rapport* · Afsluiting*
```

Per stap legt het sjabloon vast:
- titel en instructie;
- type antwoord (tekst, ja/nee/n.v.t., bedrag, bijlage verplicht);
- standaardantwoord;
- *verplicht* ja/nee;
- optioneel de **subpost** waar de stap over gaat;
- eventueel een voorwaarde (rechtsvorm, grootte);
- een verwijzing naar regelgeving.

**Hoofdposten** worden met een **rubriekselectie** aan het rubriekenschema gekoppeld. De subposts volgen automatisch uit de rubriekenboom onder die selectie.

## 10.6 Uitbreiding datamodel

Aanvulling op [hoofdstuk 6](06-datamodel.md):

- **SjabloonPost**: Id, SjabloonId, Titel, Rubrieken (selectie, bepaalt de subposts), Volgorde.
- **SjabloonStap**: Id, SjabloonPostId/OnderdeelId, Titel, Instructie, AntwoordType, Standaardantwoord, Verplicht, SubpostRubriekId (optioneel), Conditie, Volgorde.
- **DossierPost**: DossierId, SjabloonPostId, **IsMaterieel**, GewijzigdDoor/Op.
- **DossierPlanning**: DossierId, ToelichtingKeuzeMaterielePosten (optioneel).
- **DossierStap**: Id, DossierPostId/OnderdeelId, BronSjabloonStapId (*null* = eigen stap), SubpostRubriekId, Titel, Instructie, Verplicht, Volgorde, Status (`Open`/`Gereed`/`NVT`), Antwoord, UitgevoerdDoor/Op, **Verwijderd**, VerwijderdDoor/Op, **VerwijderReden**.
- **PresentatieInstelling** (per klant, doorrollend): KlantId, RubriekId, Specificatie-instellingen (omschrijvingen, samenvoegingen, verborgen regels, volgorde, subkoppen), ToonInRapport, VerloopoverzichtAan, Tekstblokken, NieuwePagina.

## 10.7 Controles

- **Planning afronden** kan pas als voor elke hoofdpost met saldo bewust is gekozen: het vinkje staat aan, of de keuze "niet materieel" is bevestigd met een tweede klik op "keuze bevestigd". Zo wordt geen post per ongeluk overgeslagen.
- **Goedkeuren** van een materiële post kan pas als alle niet-verwijderde stappen *Gereed* of *NVT* zijn en er geen open reviewpunten zijn.
- **Review-overzicht** per dossier toont:
  - de gekozen materiële posten;
  - verwijderde standaardstappen met hun reden;
  - eigen toegevoegde stappen.
