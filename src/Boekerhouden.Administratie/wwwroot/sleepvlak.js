// Het sleepvlak licht op zolang er bestanden boven hangen. Het bestandsveld zelf ligt over het vlak heen, dus het
// laten vallen en het kiezen doet de browser.
export function koppel(vlak) {
    let diepte = 0;
    const aan = (e) => {
        if (!e.dataTransfer?.types?.includes('Files')) return;
        diepte++;
        vlak.classList.add('sleept');
    };
    const uit = () => {
        diepte = Math.max(0, diepte - 1);
        if (diepte === 0) vlak.classList.remove('sleept');
    };
    const weg = () => {
        diepte = 0;
        vlak.classList.remove('sleept');
    };
    vlak.addEventListener('dragenter', aan);
    vlak.addEventListener('dragleave', uit);
    vlak.addEventListener('drop', weg);
    return {
        los: () => {
            vlak.removeEventListener('dragenter', aan);
            vlak.removeEventListener('dragleave', uit);
            vlak.removeEventListener('drop', weg);
        },
    };
}

// Slepen op de hele pagina: zolang er bestanden boven het venster hangen, krijgt het onzichtbare bestandsveld (dat het
// hele venster bedekt) de muis en verschijnt de rand; loslaten valt dus op het bestandsveld en de browser leest in.
export function koppelPagina(vlak) {
    let diepte = 0;
    const doel = document.documentElement;
    const aan = (e) => {
        if (!e.dataTransfer?.types?.includes('Files')) return;
        diepte++;
        vlak.classList.add('sleept');
    };
    const uit = () => {
        diepte = Math.max(0, diepte - 1);
        if (diepte === 0) vlak.classList.remove('sleept');
    };
    const weg = () => {
        diepte = 0;
        vlak.classList.remove('sleept');
    };
    doel.addEventListener('dragenter', aan);
    doel.addEventListener('dragleave', uit);
    doel.addEventListener('drop', weg);
    return {
        los: () => {
            doel.removeEventListener('dragenter', aan);
            doel.removeEventListener('dragleave', uit);
            doel.removeEventListener('drop', weg);
        },
    };
}
