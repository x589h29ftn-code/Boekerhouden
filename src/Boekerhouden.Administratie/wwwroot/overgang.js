// Een paginawissel als zachte overvloeier (View Transitions): bij een klik op een link binnen Sifer maakt de browser
// een beeld van de huidige pagina, wacht tot Blazor de nieuwe pagina heeft getekend (of hoogstens 300 ms) en laat die
// dan zacht binnenkomen. De kopbalk en de zijbalk blijven staan (vormgeving.css). Zonder ondersteuning, bij "minder
// beweging" of met een sneltoets (nieuw tabblad) gebeurt er niets bijzonders.
(() => {
    if (!document.startViewTransition) {
        return;
    }

    const minderBeweging = window.matchMedia('(prefers-reduced-motion: reduce)');
    let bezig = false;

    document.addEventListener('click', (e) => {
        if (bezig || minderBeweging.matches || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) {
            return;
        }

        const link = e.target instanceof Element ? e.target.closest('a[href]') : null;
        if (!link || (link.target && link.target !== '_self') || link.hasAttribute('download')) {
            return;
        }

        const doel = new URL(link.href, location.href);
        if (doel.origin !== location.origin || (doel.pathname === location.pathname && doel.search === location.search)
            || doel.pathname.startsWith('/account') || doel.pathname.startsWith('/export')) {
            return;
        }

        // Het wachten begint al bij de klik: tekent Blazor heel snel, dan hoeft de overvloeier niet te wachten.
        let klaar;
        const getekend = new Promise((r) => { klaar = r; });
        const kijker = new MutationObserver(() => klaar());
        kijker.observe(document.body, { childList: true, subtree: true });
        const uiterlijk = setTimeout(klaar, 300);

        bezig = true;
        document.documentElement.classList.add('paginawissel');
        // Tijdens de overvloeier tekent de browser niet; wachten op een volgend beeld (requestAnimationFrame) zou vastlopen.
        const overgang = document.startViewTransition(() => getekend);
        overgang.ready.catch(() => { });
        overgang.updateCallbackDone.catch(() => { });
        overgang.finished.catch(() => { }).finally(() => {
            kijker.disconnect();
            clearTimeout(uiterlijk);
            document.documentElement.classList.remove('paginawissel');
            bezig = false;
        });
    }, true);
})();
