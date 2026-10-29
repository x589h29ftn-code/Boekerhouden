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
    constructor(container, url, opties) {
        this.container = container;
        this.url = url;
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
            this.pdf = await pdfjs.getDocument({ url: this.url, isEvalSupported: false, standardFontDataUrl: standaardLettertypen, withCredentials: true }).promise;
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
            this.selecteerbaar(item);
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

/** De viewer op het verwerkscherm; een gebied gaat naar .NET (Gebied), ook als het in het externe venster is aangewezen. */
export async function toon(container, url, id, dotnet) {
    const verbinding = kanaal(id);
    const viewer = new Viewer(container, url, {
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
        extern: (adres) => window.open(adres, `factuur-${id}`, 'popup,width=900,height=1100'),
        sluit: () => verbinding.close(),
    };
}

/** Het externe venster: alleen de viewer; een gebied gaat via het kanaal naar het verwerkscherm. */
export async function extern(container) {
    const verbinding = kanaal(container.dataset.document);
    const viewer = new Viewer(container, container.dataset.url, {
        opGebied: (pagina, x0, y0, x1, y1) => verbinding.postMessage({ soort: 'gebied', pagina, x0, y0, x1, y1 }),
    });
    verbinding.onmessage = (e) => {
        if (e.data?.soort === 'doel') viewer.zetDoel(e.data.naam);
    };
    window.addEventListener('pagehide', () => verbinding.postMessage({ soort: 'gesloten' }));
    await viewer.laad();
    verbinding.postMessage({ soort: 'geopend' });
}
