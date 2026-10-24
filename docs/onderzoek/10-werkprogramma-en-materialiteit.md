# 10. Werkprogramma en materiële posten

> **Besluiten (23-10-2026):**
> 1. Het kantoor maakt een **eigen werkprogramma** in een sjabloon.
> 2. **De gebruiker kiest welke posten materieel zijn.** Het systeem rekent niets uit en doet geen voorstel.
> 3. Materieel aanvinken gebeurt **per hoofdpost**. Binnen de hoofdpost kun je **doorklikken naar de subposts** (en verder naar de grootboekrekeningen).
> 4. Een **materiële post** krijgt zijn werkprogramma in het uitvoeringsdeel. Stappen zijn per dossier **toe te voegen en te verwijderen**.
> 5. Een **niet-materiële post** heeft **geen vragen en geen werk**. Hij is alleen zichtbaar in het **rapport**, waar je de presentatie en opmaak kunt aanpassen.
>
> **Aanvulling (24-10-2026): risicogericht samenstellen.** Per hoofdpost kies je met drie bolletjes **n.v.t.**, **materieel** of **risicopost**. Standaard staat alles op n.v.t. Een materiële post is van belang voor het beeld van de jaarrekening; een risicopost is een post waar het mis kan gaan (waardering, volledigheid, schattingen), met een korte omschrijving van het risico. Beide krijgen het postprogramma ([§10.9](#109-risicogericht-nvt-materieel-of-risicopost-plakje-7a)).

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
4. **Alle posten staan in *Uitvoering*** (besluit oktober 2026), gegroepeerd naar balans en winst-en-verliesrekening in de volgorde van de jaarrekening. Zo loop je het hele rapport post voor post door (‹ vorige · volgende ›) en zet je meteen grondslagen, toelichting en verloop goed.
   - Elke post toont de cijfers (doorklikken tot de rekeningen) en **In het rapport**: het stuk van het rapport bij die post (specificatie, toelichting, verloopoverzichten en de grondslagen uit de algemene toelichting), met dezelfde bewerkingen als in de rapportviewer: uitsplitsen, toelichten, verloop toevoegen of aanpassen, grondslagen aanpassen en de specificatie aan of uit. De rapporttab is daarna voor de laatste controle van het geheel.
   - **Volgorde per post**: eerst de totalen (cijfers), dan bij een materiële post het werkprogramma (cijferbeoordeling, stappen zoals *voorziening dubieuze debiteuren*, reviewpunten), dan hoe de post in het rapport komt (specificatie, toelichting, verloop; zelf aan te passen) en tot slot de grondslag van de post uit de algemene toelichting.
   - Alleen een **materiële** post krijgt daarnaast de stappen uit het sjabloon, de cijferbeoordeling, bijlagen en reviewpunten. Materieel maken kan ook vanaf de post zelf; uitzetten gaat via Planning (met de vraag of ingevulde stappen bewaard blijven).
   - **Werkprogramma en rapport volgen elkaar**: sta je bij een post in het werkprogramma, dan openen de tabbladen Rapport en Proefbalans op die post; vanuit het rapport opent Werkprogramma (en Proefbalans) op de post die in beeld is. Het dossiermenu blijft op de rapportpagina bovenaan staan.
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

*Gebouwd (plakje 6):* in het dossier onder *Rapport* (`/dossiers/{id}/rapport`) werk je **in het rapport
zelf**, als laatste controle vóór de PDF. Links staat de inhoud (klik = naar die plek), rechts het rapport
zoals het wordt afgedrukt, met een knop **PDF**. Wat je in de viewer ziet, staat precies zo in de PDF; de
bedieningselementen (driehoekjes, knoppen) worden niet afgedrukt.

| In de viewer | Hoe | Geldt voor |
|---|---|---|
| **In- en uitklappen** | Driehoekje voor een post. In de balans en W&V klapt een post uit naar zijn subposten (bijv. *Overige bedrijfskosten* → huisvestingskosten, autokosten, verkoopkosten); in een specificatie klapt een groep in tot één regel. Standaard: balans en specificaties uit-, W&V ingeklapt | De klant, vanaf dit jaar |
| **Namen aanpassen** | Klik op de naam van een post, eigen post of rekening; Enter bewaart, Escape annuleert, leeg = de standaardnaam. Een postnaam geldt in het hele rapport (en bij het rubriceren) | De klant, vanaf dit jaar |
| **Toelichting** | Onder elke specificatie *toelichting toevoegen* (zichtbaar bij aanwijzen), of op de tekst klikken om die aan te passen. Een toelichting rolt door naar volgende jaren; leegmaken haalt hem vanaf dit jaar weg | Dit dossier en volgende jaren |
| **Rekeningen en herrubriceren** | Klik op een bedrag (of *rekeningen* bij aanwijzen) van een post, subpost, eigen post of rekening: rechts staan de grootboekrekeningen eronder, met dit jaar en vorig jaar (een rekening die door de omslag een jaar elders staat, alleen in het jaar dat hij hier staat). Sleep een rekening naar een andere regel in het rapport, of klik erop en kies de regel, om hem daar te verantwoorden: op een subpost of eigen post wordt hij daaraan gekoppeld, op een rekening krijgt hij dezelfde koppeling als die rekening. De inhoud links maakt zolang plaats voor het paneel | De klant (de rubricering; ook vergelijkende cijfers en proefbalans), in het wijzigingslog |
| **Uitsplitsen** | Bij een rekening *uitsplitsen*, bijv. *Nog te betalen kosten* in accountantskosten, energie en overige. Per regel een omschrijving, het bedrag en het bedrag vorig jaar, met een **controletelling** op het saldo van beide jaren. Sluit het niet aan, dan staat het verschil in het rapport als *Nog niet gespecificeerd* (de totalen blijven kloppen) en volgt een waarschuwing. Een uitsplitsing van vorig jaar is het voorstel (met die bedragen als vorig jaar) | Dit dossier |

Achter de schakelaar *presentatie aanpassen* staat per hoofdpost met saldo of de **specificatie** in het
rapport komt, en per rekening of hij **in Overige** meetelt (de regel *Overige* van zijn groep, zodat de
specificatie blijft aansluiten). Rekeningen onder een eigen post staan als één regel met de naam van de
eigen post. Alles staat in het wijzigingslog. Nog niet gebouwd: regels samenvoegen of in een andere
volgorde en opmaak per post (nieuwe pagina ervoor).

**Verloopoverzichten.** Standaard staat er een verloopoverzicht onder de specificatie van de immateriële,
materiële en financiële vaste activa en het eigen vermogen, met een kolom per subpost en een totaal, zoals in de
voorbeeldrapporten:

| Soort | Opzet | Voorstel voor de mutaties |
|---|---|---|
| Vaste activa met afschrijvingen (IVA, MVA) | *Boekwaarde 1 januari*: verkrijgingsprijs en cumulatieve afschrijvingen (rekeningen met een RGS-code aan de creditkant, bijv. `BMvaBeiCae`) · *Mutaties* · *Boekwaarde 31 december* · *Afschrijvingspercentage* | Afschrijvingen uit de W&V per subpost (`BMvaBei` ↔ `WAfsAmvBei`; anders de toename van de cumulatieve afschrijvingen), een hogere verkrijgingsprijs als investering, de rest als desinvestering. Percentage: afschrijving / verkrijgingsprijs, afgerond op 5% (zelf aan te passen, rolt door) |
| Financiële vaste activa, andere posten | *Stand 1 januari* · mutaties · *Stand 31 december* | Toename en afname |
| Eigen vermogen | Kolommen per subpost en *Resultaat boekjaar* | *Resultaat {jaar}*, *Bestemming resultaat {vorig jaar}* (naar de overige reserves) en de rest als *Overige mutaties* (bijv. dividend, privé) |
| Rekening (bijv. rekening-courant DGA) | Standaard **twee jaar naast elkaar** (uit te zetten met *zonder vorig jaar*) | Dit jaar: toename/afname. Vorig jaar: de mutaties uit het dossier van vorig jaar; anders uit de saldibalans van twee jaar terug; ontbreekt die, dan volgt de beginstand van vorig jaar uit de ingevulde mutaties |

In de viewer pas je de mutaties aan met *mutaties aanpassen* (per regel een omschrijving en per kolom een bedrag,
met de controle *sluit aan* of het verschil; bij de vaste activa ook het afschrijvingspercentage). Sluit het niet
aan, dan staat het verschil als *Nog niet verklaard* in het overzicht en volgt een waarschuwing. Bij een andere
balanspost zet je er een bij met *verloop toevoegen* onder de specificatie, en bij een rekening met *verloop* op de
regel. *Weghalen*, toevoegen, met of zonder vorig jaar en het afschrijvingspercentage gelden vanaf dit jaar; de
mutaties zijn per dossier. Vlottende activa hebben standaard geen verloop.

**Per boekjaar, nooit met terugwerkende kracht.** Elke aanpassing in het rapport geldt vanaf het boekjaar van
het dossier waarin je hem maakt: namen van posten en rekeningen (ook kantoorbreed), rekeningen in *Overige*,
specificatie aan of uit, in- en uitklappen, toelichtingen en grondslagen (open of dicht, eigen tekst). Eerdere
jaren blijven zoals ze waren. Volgende jaren nemen de aanpassing over, tenzij ze zelf iets anders hebben; zo gaat
het rapport van vorig jaar bij de jaarwisseling vanzelf mee. Technisch: per instelling versies met
`VanafBoekjaar` (in een jaar geldt de versie met het hoogste jaar tot en met dat jaar); een versie die gelijk is
aan wat ervoor gold, vervalt vanzelf. De grondslagenbibliotheek van het kantoor werkt hetzelfde: in Beheer ›
Grondslagen kies je het boekjaar, en een aanpassing geldt vanaf dat jaar (zie [§9.3](09-analyse-voorbeeldrapporten.md#93-catalogus-van-rapportbouwstenen)).
De rubricering (koppelingen en eigen posten) geldt per klant voor alle jaren: de vergelijkende cijfers volgen
zo de indeling van dit jaar. Een uitgebracht rapport wordt bevroren bij het afronden van het dossier (plakje 8).

**Variabelen in teksten.** In elke tekst van het rapport (toelichtingen, grondslagen, ook in de bibliotheek) kun je
variabelen gebruiken; het rapport vult ze bij het maken in, zodat een tekst die doorrolt elk jaar klopt. In de
viewer en in Beheer › Grondslagen zet *variabele invoegen* er een op de plek van de cursor (in de viewer met de
huidige waarde erbij). Bedragen in hele euro's, zoals in het rapport.

| Groep | Variabelen |
|---|---|
| Algemeen | `{naam}`, `{plaats}`, `{kvk}`, `{valuta}` (€) |
| Periode | `{boekjaar}`, `{boekjaar_vorig}`, `{boekjaar_volgend}`, `{begin_boekjaar}` (1 januari 2025), `{einde_boekjaar}` (31 december 2025), `{einde_boekjaar_vorig}` |
| Bedragen | `{nettoresultaat}` (negatief bij verlies), `{nettoresultaat_vorig}`, `{nettoresultaat_absoluut}`, `{winst_of_verlies}`, `{resultaat_voor_belastingen}`(`_vorig`), `{belastingen}`, `{netto_omzet}`(`_vorig`), `{brutomarge}`(`_vorig`), `{eigen_vermogen}`(`_vorig`, met het resultaat), `{balanstotaal}`(`_vorig`), `{solvabiliteit}`(`_vorig`, bijv. 67,8%) |
| Posten | `{post:CODE}` en `{post_vorig:CODE}`: het bedrag van een RGS-post, bijv. `{post:BMva}` |

Een onbekende variabele blijft staan en geeft een waarschuwing. Bij de jaarrekening van een BV staan standaard
twee toelichtingen klaar: bij het eigen vermogen *Het ingehouden deel van het resultaat over {boekjaar} bedraagt
{valuta} {nettoresultaat} ({boekjaar_vorig}: {valuta} {nettoresultaat_vorig}).* en bij de personeelskosten het
gemiddeld aantal werknemers (met *[aantal]* om in te vullen). Aanpassen of weghalen kan in de viewer en geldt vanaf
dat jaar.

## 10.5 Het sjabloon (beheer)

```
Werkprogrammasjabloon "Samenstellen jaarrekening" (BV) v2027.1
├─ Profiel ─ Cliënt · Opdracht · Rapport
├─ Voorbereiding
│  ├─ Acceptatie: Scope opdracht* · Akkoord acceptatie*      (vragen + standaardantwoorden)
│  └─ Planning: Keuze materiële posten* · Bijzonderheden
├─ Uitvoering  (alle hoofdposten met saldo; alleen materiële krijgen stappen)
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
- **DossierPostKeuze**: DossierId, RGS-code hoofdpost, **Beoordeling** (n.v.t., materieel, risicopost), **Risico** (tekst bij een risicopost), GewijzigdDoor/Op.
- **DossierPlanning**: DossierId, ToelichtingKeuzeMaterielePosten (optioneel).
- **DossierStap**: Id, DossierPostId/OnderdeelId, BronSjabloonStapId (*null* = eigen stap), SubpostRubriekId, Titel, Instructie, Verplicht, Volgorde, Status (`Open`/`Gereed`/`NVT`), Antwoord, UitgevoerdDoor/Op, **Verwijderd**, VerwijderdDoor/Op, **VerwijderReden**.
- **PresentatieInstelling** (per klant, doorrollend): KlantId, RubriekId, Specificatie-instellingen (omschrijvingen, samenvoegingen, verborgen regels, volgorde, subkoppen), ToonInRapport, VerloopoverzichtAan, Tekstblokken, NieuwePagina.

## 10.7 Controles

- **Planning afronden**: sinds plakje 7a staat elke post standaard op *n.v.t.*; een aparte bevestiging van de niet-materiële posten is vervallen. *Beoordeling posten* is een verplichte stap die je aftekent als de keuze klaar is.
- **Goedkeuren** van een materiële post kan pas als alle niet-verwijderde stappen *Gereed* of *NVT* zijn en er geen open reviewpunten zijn.
- **Review-overzicht** per dossier toont:
  - de gekozen materiële posten;
  - verwijderde standaardstappen met hun reden;
  - eigen toegevoegde stappen.

## 10.8 Gebouwd in plakje 5

**Werkprogramma in het dossier** (`/dossiers/{id}/werkprogramma`):
- Links de onderdelen per fase (voorbereiding, uitvoering, afronding) met de voortgang (*2/5*, *gereed*, *gereviewd*) en open reviewpunten; rechts het gekozen onderdeel.
- Bij de eerste keer openen krijgt het dossier een **kopie** van het kantoorsjabloon voor de soort opdracht; de versie staat bovenaan. Latere wijzigingen in het sjabloon veranderen het dossier niet.
- **Planning**: per RGS-hoofdpost (niveau 2) met saldo een vinkje *materieel*, met doorklikken naar de subposten. *Overige posten: niet materieel* bevestigt de rest in één keer. Een toelichting op de keuze is optioneel.
  - Vinkje aan: de post komt in de uitvoering, met de stappen uit het sjabloon (of zonder stappen als het sjabloon die post niet kent), de cijfers (subposten en rekeningen, huidig, vorig, verschil) en een notitie *cijferbeoordeling*.
  - Vinkje uit: zonder invulling verdwijnt de post; met invulling (antwoord, status, notitie, bijlage of reviewpunt) kies je tussen *bewaren als niet van toepassing* en *verwijderen*.
- **Stappen**: aftekenen met een vinkje (wie en wanneer), *niet van toepassing* met een verplichte reden, een antwoord, bijlagen. Eigen stappen toevoegen; verwijderen met reden (vraag C5: iedereen, reden verplicht), herstellen, volgorde ↑/↓. Verplichte stappen kunnen niet weg. Verwijderde stappen blijven zichtbaar via de schakelaar *verwijderde stappen*.
- **Controles bij aftekenen**: *Alle rekeningen gerubriceerd* kan pas als alles met saldo is gekoppeld; *Keuze materiële posten* pas als elke hoofdpost met saldo een keuze heeft (§10.7).
- **Review** (vraag C6): per stap *uitgevoerd* (aftekenen), per onderdeel *gereviewd* door een gebruiker met de rol reviewer of accountant, als alle stappen gereed of n.v.t. zijn en er geen open reviewpunten zijn. Daarna is het onderdeel vergrendeld tot de review wordt teruggezet. Een nieuw reviewpunt laat de review vervallen. Goedkeuring van het hele dossier door de accountant volgt in plakje 8.
- **Reviewpunten** per onderdeel, eventueel bij een stap; oplossen met een reactie, heropenen.
- **Bijlagen** bij een stap, tot 25 MB, opgeslagen in de database met SHA-256 (vraag E2). Downloaden alleen na inloggen, altijd als bestand.
- Alles staat in het wijzigingslog.

**Beheer › Werkprogramma's**: per soort opdracht de versie, onderdelen en stappen (titel, instructie, verplicht) aanpassen. In de uitvoering hoort elk onderdeel bij een RGS-hoofdpost. De planning kan niet weg.

**Meegeleverde inhoud** (versie 2026.1): een eerste werkprogramma voor *Samenstellen jaarrekening* (BV) en *Winstaangifte opstellen* (eenmanszaak), in eigen woorden opgesteld langs de fasen van Standaard 4410 ([§2.1](02-vaktechnisch-kader.md#21-standaard-4410-samenstellingsopdrachten)). Er is geen tekst uit gelicentieerde werkprogramma's overgenomen. Het is een startpunt: de vaktechnisch verantwoordelijke stelt de inhoud vast.

**Klantenoverzicht** (S1): per klant het laatste dossier met gekoppeld %, de voortgang van het werkprogramma (stappen klaar) en signalen: geen saldibalans, rekeningen met saldo niet gekoppeld, open reviewpunten, keuze materiële posten niet afgetekend. Met de schakelaar *alleen met signalen*. Dezelfde regel staat bovenaan het dossieroverzicht.

**Nog niet gebouwd**: antwoordtypes (ja/nee, bedrag), voorwaarden per stap (rechtsvorm, grootte), verwijzing naar regelgeving, *stap voorstellen voor het sjabloon* en het review-overzicht per dossier. De tab *Rapport* met de presentatie per post is gebouwd in plakje 6 ([§10.4](#104-rapport-presentatie-en-opmaak-per-post)).

## 10.9 Risicogericht: n.v.t., materieel of risicopost (plakje 7a)

**Planning › Beoordeling posten**: per RGS-hoofdpost met saldo drie bolletjes.

| Keuze | Betekenis | Gevolg |
|---|---|---|
| **n.v.t.** (standaard) | Geen bijzonder werk nodig | De post staat wel in *Uitvoering* met cijfers en rapportfragment, zonder stappen |
| **materieel** | Van belang voor het beeld van de jaarrekening | Postprogramma: stappen, cijferbeoordeling, bijlagen, reviewpunten |
| **risicopost** | Hier kan het misgaan (waardering, volledigheid, schatting) | Postprogramma, plus een regel *Risico* met wat er mis kan gaan |

- Dezelfde bolletjes staan in de kop van elke post in *Uitvoering*, zodat je ook tijdens het uitwerken kunt bijstellen. In de boom staat bij een risicopost *risico* voor de voortgang.
- Terug naar n.v.t. met ingevulde stappen: dezelfde vraag als bij het uitzetten van materieel (bewaren als niet van toepassing, of verwijderen).
- Alles staat in het wijzigingslog (*Post materieel*, *Post risicopost*, *Risico vastgelegd*).

**Kennis van de cliënt** (bedrijfsverkenning) is uitgebreid tot tien vragen: onderneming en organisatie, activiteiten en afhankelijkheden, administratie en IT, stelsel en grondslagen (deze vier verplicht), contracten en geschillen, financiering en investeringen, fiscaliteit, duurzaamheid, bijzonderheden in het boekjaar en doorlopende aandachtspunten. Bij de eenmanszaak (*Kennis van de ondernemer*, aangifte-plus) zijn het er acht. Planning heeft nu *Alle rekeningen gerubriceerd*, *Materialiteit*, *Beoordeling posten*, *Cijferanalyse* en *Afspraken*.

**Doorrollen naar het volgende jaar**: een nieuw dossier neemt van het laatste eerdere dossier van dezelfde klant en soort over:
- de antwoorden in de voorbereiding (gekoppeld op onderdeel en stap), met de status *open* en de aanduiding *overgenomen uit {jaar}*, die vervalt zodra je het antwoord bijwerkt;
- de keuzes materieel en risicopost (met het risico) voor hoofdposten die dit jaar saldo hebben.

**Meegeleverde inhoud** (versie 2026.2): een ongewijzigd standaardsjabloon gaat bij het starten van de server mee naar de nieuwe versie; een door het kantoor aangepast sjabloon blijft zoals het is. Lopende dossiers houden hun kopie.

De winstaangifte van de eenmanszaak is een aangifte-plusopdracht: zonder samenstellingsverklaring.

## 10.10 Doorlezen met signalen en paraaf (plakje 7b)

**Afronding › Doorlezen** (vervangt *Evaluatie*; standaardwerkprogramma 2026.3) toont boven de stappen twee lijsten.

**Signalen**: vaste controles op samenhang in de cijfers (na correcties) en het rapport. Ze zijn een hulp, geen oordeel, en worden bij elke keer openen opnieuw uitgerekend (`Leessignalen` in het domein).

| Signaal | Wanneer |
|---|---|
| Uit het rapport | Elke waarschuwing van het rapport, bijv. *grondslagen nog niet gekozen* of *tekst tussen [ ] nog invullen* |
| Negatief eigen vermogen | BV: eigen vermogen negatief en de alinea over de continuïteit staat niet in de toelichting. Eenmanszaak: negatief ondernemingsvermogen |
| Winst zonder vennootschapsbelasting | BV: resultaat vóór belastingen positief, geen belasting geboekt |
| Rekening-courant zonder rente | Saldo rekening-courant of lening met bestuurder of aandeelhouder, en niets op de rentecodes daarvoor |
| Afschrijvingen en vaste activa | Afschrijvingen zonder vaste activa, of materiële vaste activa zonder afschrijvingen |
| Brutomarge | Wijkt 5 procentpunt of meer af van vorig jaar |
| Groot verschil met vorig jaar | Per hoofdpost: verschil van minstens 5% van de omvang (het grootste van balanstotaal en omzet, minimaal € 1.000) én minstens 25% van vorig jaar. Niet bij een post met een cijferbeoordeling |
| Aan de andere kant | Een balanspost met een saldo aan de andere kant (behalve eigen vermogen) |
| Zonder grondslag | BV: een balanspost met saldo waarvan de grondslag uit staat |

Per signaal (één regel; details in de tooltip of door open te klikken; acties bij aanwijzen):
- **in orde** met een korte toelichting (verplicht);
- **bespreekpunt**: het signaal komt bij de bespreekpunten voor de klant;
- **heropen** maakt de afhandeling ongedaan (een nog niet besproken bespreekpunt vervalt dan ook);
- **naar de post** opent de post in de uitvoering.

**Bespreekpunten**: de punten uit de signalen en eigen punten (bijv. een vraag die bij het uitwerken opkwam). De bespreking zelf staat in §10.11.

**Stap *Signalen afgehandeld*** (verplicht) kan pas worden afgetekend als er geen open signalen meer zijn. Verder: *Lezen in samenhang* (verplicht), *Fraude en niet-naleving*, *Significante aangelegenheden* en *Continuïteit* (BV).

**Paraaf**: het onderdeel Doorlezen wordt niet *gereviewd* maar **geparafeerd**, en alleen door een gebruiker met de rol accountant (ook terugzetten). Daarna ligt de afhandeling van de signalen vast.

Alles staat in het wijzigingslog (*Signaal in orde*, *Signaal heropend*, *Bespreekpunt toegevoegd*, *Bespreekpunt verwijderd*, *Onderdeel gereviewd*).

## 10.11 Bespreking met de klant, bespreekverslag en erkenning (plakje 7c)

**Afronding › Bespreking met de klant** (BV; bij de eenmanszaak *Bespreking met de ondernemer*; standaardwerkprogramma 2026.4) vervangt *Bevestiging klant*. Boven de stappen staan drie delen:

- **Bespreking**: datum (niet in de toekomst), aanwezigen en een optionele notitie. Rechtsboven: *bespreekverslag (PDF)*.
- **Bespreekpunten**: dezelfde lijst als bij het doorlezen. Per punt de **uitkomst** (wat is besproken of afgesproken) en het vinkje **significante aangelegenheid**. Leegmaken van de uitkomst zet het punt weer open. Een besproken punt kan niet meer weg.
- **Erkenning**: wie namens de klant de verantwoordelijkheid erkent, de datum en de wijze (bevestigingsbrief, e-mail, anders). De tekst, in eigen woorden: bij de BV *het bestuur bevestigt dat het verantwoordelijk is voor de jaarrekening en dat de verstrekte gegevens en inlichtingen juist en volledig zijn*; bij de eenmanszaak bevestigt de ondernemer de juistheid en volledigheid van de gegevens voor de cijfers en de aangifte.

**Controles bij aftekenen**: *Bespreekpunten besproken* kan pas met een datum en een uitkomst bij elk punt; *Erkenning* pas als wie en wanneer zijn vastgelegd. *Uitbrengen rapport › Datum verklaring*: op of na de datum van de erkenning (§10.12).

**Review**: na *gereviewd* ligt de bespreking vast (uitkomsten en erkenning).

**Bespreekverslag (PDF)** (`/dossiers/{id}/bespreekverslag.pdf`): klant, datum en aanwezigen, de notitie, de bespreekpunten met toelichting en uitkomst (significante aangelegenheden gemarkeerd), de erkenning en wie het verslag opstelde. In de rustige stijl van de app; alle tekst is ge-escaped.

De teksten van de signalen zijn neutraal (feiten en een vraag), zodat ze als toelichting bij een bespreekpunt in het verslag kunnen staan.

Wijzigingslog: *Bespreking vastgelegd*, *Uitkomst bespreekpunt*, *Erkenning vastgelegd*, *Bespreekverslag gemaakt*.

## 10.12 Samenstellingsverklaring (plakje 7d)

Alleen bij *Samenstellen jaarrekening* (BV). De winstaangifte van de eenmanszaak is aangifte-plus: zonder verklaring.

**In het rapport** staat vooraan een deel *SAMENSTELLINGSVERKLARING* met hoofdstuk 1 *Samenstellingsverklaring van de accountant*; de hoofdstukken van de jaarrekening nummeren door (2 Balans, 3 Winst-en-verliesrekening, …). De tekst is in eigen woorden opgesteld langs wat Standaard 4410 vraagt (`Samenstellingsverklaring` in het domein):

- **aanhef**: aan het bestuur van de statutaire naam (anders de klantnaam);
- **Opdracht**: wat is samengesteld (balans, W&V, toelichting) op basis van de verstrekte gegevens, en het stelsel: Titel 9 Boek 2 BW, of fiscale grondslagen (art. 2:396 lid 6 BW), volgens de keuze bij de grondslagen;
- **Onze werkzaamheden**: Standaard 4410; geen controle- of beoordelingsopdracht, dus geen oordeel of conclusie;
- **Uw verantwoordelijkheid**: juistheid en volledigheid van de gegevens en de jaarrekening;
- **Beroepsregels**: de fundamentele beginselen;
- **Onzekerheid over de continuïteit**: alleen als de alinea *Continuïteit* in de algemene toelichting is opgenomen; de verklaring verwijst ernaar;
- **Toelichting**: een eigen toelichtende alinea (optioneel);
- **ondertekening**: plaats van het kantoor, datum, kantoornaam en accountant met titel (bijv. *J. de Vries AA*); zolang ze ontbreken staat er *[datum]* of *[accountant]*.

**Afronding › Uitbrengen rapport** (standaardwerkprogramma 2026.5): kies de accountant (een actieve gebruiker met de rol accountant), de datum (niet in de toekomst) en eventueel de eigen alinea; de datum van de erkenning staat ernaast. De stap *Datum verklaring* kan pas als accountant en datum zijn ingevuld, de erkenning is vastgelegd en de verklaring niet eerder is gedateerd dan de erkenning.

**Waarschuwingen in het concept**: *datum en accountant nog invullen*, *erkenning nog niet vastgelegd* of *gedateerd vóór de erkenning*. Bij het doorlezen tellen deze niet als signaal (de erkenning volgt pas na de bespreking).

Wijzigingslog: *Samenstellingsverklaring gewijzigd*. Ondertekenen en het definitieve rapport (zonder concept) volgen met de goedkeuring in plakje 8.
