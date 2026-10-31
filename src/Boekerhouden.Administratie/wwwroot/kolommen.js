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

export function maak(tabel, sleutel) {
    if (!tabel || !tabel.tHead || tabel.dataset.kolommen) {
        return;
    }

    tabel.dataset.kolommen = sleutel;
    const koppen = [...tabel.tHead.rows[0].cells];
    const oorspronkelijk = koppen.map(k => Math.round(k.getBoundingClientRect().width));
    const bewaard = lees(sleutel);
    tabel.style.tableLayout = "fixed";
    zet(tabel, koppen, bewaard && bewaard.length === koppen.length ? bewaard : oorspronkelijk);

    koppen.forEach((kop, i) => {
        const greep = document.createElement("span");
        greep.className = "kolomgreep";
        greep.title = "Slepen: breder of smaller · dubbelklik: terug";
        kop.appendChild(greep);
        greep.addEventListener("click", e => e.stopPropagation());
        greep.addEventListener("dblclick", e => {
            e.stopPropagation();
            try { localStorage.removeItem(voorvoegsel + sleutel); } catch { /* geen opslag */ }
            zet(tabel, koppen, oorspronkelijk);
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

// Elke tabel met data-sleepbaar="sleutel" krijgt vanzelf versleepbare kolommen, ook als Blazor hem later tekent.
function koppelAlle() {
    document.querySelectorAll("table[data-sleepbaar]:not([data-kolommen])").forEach(t => {
        if (t.tHead && t.tHead.rows.length > 0 && t.offsetParent !== null) {
            maak(t, t.dataset.sleepbaar);
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
