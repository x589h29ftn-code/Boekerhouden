// Van het rapport naar het werkprogramma of de proefbalans: de link krijgt de post mee die je in het rapport bekijkt
// (het laatste blok met data-post dat bovenaan het venster is begonnen). Dit loopt vóór de navigatie van Blazor
// (capture op window).
window.boekerhouden = window.boekerhouden || {};
window.boekerhouden.postInBeeld = () => {
    let post = null;
    for (const blok of document.querySelectorAll('.rapport-viewer [data-post]')) {
        if (blok.getBoundingClientRect().top > 120) {
            break;
        }
        post = blok.dataset.post;
    }
    return post;
};
window.addEventListener('click', e => {
    const link = e.target instanceof Element ? e.target.closest('a[data-volg-post]') : null;
    if (!link) {
        return;
    }
    const url = new URL(link.href, location.href);
    const post = window.boekerhouden.postInBeeld();
    if (post) {
        url.searchParams.set(link.dataset.volgPost, post);
    } else {
        url.searchParams.delete(link.dataset.volgPost);
    }
    link.href = url.toString();
}, true);

// Naar het anker in de adresbalk (bijv. #post-BMva), als de inhoud er pas na het laden staat.
window.boekerhouden.naarAnker = () => {
    const id = decodeURIComponent(location.hash.slice(1));
    const doel = id ? document.getElementById(id) : null;
    if (doel) {
        doel.scrollIntoView({ behavior: 'instant', block: 'start' });
    }
};

// Uitklapmenu's (gebruikersmenu, het menu bij een regel in het rapport) sluiten bij een klik ernaast, na een keuze
// in het menu, of met Escape.
document.addEventListener('click', e => {
    const keuze = e.target instanceof Element ? e.target.closest('details.uitklapmenu .menu button, details.uitklapmenu .menu a') : null;
    for (const menu of document.querySelectorAll('details.uitklapmenu[open]')) {
        if (!menu.contains(e.target) || (keuze && menu.contains(keuze))) {
            menu.open = false;
        }
    }
});
document.addEventListener('keydown', e => {
    if (e.key === 'Escape') {
        document.querySelectorAll('details.uitklapmenu[open]').forEach(menu => menu.open = false);
    }
});

// Zijkolommen (dossiermenu, onderdelen van het werkprogramma, inhoud van het rapport): inklappen met ‹ › en de
// breedte slepen aan de rand. De stand onthoudt de browser per soort kolom; ook na navigeren of opnieuw tekenen.
(() => {
    const sleutel = el => 'kolom:' + el.dataset.kolommen;
    const lees = el => {
        try {
            return JSON.parse(localStorage.getItem(sleutel(el)) || '{}');
        } catch {
            return {};
        }
    };
    const bewaar = (el, stand) => {
        try {
            localStorage.setItem(sleutel(el), JSON.stringify(stand));
        } catch {
            // Zonder opslag werkt het alleen voor deze pagina.
        }
    };
    const pasToe = el => {
        const stand = lees(el);
        if (stand.breedte) {
            el.style.setProperty('--zijbreedte', stand.breedte + 'px');
        }
        el.toggleAttribute('data-ingeklapt', !!stand.ingeklapt);
        el.dataset.kolomKlaar = '1';
    };
    const nieuwe = () => document.querySelectorAll('[data-kolommen]:not([data-kolom-klaar])').forEach(pasToe);
    new MutationObserver(nieuwe).observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener('DOMContentLoaded', nieuwe);
    nieuwe();

    document.addEventListener('click', e => {
        const knop = e.target instanceof Element ? e.target.closest('.kolom-knop') : null;
        const el = knop?.closest('[data-kolommen]');
        if (!el) {
            return;
        }
        const stand = lees(el);
        stand.ingeklapt = !stand.ingeklapt;
        bewaar(el, stand);
        pasToe(el);
    });

    document.addEventListener('pointerdown', e => {
        const greep = e.target instanceof Element ? e.target.closest('.kolom-greep') : null;
        const el = greep?.closest('[data-kolommen]');
        const kolom = greep?.closest('.zijkolom');
        if (!el || !kolom) {
            return;
        }
        e.preventDefault();
        const start = e.clientX;
        const breedte = kolom.getBoundingClientRect().width;
        let nieuw = breedte;
        greep.classList.add('actief');
        document.body.classList.add('kolom-slepen');
        const beweeg = m => {
            nieuw = Math.round(Math.min(Math.max(breedte + m.clientX - start, 140), Math.min(560, window.innerWidth * .5)));
            el.style.setProperty('--zijbreedte', nieuw + 'px');
        };
        const los = () => {
            window.removeEventListener('pointermove', beweeg);
            window.removeEventListener('pointerup', los);
            greep.classList.remove('actief');
            document.body.classList.remove('kolom-slepen');
            bewaar(el, { ...lees(el), breedte: nieuw, ingeklapt: false });
        };
        window.addEventListener('pointermove', beweeg);
        window.addEventListener('pointerup', los);
    });

    // Dubbelklik op de rand: terug naar de standaardbreedte.
    document.addEventListener('dblclick', e => {
        const greep = e.target instanceof Element ? e.target.closest('.kolom-greep') : null;
        const el = greep?.closest('[data-kolommen]');
        if (el) {
            el.style.removeProperty('--zijbreedte');
            bewaar(el, { ...lees(el), breedte: null });
        }
    });
})();
