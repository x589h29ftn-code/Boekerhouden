// De factuurontwerper: een blok op het A4 aanklikken kiest het, slepen verplaatst het, het handvat rechts maakt het
// breder of smaller, en de pijltjestoetsen schuiven het gekozen blok een millimeter (met Shift vijf). De pagina staat
// geschaald in het vlak (data-schaal); alle maten gaan in millimeters naar .NET, dat het ontwerp bijwerkt en de pagina
// opnieuw tekent. Het luisteren gebeurt op het vlak zelf, zodat het blijft werken als de pagina opnieuw getekend wordt.
const pxPerMm = 96 / 25.4;

function mm(waarde) {
    const getal = parseFloat(waarde);
    return Number.isFinite(getal) ? getal : 0;
}

export function koppel(vlak, dotnet) {
    let sleep = null;
    const schaal = () => parseFloat(vlak.dataset.schaal || "1") * pxPerMm;

    function plek(blok) {
        const stroom = blok.dataset.stroom === "1";
        const ouder = blok.parentElement;
        return {
            x: mm(blok.style.left),
            y: stroom ? mm(ouder.style.paddingTop) : mm(blok.style.top),
            breedte: mm(blok.style.width),
        };
    }

    function omlaag(e) {
        if (e.button !== 0) {
            return;
        }

        const blok = e.target.closest("[data-blok]");
        if (!blok || !vlak.contains(blok)) {
            if (e.target.closest(".fp")) {
                dotnet.invokeMethodAsync("Kies", null);
            }
            return;
        }

        e.preventDefault();
        vlak.focus({ preventScroll: true });
        blok.setPointerCapture(e.pointerId);
        sleep = {
            blok,
            greep: !!e.target.closest("[data-greep]"),
            regels: blok.classList.contains("fp-regels") || blok.classList.contains("fp-stroomblok"),
            betaal: blok.classList.contains("fp-betaal"),
            beginX: e.clientX,
            beginY: e.clientY,
            begin: plek(blok),
            nu: plek(blok),
            bewogen: false,
        };
    }

    function beweeg(e) {
        if (!sleep) {
            return;
        }

        const dx = (e.clientX - sleep.beginX) / schaal();
        const dy = (e.clientY - sleep.beginY) / schaal();
        if (!sleep.bewogen && Math.abs(dx) < 0.8 && Math.abs(dy) < 0.8) {
            return;
        }

        sleep.bewogen = true;
        const b = sleep.begin;
        if (sleep.greep) {
            sleep.nu = { ...b, breedte: Math.max(10, Math.min(210 - b.x, Math.round(b.breedte + dx))) };
            sleep.blok.style.width = sleep.nu.breedte + "mm";
            return;
        }

        const x = Math.max(0, Math.min(210 - b.breedte, Math.round(b.x + dx)));
        const y = sleep.betaal ? b.y : Math.max(0, Math.min(292, Math.round(b.y + dy)));
        sleep.nu = { ...b, x, y };
        sleep.blok.style.left = x + "mm";
        if (sleep.regels) {
            sleep.blok.parentElement.style.paddingTop = y + "mm";
        } else if (!sleep.betaal) {
            sleep.blok.style.top = y + "mm";
        }
    }

    function los() {
        if (!sleep) {
            return;
        }

        const s = sleep;
        sleep = null;
        if (s.bewogen) {
            dotnet.invokeMethodAsync("Verplaats", s.blok.dataset.blok, s.nu.x, s.nu.y, s.nu.breedte);
        } else {
            dotnet.invokeMethodAsync("Kies", s.blok.dataset.blok);
        }
    }

    function toets(e) {
        const blok = vlak.querySelector("[data-blok].gekozen");
        const stap = e.shiftKey ? 5 : 1;
        const richting = { ArrowLeft: [-stap, 0], ArrowRight: [stap, 0], ArrowUp: [0, -stap], ArrowDown: [0, stap] }[e.key];
        if (!blok || !richting || e.target.closest("input, textarea, select")) {
            return;
        }

        e.preventDefault();
        const p = plek(blok);
        dotnet.invokeMethodAsync("Verplaats", blok.dataset.blok, p.x + richting[0], p.y + richting[1], p.breedte);
    }

    vlak.addEventListener("pointerdown", omlaag);
    vlak.addEventListener("pointermove", beweeg);
    vlak.addEventListener("pointerup", los);
    vlak.addEventListener("pointercancel", los);
    vlak.addEventListener("keydown", toets);
    return {
        ontkoppel() {
            vlak.removeEventListener("pointerdown", omlaag);
            vlak.removeEventListener("pointermove", beweeg);
            vlak.removeEventListener("pointerup", los);
            vlak.removeEventListener("pointercancel", los);
            vlak.removeEventListener("keydown", toets);
        },
    };
}
