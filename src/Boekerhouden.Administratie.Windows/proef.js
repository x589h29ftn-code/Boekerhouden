// Knoppenproef voor de Windows-app (--proef), ook bruikbaar in de webapp: op de pagina die open staat alle schakelaars
// (weergaveknoppen, tabbladen, vinkjes en keuzelijsten in de kop, periode- en rekeningknoppen, openklapbare groepen) heen
// en terug, ook snel achter elkaar, plus "Opnieuw herkennen". Na elke klik: staat er een fout in beeld? Geeft
// { klikken, fouten: [...] } terug. Knoppen die iets wijzigen of verwijderen (boeken, dagboek, verwijderen) blijven
// buiten schot.
window.boekerhoudenProef = async function () {
    const wacht = ms => new Promise(r => setTimeout(r, ms));
    const zichtbaar = e => !!e && e.offsetParent !== null;
    const pad = location.pathname + location.search;
    const fouten = [];
    let klikken = 0;
    const foutInBeeld = () => {
        const ui = document.getElementById('blazor-error-ui');
        return document.body.innerText.includes('Er ging iets mis') || (ui && getComputedStyle(ui).display !== 'none');
    };
    const controle = async (wat, ms = 450) => {
        await wacht(ms);
        if (foutInBeeld()) {
            fouten.push(`${pad} › ${wat}`);
            return false;
        }
        return true;
    };
    const terug = async () => {
        if (location.pathname + location.search !== pad && !location.pathname.startsWith(pad.split('?')[0])) {
            Blazor.navigateTo(pad);
            await wacht(900);
        }
    };
    const tekst = e => (e.textContent || e.getAttribute('aria-label') || '').trim().slice(0, 30);
    const veilig = e => !/dagboek|verwijder|boeken|wissen|afsluiten/i.test(tekst(e));
    const kop = () => document.querySelector('.administratie-inhoud > div:first-child') || document;

    const groepknoppen = () => [...document.querySelectorAll('.btn-group .btn, .nav-tabs .nav-link, [role=tab]')].filter(e => zichtbaar(e) && veilig(e));
    for (let ronde = 1; ronde <= 2; ronde++) {
        const aantal = groepknoppen().length;
        for (let i = 0; i < aantal; i++) {
            const k = groepknoppen()[i];
            if (!k) break;
            const naam = tekst(k);
            k.click(); klikken++;
            await controle(`${naam} (ronde ${ronde})`);
            await terug();
        }
        for (let i = groepknoppen().length - 1; i >= 0; i--) {
            const k = groepknoppen()[i];
            if (k) { k.click(); klikken++; await wacht(60); }
        }
        await controle(`alles snel terug (ronde ${ronde})`, 900);
        await terug();
    }

    const vinkjes = () => [...kop().querySelectorAll('input[type=checkbox]')].filter(zichtbaar);
    for (let i = 0; i < vinkjes().length; i++) {
        for (let r = 1; r <= 3; r++) {
            const v = vinkjes()[i];
            if (!v) break;
            v.click(); klikken++;
            await controle(`vinkje ${i + 1} (${r}e keer)`);
        }
    }

    const lijsten = () => [...kop().querySelectorAll('select')].filter(zichtbaar);
    for (let i = 0; i < lijsten().length; i++) {
        const opties = [...lijsten()[i].options].map(o => o.value);
        for (const w of [...opties.slice(0, 6), opties[0]]) {
            const l = lijsten()[i];
            if (!l) break;
            l.value = w; l.dispatchEvent(new Event('change', { bubbles: true })); klikken++;
            await controle(`keuzelijst ${i + 1} = ${w}`, 600);
        }
    }

    for (const label of ['Vorige periode', 'Volgende periode', 'Vorige periode', 'Volgende periode', 'Volgende rekening', 'Volgende rekening', 'Vorige rekening', 'Vorige rekening']) {
        const k = document.querySelector(`button[aria-label="${label}"]`);
        if (zichtbaar(k) && !k.disabled) { k.click(); klikken++; await controle(label, 700); }
    }

    const groepen = () => [...document.querySelectorAll('tr:has(.pijl)')].filter(zichtbaar).slice(0, 12);
    for (let r = 1; r <= 2; r++) {
        for (let i = 0; i < groepen().length; i++) {
            const g = groepen()[i];
            if (!g) break;
            g.click(); klikken++;
            await controle(`groep ${i + 1} open/dicht (${r})`, 250);
        }
    }

    const herkennen = [...document.querySelectorAll('button')].find(b => zichtbaar(b) && !b.disabled && /^Opnieuw herkennen$/.test(b.textContent.trim()));
    if (herkennen) {
        herkennen.click(); klikken++;
        await controle('Opnieuw herkennen', 4000);
    }

    return { klikken, fouten };
};
