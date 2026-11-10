// Stapel splitsen: miniaturen van alle pagina's van een PDF (pas getekend als ze in beeld komen) en één pagina groot
// in het paneel ernaast. De pagina's zelf (tegels, knippen) tekent Blazor; dit script vult alleen de canvassen en
// geeft toetsen door: pijltjes bladeren, K knipt, W laat weg, Esc sluit het grote beeld.
import * as pdfjs from './lib/pdfjs/pdf.min.mjs';

pdfjs.GlobalWorkerOptions.workerSrc = new URL('./lib/pdfjs/pdf.worker.min.mjs', import.meta.url).href;

const standaardLettertypen = new URL('./lib/pdfjs/standard_fonts/', import.meta.url).href;

class Stapel {
    constructor(container, paneel, dotnet) {
        this.container = container;
        this.paneel = paneel;
        this.dotnet = dotnet;
        this.getekend = new WeakSet();
        this.draai = 0;
        this.groot = null;
        this.inBeeld = new IntersectionObserver((items) => {
            for (const item of items) {
                if (item.isIntersecting) this.tekenMiniatuur(item.target);
            }
        }, { rootMargin: '400px' });
        this.toetsen = (e) => this.toets(e);
        document.addEventListener('keydown', this.toetsen);
        this.formaat = new ResizeObserver(() => { if (this.groot) this.toon(this.groot); });
        this.formaat.observe(paneel);
    }

    /** @param bron het adres van de PDF, of (Windows-app) een stroom uit .NET met de inhoud. */
    async laad(bron) {
        const gegevens = typeof bron === 'string'
            ? { url: bron, withCredentials: true }
            : { data: new Uint8Array(await bron.arrayBuffer()) };
        this.pdf = await pdfjs.getDocument({ ...gegevens, isEvalSupported: false, standardFontDataUrl: standaardLettertypen }).promise;
        this.ververs();
        return this.pdf.numPages;
    }

    /** Na elke tekenronde van Blazor: nieuwe canvassen gaan onder de waarneming. */
    ververs() {
        if (!this.pdf) return;
        for (const canvas of this.container.querySelectorAll('canvas[data-pagina]')) {
            if (!this.getekend.has(canvas)) this.inBeeld.observe(canvas);
        }
    }

    async tekenMiniatuur(canvas) {
        if (this.getekend.has(canvas) || !this.pdf) return;
        this.getekend.add(canvas);
        this.inBeeld.unobserve(canvas);
        const pagina = await this.pdf.getPage(Number(canvas.dataset.pagina));
        const basis = pagina.getViewport({ scale: 1 });
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        const breedte = canvas.clientWidth || 150;
        const viewport = pagina.getViewport({ scale: (breedte / basis.width) * dpr });
        canvas.width = Math.round(viewport.width);
        canvas.height = Math.round(viewport.height);
        canvas.style.aspectRatio = `${basis.width} / ${basis.height}`;
        await pagina.render({ canvasContext: canvas.getContext('2d'), viewport }).promise;
        canvas.classList.add('klaar');
    }

    /** Toont een pagina groot in het paneel (0 = niets). */
    async toon(nummer) {
        if (nummer !== this.groot) this.draai = 0;
        this.groot = nummer || null;
        const canvas = this.paneel.querySelector('canvas');
        if (!canvas || !this.pdf || !nummer) return;
        this.taak?.cancel();
        const pagina = await this.pdf.getPage(nummer);
        const basis = pagina.getViewport({ scale: 1, rotation: (pagina.rotate + this.draai) % 360 });
        const dpr = window.devicePixelRatio || 1;
        const breedte = Math.max(200, canvas.parentElement.clientWidth - 2);
        const viewport = pagina.getViewport({ scale: (breedte / basis.width) * dpr, rotation: (pagina.rotate + this.draai) % 360 });
        canvas.width = Math.round(viewport.width);
        canvas.height = Math.round(viewport.height);
        canvas.style.width = `${breedte}px`;
        this.taak = pagina.render({ canvasContext: canvas.getContext('2d'), viewport });
        try {
            await this.taak.promise;
        } catch {
            // Afgebroken door een volgende pagina.
        }
        this.container.querySelector(`[data-blad="${nummer}"]`)?.scrollIntoView({ block: 'nearest' });
    }

    /** Draait het grote beeld een kwartslag (alleen om te kijken; de factuur zelf verandert niet). */
    draaiKwart() {
        this.draai = (this.draai + 90) % 360;
        return this.toon(this.groot);
    }

    toets(e) {
        if (e.ctrlKey || e.metaKey || e.altKey) return;
        const doel = e.target;
        if (doel instanceof HTMLElement && (doel.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(doel.tagName))) return;
        const actie = { ArrowRight: 'volgende', ArrowDown: 'volgende', ArrowLeft: 'vorige', ArrowUp: 'vorige', k: 'knip', K: 'knip', w: 'weg', W: 'weg', Delete: 'weg', Escape: 'sluit' }[e.key];
        if (!actie) return;
        // Zonder gekozen pagina doen alleen de pijltjes iets (ze kiezen de eerste); de pagina scrolt dan gewoon.
        if (!this.groot && actie !== 'volgende') return;
        e.preventDefault();
        this.dotnet.invokeMethodAsync('Toets', actie);
    }

    sluit() {
        document.removeEventListener('keydown', this.toetsen);
        this.inBeeld.disconnect();
        this.formaat.disconnect();
        this.taak?.cancel();
        this.pdf?.destroy();
    }
}

export async function koppel(container, paneel, bron, dotnet) {
    const stapel = new Stapel(container, paneel, dotnet);
    try {
        await stapel.laad(bron);
    } catch {
        container.classList.add('onleesbaar');
    }
    return stapel;
}
