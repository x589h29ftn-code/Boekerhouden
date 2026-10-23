// Slepen van grootboekrekeningen naar posten (rubriceren). De koppeling zelf gebeurt op de server
// (Blazor @ondragstart/@ondrop); dit script laat alleen neerzetten toe op geldige posten en markeert het doel.
(() => {
    const doel = e => e.target instanceof Element ? e.target.closest('.dropdoel') : null;
    const wis = uitgezonderd => document.querySelectorAll('.dropdoel.doel').forEach(d => { if (d !== uitgezonderd) d.classList.remove('doel'); });

    document.addEventListener('dragstart', e => {
        if (e.target instanceof Element && e.target.closest('[draggable="true"]')) {
            e.dataTransfer?.setData('text/plain', 'rekening');
        }
    });
    document.addEventListener('dragover', e => {
        if (doel(e)) {
            e.preventDefault();
        }
    });
    document.addEventListener('dragenter', e => {
        const d = doel(e);
        wis(d);
        d?.classList.add('doel');
    });
    document.addEventListener('drop', () => wis(null));
    document.addEventListener('dragend', () => wis(null));
})();
