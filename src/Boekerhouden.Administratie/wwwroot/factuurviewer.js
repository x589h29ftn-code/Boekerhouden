// Factuurviewer: toont een PDF met pdf.js en laat de gebruiker een gebied aanwijzen (slepen, of klikken op een woord).
// Het gebied gaat in PDF-punten naar de server; die haalt zelf de tekst eruit. Het verwerkscherm en het externe
// venster (voor een tweede beeldscherm) praten met elkaar via een BroadcastChannel per document.
import * as pdfjs from './lib/pdfjs/pdf.min.mjs';

pdfjs.GlobalWorkerOptions.workerSrc = new URL('./lib/pdfjs/pdf.worker.min.mjs', import.meta.url).href;

const standaardLettertypen = new URL('./lib/pdfjs/standard_fonts/', import.meta.url).href;

function maak(tag, klasse, tekst) {
    const element = document.createElement(tag);
    if (klasse) element.className = klasse;
    if (tekst !== undefined) element.textContent = tekst;
    return element;
}

class Viewer {
    /** @param bron het adres van de PDF, of (Windows-app) een stroom uit .NET met de inhoud. */
    constructor(container, bron, opties) {
        this.container = container;
        this.bron = bron;
        this.opties = opties;
        this.zoom = 1;
        this.paginas = [];
        this.container.replaceChildren();
        this.container.classList.add('factuurviewer');

        const balk = maak('div', 'viewerbalk');
        const knop = (tekst, titel, actie) => {
            const b = maak('button', 'btn btn-sm btn-light', tekst);
            b.type = 'button';
            b.title = titel;
            b.addEventListener('click', actie);
            balk.append(b);
            return b;
        };
        knop('−', 'Kleiner', () => this.zoomNaar(this.zoom / 1.2));
        knop('+', 'Groter', () => this.zoomNaar(this.zoom * 1.2));
        knop('↔', 'Passend', () => this.zoomNaar(1));
        this.doel = maak('span', 'viewerdoel small text-secondary ms-2');
        balk.append(this.doel);
        if (opties.extern) {
            const b = knop('Extern', 'In een apart venster openen (bijv. op een tweede beeldscherm)', opties.extern);
            b.classList.add('ms-auto');
        }
        this.vlak = maak('div', 'viewervlak');
        this.container.append(balk, this.vlak);
        this.waarneming = new ResizeObserver(() => this.tekenOpnieuw());
    }

    async laad() {
        try {
            const bron = typeof this.bron === 'string'
                ? { url: this.bron, withCredentials: true }
                : { data: new Uint8Array(await this.bron.arrayBuffer()) };
            this.pdf = await pdfjs.getDocument({ ...bron, isEvalSupported: false, standardFontDataUrl: standaardLettertypen }).promise;
        } catch {
            this.vlak.replaceChildren(maak('p', 'text-secondary p-3', 'Dit document is niet als PDF te tonen.'));
            return;
        }
        for (let n = 1; n <= this.pdf.numPages; n++) {
            const pagina = await this.pdf.getPage(n);
            const vak = maak('div', 'pdfpagina');
            const canvas = maak('canvas');
            vak.append(canvas);
            this.vlak.append(vak);
            const item = { nummer: n, pagina, vak, canvas, viewport: null };
            this.paginas.push(item);
            if (this.opties.opGebied) this.selecteerbaar(item);
        }
        await this.teken();
        this.waarneming.observe(this.vlak);
    }

    breedte() {
        return Math.max(200, this.vlak.clientWidth - 24);
    }

    async teken() {
        const breedte = this.breedte();
        this.getekendBij = breedte;
        const dpr = window.devicePixelRatio || 1;
        for (const item of this.paginas) {
            const basis = item.pagina.getViewport({ scale: 1 });
            const viewport = item.pagina.getViewport({ scale: (breedte / basis.width) * this.zoom });
            item.viewport = viewport;
            const { canvas } = item;
            canvas.width = Math.floor(viewport.width * dpr);
            canvas.height = Math.floor(viewport.height * dpr);
            canvas.style.width = `${Math.floor(viewport.width)}px`;
            canvas.style.height = `${Math.floor(viewport.height)}px`;
            item.vak.style.width = canvas.style.width;
            item.vak.querySelector('.selectie')?.remove();
            await item.pagina.render({ canvasContext: canvas.getContext('2d'), canvas, viewport, transform: dpr === 1 ? null : [dpr, 0, 0, dpr, 0, 0] }).promise;
        }
        this.tekenMarkering(false);
    }

    /** Laat de plekken (PDF-punten) oplichten waar het gekozen veld op de factuur staat, en scrolt naar de eerste. */
    markeer(plekken) {
        this.markering = plekken ?? [];
        this.tekenMarkering(true);
    }

    tekenMarkering(scrollen) {
        this.container.querySelectorAll('.vindplaats').forEach((m) => m.remove());
        let eerste = null;
        for (const p of this.markering ?? []) {
            const item = this.paginas[p.pagina - 1];
            if (!item?.viewport) continue;
            const [xa, ya, xb, yb] = item.viewport.convertToViewportRectangle([p.x0, p.y0, p.x1, p.y1]);
            const vak = maak('div', 'vindplaats');
            vak.style.left = `${Math.min(xa, xb) - 2}px`;
            vak.style.top = `${Math.min(ya, yb) - 2}px`;
            vak.style.width = `${Math.abs(xb - xa) + 4}px`;
            vak.style.height = `${Math.abs(yb - ya) + 4}px`;
            item.vak.append(vak);
            eerste ??= vak;
        }
        if (scrollen && eerste) eerste.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    }

    tekenOpnieuw() {
        if (!this.pdf || Math.abs(this.breedte() - this.getekendBij) < 8) return;
        clearTimeout(this.wachten);
        this.wachten = setTimeout(() => this.teken(), 150);
    }

    zoomNaar(zoom) {
        this.zoom = Math.min(4, Math.max(0.4, zoom));
        this.teken();
    }

    zetDoel(naam) {
        this.doel.textContent = naam ? `→ ${naam}` : '';
    }

    selecteerbaar(item) {
        let start = null;
        let kader = null;
        const punt = (e) => {
            const r = item.vak.getBoundingClientRect();
            return { x: Math.min(Math.max(e.clientX - r.left, 0), r.width), y: Math.min(Math.max(e.clientY - r.top, 0), r.height) };
        };
        const zet = (a, b) => {
            kader.style.left = `${Math.min(a.x, b.x)}px`;
            kader.style.top = `${Math.min(a.y, b.y)}px`;
            kader.style.width = `${Math.abs(a.x - b.x)}px`;
            kader.style.height = `${Math.abs(a.y - b.y)}px`;
        };
        item.vak.addEventListener('pointerdown', (e) => {
            if (e.button !== 0) return;
            e.preventDefault();
            item.vak.setPointerCapture(e.pointerId);
            this.container.querySelectorAll('.selectie').forEach((s) => s.remove());
            start = punt(e);
            kader = maak('div', 'selectie');
            item.vak.append(kader);
            zet(start, start);
        });
        item.vak.addEventListener('pointermove', (e) => {
            if (start) zet(start, punt(e));
        });
        item.vak.addEventListener('pointerup', (e) => {
            if (!start) return;
            const eind = punt(e);
            const klik = Math.abs(eind.x - start.x) < 4 && Math.abs(eind.y - start.y) < 4;
            if (klik) kader.remove();
            const [x0, y0] = item.viewport.convertToPdfPoint(start.x, start.y);
            const [x1, y1] = klik ? [x0 + 0.5, y0 + 0.5] : item.viewport.convertToPdfPoint(eind.x, eind.y);
            start = null;
            this.opties.opGebied(item.nummer, x0, y0, x1, y1);
        });
    }
}

const kanaal = (id) => new BroadcastChannel(`factuur-${id}`);

// Het externe venster van het verwerkscherm. De module blijft bestaan zolang de app open is, dus ook een volgende
// factuur (een nieuw scherm) weet dat het venster er nog is en toont daar zijn eigen document.
let externVenster = null;

/** Staat de factuurviewer in een apart venster (en is dat venster nog open)? */
export function externOpen() {
    return externVenster !== null && !externVenster.closed;
}

/** Alleen bekijken (bijv. bij een boeking): geen gebied aanwijzen, geen apart venster. */
export async function bekijk(container, bron) {
    const viewer = new Viewer(container, bron, {});
    await viewer.laad();
    return { sluit: () => viewer.waarneming.disconnect() };
}

/** De viewer op het verwerkscherm; een gebied gaat naar .NET (Gebied), ook als het in het externe venster is aangewezen. */
export async function toon(container, bron, id, dotnet) {
    const verbinding = kanaal(id);
    const viewer = new Viewer(container, bron, {
        opGebied: (pagina, x0, y0, x1, y1) => dotnet.invokeMethodAsync('Gebied', pagina, x0, y0, x1, y1),
        extern: () => dotnet.invokeMethodAsync('NaarExtern'),
    });
    verbinding.onmessage = (e) => {
        const m = e.data;
        if (m?.soort === 'gebied') dotnet.invokeMethodAsync('Gebied', m.pagina, m.x0, m.y0, m.x1, m.y1);
        else if (m?.soort === 'gesloten') dotnet.invokeMethodAsync('ExternGesloten');
        else if (m?.soort === 'geopend') verbinding.postMessage({ soort: 'doel', naam: viewer.laatsteDoel ?? '' });
    };
    await viewer.laad();
    return {
        doel: (naam) => {
            viewer.laatsteDoel = naam;
            viewer.zetDoel(naam);
            verbinding.postMessage({ soort: 'doel', naam });
        },
        markeer: (plekken) => {
            viewer.markeer(plekken);
            verbinding.postMessage({ soort: 'markeer', plekken });
        },
        // Eén vast venster: bij de volgende factuur toont hetzelfde venster het nieuwe document.
        extern: (adres) => {
            if (externOpen()) externVenster.location.replace(adres);
            else externVenster = window.open(adres, 'factuurviewer', 'popup,width=900,height=1100');
        },
        sluit: () => verbinding.close(),
    };
}

/** Het externe venster: alleen de viewer; een gebied gaat via het kanaal naar het verwerkscherm. */
export async function extern(container, bron, id) {
    const verbinding = kanaal(id ?? container.dataset.document);
    const viewer = new Viewer(container, bron ?? container.dataset.url, {
        opGebied: (pagina, x0, y0, x1, y1) => verbinding.postMessage({ soort: 'gebied', pagina, x0, y0, x1, y1 }),
    });
    verbinding.onmessage = (e) => {
        if (e.data?.soort === 'doel') viewer.zetDoel(e.data.naam);
        else if (e.data?.soort === 'markeer') viewer.markeer(e.data.plekken);
    };
    window.addEventListener('pagehide', () => verbinding.postMessage({ soort: 'gesloten' }));
    await viewer.laad();
    verbinding.postMessage({ soort: 'geopend' });
}

const weergaveSleutel = 'boekerhouden.inkoop.weergave';

/** De weergave van het verwerkscherm (welke onderdelen zichtbaar zijn), per browser onthouden. */
export function leesWeergave() {
    try {
        return localStorage.getItem(weergaveSleutel);
    } catch {
        return null;
    }
}

export function bewaarWeergave(waarde) {
    try {
        localStorage.setItem(weergaveSleutel, waarde);
    } catch {
        // Geen opslag (bijv. privévenster): dan geldt de standaardweergave.
    }
}

const breedteSleutel = 'boekerhouden.inkoop.viewerbreedte';

/**
 * De scheiding tussen formulier en factuur: slepen maakt de factuur (rechts) breder of smaller, dubbelklikken zet de
 * standaardbreedte terug. De breedte wordt per browser onthouden.
 */
export function scheiding(handvat, paneel) {
    // Eén keer per element: het scherm kan dit vaker vragen (bijv. na heropenen).
    if (handvat.dataset.gekoppeld) return;
    handvat.dataset.gekoppeld = '1';
    const zet = (breedte) => {
        if (breedte) paneel.style.width = `${breedte}px`;
        else paneel.style.removeProperty('width');
    };
    try {
        const opgeslagen = Number(localStorage.getItem(breedteSleutel));
        if (opgeslagen > 0) zet(Math.min(opgeslagen, window.innerWidth * 0.8));
    } catch {
        // Geen opslag: de standaardbreedte.
    }

    let slepen = false;
    handvat.addEventListener('pointerdown', (e) => {
        slepen = true;
        handvat.setPointerCapture(e.pointerId);
        document.body.classList.add('scheiding-slepen');
        e.preventDefault();
    });
    handvat.addEventListener('pointermove', (e) => {
        if (!slepen) return;
        const rechts = paneel.getBoundingClientRect().right;
        // Het formulier links houdt minstens 520 pixels.
        const max = Math.min(window.innerWidth * 0.8, paneel.parentElement.getBoundingClientRect().width - 520 - handvat.offsetWidth);
        zet(Math.round(Math.min(max, Math.max(320, rechts - e.clientX - handvat.offsetWidth / 2))));
    });
    handvat.addEventListener('pointerup', () => {
        slepen = false;
        document.body.classList.remove('scheiding-slepen');
        try {
            localStorage.setItem(breedteSleutel, String(Math.round(paneel.getBoundingClientRect().width)));
        } catch {
            // Niet onthouden; de breedte geldt dan alleen nu.
        }
    });
    handvat.addEventListener('dblclick', () => {
        zet(null);
        try {
            localStorage.removeItem(breedteSleutel);
        } catch {
            // Niets te vergeten.
        }
    });
}

