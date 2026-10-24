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
