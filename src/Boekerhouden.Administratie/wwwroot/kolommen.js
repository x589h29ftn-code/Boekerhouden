// Kolommen van een tabel breder of smaller maken door de rand van een kolomkop te slepen; de breedtes blijven bewaard
// in deze browser (per tabel). Dubbelklik op een rand zet de breedtes terug.
const voorvoegsel = "kolommen:";

function lees(sleutel) {
    try {
        const breedtes = JSON.parse(localStorage.getItem(voorvoegsel + sleutel));
        return Array.isArray(breedtes) ? breedtes : null;
    } catch {
        return null;
    }
}

function bewaar(sleutel, koppen) {
    try {
        localStorage.setItem(voorvoegsel + sleutel, JSON.stringify(koppen.map(k => Math.round(k.getBoundingClientRect().width))));
    } catch {
        // Zonder opslag (privévenster) werkt het slepen nog, alleen niet bewaard.
    }
}

function zet(tabel, koppen, breedtes) {
    koppen.forEach((k, i) => k.style.width = breedtes[i] + "px");
    tabel.style.width = breedtes.reduce((a, b) => a + b, 0) + "px";
}

// Zonder eigen breedtes: de tabel vult de beschikbare ruimte en alleen de kolom met class "rekt" groeit of krimpt mee
// (bijv. als er rechts een paneel bij komt); de andere kolommen houden hun breedte.
function vul(tabel, koppen, breedtes, rekt) {
    // Blijft er voor de meegevende kolom te weinig over, dan houdt alles zijn breedte en schuift de tabel (schuifbalk).
    const ruimte = tabel.parentElement ? tabel.parentElement.clientWidth : 0;
    if (ruimte > 0 && ruimte - breedtes.reduce((a, b, i) => a + (i === rekt ? 0 : b), 0) < 80) {
        zet(tabel, koppen, breedtes.map((b, i) => i === rekt ? Math.max(80, Math.min(b, 240)) : b));
        return;
    }

    koppen.forEach((k, i) => k.style.width = i === rekt ? "" : breedtes[i] + "px");
    tabel.style.width = "100%";
}

// Wat een kolom minstens nodig heeft: de breedste inhoud die niet wordt afgekapt (bedragen, datums, kolomkoppen,
// knoppen), met de opvulling van de cel. Tekst die afkapt (bijv. .text-truncate) telt niet mee, tekst die mag
// teruglopen alleen met zijn langste woord; een bedrag telt altijd helemaal mee.
// Gemeten zoals de tabel nu getekend is: past iets niet, dan steekt het uit en zie je hoe breed het is.
const atomair = new Set(["BUTTON", "INPUT", "SELECT", "TEXTAREA", "IMG", "svg"]);

function knipt(element, cel) {
    // Een bedrag kapt nooit af, ook niet in een cel die afkapt (bijv. de grootboekkaart): dan zie je een half bedrag.
    if (cel.matches(".bedrag, .text-end") || element.closest(".bedrag")) {
        return false;
    }

    for (let e = element; e && e !== cel.parentElement; e = e.parentElement) {
        if (getComputedStyle(e).overflowX !== "visible") {
            return true;
        }
    }

    return false;
}

function inhoud(cel) {
    let links = Infinity, rechts = -Infinity;
    const neem = r => { if (r.width > 0) { links = Math.min(links, r.left); rechts = Math.max(rechts, r.right); } };
    const loper = document.createTreeWalker(cel, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT);
    for (let n = loper.nextNode(); n; n = loper.nextNode()) {
        const element = n.nodeType === Node.TEXT_NODE ? n.parentElement : n;
        if (element.closest(".kolomgreep") || knipt(element, cel)) {
            continue;
        }

        if (n.nodeType === Node.TEXT_NODE) {
            if (n.textContent.trim()) {
                const bereik = document.createRange();
                bereik.selectNodeContents(n);
                [...bereik.getClientRects()].forEach(neem);
            }
        } else if (atomair.has(n.tagName)) {
            neem(n.getBoundingClientRect());
        }
    }

    if (rechts < links) {
        return 0;
    }

    const stijl = getComputedStyle(cel);
    return Math.ceil(rechts - links + parseFloat(stijl.paddingLeft) + parseFloat(stijl.paddingRight) + 1);
}

// Per kolom (index) wat hij nodig heeft; alleen de gevraagde kolommen. Cellen over meer kolommen tellen niet mee.
function nodig(tabel, kolommen) {
    const uitkomst = new Map(kolommen.map(i => [i, 0]));
    for (const rij of tabel.rows) {
        let kolom = 0;
        for (const cel of rij.cells) {
            if (cel.colSpan === 1 && uitkomst.has(kolom) && cel.offsetParent !== null) {
                uitkomst.set(kolom, Math.max(uitkomst.get(kolom), inhoud(cel)));
            }

            kolom += cel.colSpan;
        }
    }

    return uitkomst;
}

export function maak(tabel, sleutel) {
    if (!tabel || !tabel.tHead || tabel.dataset.kolommen) {
        return;
    }

    tabel.dataset.kolommen = sleutel;
    const koppen = [...tabel.tHead.rows[0].cells];
    // Een kolom met data-breedte (bijv. een vakje om te kiezen) houdt die breedte; de andere zoals ze nu getekend zijn.
    // Naar beneden afronden: anders is de som een paar pixels breder dan de ruimte en valt de laatste kolom net af.
    const gemeten = koppen.map(k => Math.floor(k.getBoundingClientRect().width));
    const oorspronkelijk = koppen.map((k, i) => k.dataset.breedte ? Number(k.dataset.breedte) : gemeten[i]);
    const over = gemeten.reduce((a, b) => a + b, 0) - oorspronkelijk.reduce((a, b) => a + b, 0);
    if (over > 0) {
        // De ruimte die een vaste kolom overhoudt, gaat naar de kolom met class "rekt" (de omschrijving), anders de breedste.
        const rekt = koppen.findIndex(k => k.classList.contains("rekt"));
        const breedste = rekt >= 0 ? rekt : oorspronkelijk.indexOf(Math.max(...oorspronkelijk.filter((_, i) => !koppen[i].dataset.breedte)));
        oorspronkelijk[breedste] += over;
    }
    // Wat door het afronden overblijft (een paar pixels), krijgt de breedste kolom: dan eindigt de tabel precies rechts.
    const ruimte = Math.floor(tabel.parentElement ? tabel.parentElement.clientWidth : 0);
    const rest = ruimte - oorspronkelijk.reduce((a, b) => a + b, 0);
    // Is de tabel breder dan de ruimte (bijv. door vaste kolombreedtes), dan gaat het verschil van de breedste kolom af,
    // zolang die nog leesbaar blijft: anders valt de laatste kolom rechts buiten beeld.
    const breedste = oorspronkelijk.indexOf(Math.max(...oorspronkelijk.filter((_, i) => !koppen[i].dataset.breedte)));
    if (ruimte > 0 && breedste >= 0 && ((rest > 0 && rest <= koppen.length + 2) || (rest < 0 && oorspronkelijk[breedste] + rest >= 80))) {
        oorspronkelijk[breedste] += rest;
    }
    // Geen kolom smaller dan zijn inhoud (bijv. een kop die anders over de kolom ernaast loopt): wat erbij moet,
    // gaat af van de kolommen die ruimer zijn dan ze nodig hebben, de breedste eerst. Lukt dat niet, dan wordt de
    // tabel breder dan zijn kader en schuift het kader.
    const minimum = nodig(tabel, koppen.map((_, i) => i));
    koppen.forEach((_, i) => { oorspronkelijk[i] = Math.max(oorspronkelijk[i], minimum.get(i)); });
    let teVeel = ruimte > 0 ? oorspronkelijk.reduce((a, b) => a + b, 0) - ruimte : 0;
    while (teVeel > 0) {
        const kandidaten = oorspronkelijk.map((b, i) => i).filter(i => !koppen[i].dataset.breedte && oorspronkelijk[i] > Math.max(80, minimum.get(i)));
        if (kandidaten.length === 0) {
            break;
        }

        const i = kandidaten.reduce((m, j) => oorspronkelijk[j] > oorspronkelijk[m] ? j : m, kandidaten[0]);
        const af = Math.min(teVeel, oorspronkelijk[i] - Math.max(80, minimum.get(i)));
        oorspronkelijk[i] -= af;
        teVeel -= af;
    }

    const rekbaar = koppen.findIndex(k => k.classList.contains("rekt"));
    // De kolom die meegeeft als de ruimte verandert: de kolom met class "rekt", anders de breedste.
    const meegevend = rekbaar >= 0 ? rekbaar : breedste;
    const standaard = () => meegevend >= 0 ? vul(tabel, koppen, oorspronkelijk, meegevend) : zet(tabel, koppen, oorspronkelijk);
    // Eigen breedtes gelden zolang ze passen; is het venster smaller geworden (of kwam er een schuifbalk bij), dan
    // vult de tabel de ruimte en geeft de meegevende kolom toe, zodat de laatste kolom niet buiten beeld valt.
    const pas = () => {
        const bewaard = lees(sleutel);
        // Een kader dat meegroeit met de tabel (data-groeit: de ruimte die ernaast vrij moet blijven, bijv. voor een
        // paneel): de tabel mag zo breed worden als de rij min die ruimte.
        const groeit = tabel.closest("[data-groeit]");
        const beschikbaar = groeit && groeit.parentElement
            ? groeit.parentElement.clientWidth - Number(groeit.dataset.groeit)
            : (tabel.parentElement ? tabel.parentElement.clientWidth : 0);
        if (!bewaard || bewaard.length !== koppen.length) {
            standaard();
        } else if (beschikbaar > 0 && meegevend >= 0 && bewaard.reduce((a, b) => a + b, 0) > beschikbaar + 1) {
            vul(tabel, koppen, bewaard, meegevend);
        } else {
            zet(tabel, koppen, bewaard);
        }

        // Eigen breedtes die te smal zijn geworden (bijv. grotere bedragen dan toen): de kolom wordt zo breed als nodig.
        if (bewaard && bewaard.length === koppen.length) {
            const breedtes = koppen.map(k => k.getBoundingClientRect().width);
            const minimum = nodig(tabel, koppen.map((_, i) => i));
            if (breedtes.some((b, i) => minimum.get(i) > b + 1)) {
                zet(tabel, koppen, breedtes.map((b, i) => Math.max(b, minimum.get(i))));
            }
        }
    };
    tabel.style.tableLayout = "fixed";
    tabel.kolommenPas = pas;
    pas();

    koppen.forEach((kop, i) => {
        const greep = document.createElement("span");
        greep.className = "kolomgreep";
        greep.title = "Slepen: breder of smaller · dubbelklik: terug";
        kop.appendChild(greep);
        greep.addEventListener("click", e => e.stopPropagation());
        greep.addEventListener("dblclick", e => {
            e.stopPropagation();
            try { localStorage.removeItem(voorvoegsel + sleutel); } catch { /* geen opslag */ }
            standaard();
            past(tabel);
        });
        greep.addEventListener("pointerdown", e => {
            e.preventDefault();
            e.stopPropagation();
            const start = e.clientX;
            const breedtes = koppen.map(k => k.getBoundingClientRect().width);
            greep.setPointerCapture(e.pointerId);
            document.body.classList.add("kolom-slepen");
            // Niet smaller dan de inhoud: anders lopen bedragen over de kolom ernaast heen. Eén keer gemeten, met de
            // kolom zo smal mogelijk, zodat ook wat nu nog past maar straks niet meer, meetelt.
            const smal = [...breedtes];
            smal[i] = 36;
            zet(tabel, koppen, smal);
            const minimum = Math.max(36, nodig(tabel, [i]).get(i));
            zet(tabel, koppen, breedtes);
            // Wordt de kolom breder dan er ruimte is, dan geeft eerst de meegevende kolom (de omschrijving) toe, tot wat
            // die zelf nodig heeft; pas daarna wordt de tabel breder dan zijn kader (en schuift het kader).
            const ruimte = tabel.parentElement ? tabel.parentElement.clientWidth : 0;
            const geeft = meegevend >= 0 && meegevend !== i ? Math.max(80, nodig(tabel, [meegevend]).get(meegevend)) : null;
            const beweeg = ev => {
                const nieuw = [...breedtes];
                nieuw[i] = Math.max(minimum, breedtes[i] + ev.clientX - start);
                const teVeel = nieuw.reduce((a, b) => a + b, 0) - ruimte;
                if (geeft !== null && ruimte > 0 && teVeel > 0 && nieuw[i] > breedtes[i]) {
                    nieuw[meegevend] -= Math.max(0, Math.min(teVeel, nieuw[meegevend] - geeft));
                }

                zet(tabel, koppen, nieuw);
                past(tabel);
            };
            const los = () => {
                greep.removeEventListener("pointermove", beweeg);
                greep.removeEventListener("pointerup", los);
                document.body.classList.remove("kolom-slepen");
                bewaar(sleutel, koppen);
                past(tabel);
            };
            greep.addEventListener("pointermove", beweeg);
            greep.addEventListener("pointerup", los);
        });
    });
}

// Alleen een tabel die echt breder is dan zijn kader krijgt een horizontale schuifbalk (.te-breed); de andere
// kaders scrollen niet zelf, zodat de kolomkoppen bovenaan het venster blijven staan bij het naar beneden scrollen.
// Het kader is de kaart om de tabel (.overzichtskaart of .card), anders het element er direct omheen.
const breedte = typeof ResizeObserver !== "undefined"
    ? new ResizeObserver(items => items.forEach(i => past(i.target.matches("table") ? i.target : i.target.querySelector(":scope > table"))))
    : null;

function kaderVan(tabel) {
    return tabel.parentElement;
}

function past(tabel) {
    const kaart = tabel && kaderVan(tabel);
    if (kaart) {
        if (tabel.kolommenPas && !document.body.classList.contains("kolom-slepen") && tabel.kolommenRuimte !== kaart.clientWidth) {
            tabel.kolommenRuimte = kaart.clientWidth;
            tabel.kolommenPas();
        }
        // De breedte van de tabel zelf: de sleepgreep van de laatste kolom steekt een paar pixels uit en telt niet mee.
        kaart.classList.toggle("te-breed", tabel.offsetWidth > kaart.clientWidth + 1);
    }
}

// Een kop die blijft staan (.kop-vast): de hoogte van de kop en van de balk eronder als variabelen, zodat de balk onder
// de kop en de kolomkoppen daar weer onder blijven staan.
function vasteKoppen() {
    document.querySelectorAll(".administratie-inhoud.kop-vast").forEach(inhoud => {
        const kop = inhoud.firstElementChild;
        const onder = inhoud.querySelector(":scope > .onderkop");
        const hoogte = kop ? Math.floor(kop.getBoundingClientRect().height) : 0;
        const totaal = hoogte + (onder ? Math.floor(onder.getBoundingClientRect().height) : 0);
        if (inhoud.dataset.kopVast !== `${hoogte}/${totaal}`) {
            inhoud.dataset.kopVast = `${hoogte}/${totaal}`;
            inhoud.style.setProperty("--kop-hoogte", hoogte + "px");
            inhoud.style.setProperty("--kop-vast", totaal + "px");
        }
    });
}

// Elke tabel met data-sleepbaar="sleutel" krijgt vanzelf versleepbare kolommen, ook als Blazor hem later tekent.
function koppelAlle() {
    document.querySelectorAll("table[data-sleepbaar]:not([data-kolommen])").forEach(t => {
        if (t.tHead && t.tHead.rows.length > 0 && t.offsetParent !== null) {
            maak(t, t.dataset.sleepbaar);
            const kaart = kaderVan(t);
            if (kaart && breedte) {
                breedte.observe(kaart);
                breedte.observe(t);
            }

            past(t);
        }
    });
}

if (typeof document !== "undefined" && !window.kolommenGekoppeld) {
    window.kolommenGekoppeld = true;
    let gepland = false;
    new MutationObserver(() => {
        if (!gepland) {
            gepland = true;
            requestAnimationFrame(() => { gepland = false; koppelAlle(); vasteKoppen(); });
        }
    }).observe(document.documentElement, { childList: true, subtree: true });
    window.addEventListener("resize", vasteKoppen);
    koppelAlle();
    vasteKoppen();
}
