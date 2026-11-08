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
    koppen.forEach((k, i) => k.style.width = i === rekt ? "" : breedtes[i] + "px");
    tabel.style.width = "100%";
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
    const bewaard = lees(sleutel);
    const rekbaar = koppen.findIndex(k => k.classList.contains("rekt"));
    const standaard = () => rekbaar >= 0 ? vul(tabel, koppen, oorspronkelijk, rekbaar) : zet(tabel, koppen, oorspronkelijk);
    tabel.style.tableLayout = "fixed";
    if (bewaard && bewaard.length === koppen.length) {
        zet(tabel, koppen, bewaard);
    } else {
        standaard();
    }

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
        });
        greep.addEventListener("pointerdown", e => {
            e.preventDefault();
            e.stopPropagation();
            const start = e.clientX;
            const breedtes = koppen.map(k => k.getBoundingClientRect().width);
            greep.setPointerCapture(e.pointerId);
            document.body.classList.add("kolom-slepen");
            const beweeg = ev => {
                const nieuw = [...breedtes];
                nieuw[i] = Math.max(36, breedtes[i] + ev.clientX - start);
                zet(tabel, koppen, nieuw);
            };
            const los = () => {
                greep.removeEventListener("pointermove", beweeg);
                greep.removeEventListener("pointerup", los);
                document.body.classList.remove("kolom-slepen");
                bewaar(sleutel, koppen);
            };
            greep.addEventListener("pointermove", beweeg);
            greep.addEventListener("pointerup", los);
        });
    });
}

// Alleen een tabel die echt breder is dan zijn kader krijgt een horizontale schuifbalk (.te-breed); de andere
// kaders scrollen niet zelf, zodat de kolomkoppen bovenaan het venster blijven staan bij het naar beneden scrollen.
const breedte = typeof ResizeObserver !== "undefined"
    ? new ResizeObserver(items => items.forEach(i => past(i.target.closest(".overzichtskaart"))))
    : null;

function past(kaart) {
    const tabel = kaart && kaart.querySelector(":scope > table");
    if (tabel) {
        kaart.classList.toggle("te-breed", tabel.scrollWidth > kaart.clientWidth + 1);
    }
}

// Elke tabel met data-sleepbaar="sleutel" krijgt vanzelf versleepbare kolommen, ook als Blazor hem later tekent.
function koppelAlle() {
    document.querySelectorAll("table[data-sleepbaar]:not([data-kolommen])").forEach(t => {
        if (t.tHead && t.tHead.rows.length > 0 && t.offsetParent !== null) {
            maak(t, t.dataset.sleepbaar);
            const kaart = t.parentElement && t.parentElement.closest(".overzichtskaart");
            if (kaart && breedte) {
                breedte.observe(kaart);
                breedte.observe(t);
            }
        }
    });
}

if (typeof document !== "undefined" && !window.kolommenGekoppeld) {
    window.kolommenGekoppeld = true;
    let gepland = false;
    new MutationObserver(() => {
        if (!gepland) {
            gepland = true;
            requestAnimationFrame(() => { gepland = false; koppelAlle(); });
        }
    }).observe(document.documentElement, { childList: true, subtree: true });
    koppelAlle();
}
