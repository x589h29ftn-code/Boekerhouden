// Afdrukken controleren: een volle administratie (auditfile) inlezen en elk overzicht afdrukken zoals Ctrl+P (als
// PDF, met de afdrukstijl), licht en donker. Per pagina: de administratie bovenaan, paginanummers, geen menu, zoekknop,
// knoppen, niets rechts van het papier af, en zwart op wit. Meldt wat er misgaat; de PDF's in <map>.
// Gebruik: scripts/ui/herstart.sh && node scripts/ui/afdrukken.js <auditfile.xaf> <map>
// (de auditfile: BOEKERHOUDEN_AFDRUKGEGEVENS=<pad> dotnet test tests/Boekerhouden.Administratie.Tests --filter Afdrukgegevens)
const { chromium } = require('playwright');
const crypto = require('crypto');
const { execFileSync } = require('child_process');
const path = require('path');

function totp(sleutel) {
  const a = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'; let bits = '';
  for (const c of sleutel.replace(/\s/g, '').toUpperCase()) bits += a.indexOf(c).toString(2).padStart(5, '0');
  const b = []; for (let i = 0; i + 8 <= bits.length; i += 8) b.push(parseInt(bits.slice(i, i + 8), 2));
  const t = Buffer.alloc(8); t.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000)));
  const h = crypto.createHmac('sha1', Buffer.from(b)).update(t).digest(); const o = h[h.length - 1] & 0xf;
  return (((h[o] & 0x7f) << 24 | h[o + 1] << 16 | h[o + 2] << 8 | h[o + 3]) % 1000000).toString().padStart(6, '0');
}

(async () => {
  const [auditfile, uit] = [process.argv[2], process.argv[3] || '.'];
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1400, height: 900 } });
  const page = await context.newPage();
  const fouten = []; const meld = t => { fouten.push(t); console.log('FOUT: ' + t); };
  page.on('pageerror', e => meld('console: ' + e.message));
  const basis = 'http://localhost:5090';

  await page.goto(basis + '/'); await page.waitForURL(/eerste-gebruiker/);
  await page.fill('#naam', 'Bea Beheer'); await page.fill('#initialen', 'BB'); await page.fill('#email', 'beheer@kantoor.test');
  await page.fill('#wachtwoord', 'Groene-Appel-Boom-42'); await page.fill('#herhaal', 'Groene-Appel-Boom-42');
  await page.click('button[type=submit]'); await page.waitForURL(/account\/beveiliging/);
  await page.fill('#code', totp(await page.textContent('code.fs-6'))); await page.click('button:has-text("Aanzetten")');
  await page.waitForSelector('text=Je herstelcodes');
  await page.goto(basis + '/'); await page.waitForTimeout(1500);
  await page.click('text=Nieuwe administratie'); await page.waitForSelector('#naam'); await page.waitForTimeout(800);
  await page.setInputFiles('#auditfile', auditfile);
  await page.waitForURL(/\/a\/[0-9a-f-]+/, { timeout: 120000 }); const id = page.url().split('/a/')[1].split(/[/?#]/)[0];
  const naam = (await page.locator('.wisselaar-naam').first().innerText()).trim();
  console.log(`administratie ${naam}`);

  const jaar = 'van=2025-01-01&tot=2025-12-31';
  const paginas = [
    ['overzicht', ''], ['kolommenbalans', `/kolommenbalans?${jaar}`], ['kolommenbalans-maand', `/kolommenbalans?${jaar}&weergave=maand`],
    ['kolommenbalans-rgs', `/kolommenbalans?${jaar}&indeling=rgs`], ['grootboekkaart', `/grootboek?rekening=1100&${jaar}`],
    ['journaal', `/journaal?${jaar}`], ['debiteuren', '/debiteuren'], ['crediteuren', '/crediteuren'], ['btw', '/btw?jaar=2025'],
    ['fiscale-positie', '/vpb?jaar=2025'], ['activa', '/activa'], ['aansluitingen', '/aansluitingen'], ['inkoopboek', `/inkoopboek?${jaar}`],
    ['verkoopboek', `/verkoopboek?${jaar}`], ['facturen', '/facturen'], ['bank', '/bank'], ['memoriaal', '/memoriaal'], ['relaties', '/relaties'],
    ['rekeningschema', '/rekeningschema'], ['perioden', '/perioden'], ['balans', '/balans?jaar=2025'], ['proefbalans', '/proefbalans?jaar=2025'],
  ];
  const menuwoorden = ['Verwerken', 'Overzichten', 'Inrichting', 'Zoeken', 'Ctrl K', 'Bea Beheer · kantoor'];
  for (const thema of ['licht', 'donker']) {
    await page.evaluate(t => localStorage.setItem('thema', t), thema);
    for (const [wat, pad] of paginas) {
      await page.goto(`${basis}/a/${id}${pad}`); await page.waitForTimeout(1800);
      if (thema === 'donker') await page.evaluate(() => window.dispatchEvent(new Event('beforeprint')));
      await page.emulateMedia({ media: 'print' });
      // Niets rechts van het papier: A4 staand min de marges is ~186 mm = ~703 px.
      const liggend = await page.evaluate(() => !!document.querySelector('.administratie-inhoud .maandoverzicht'));
      await page.setViewportSize({ width: liggend ? 1047 : 703, height: 900 });
      const breed = await page.evaluate(() => {
        const rechts = document.documentElement.clientWidth + 2;
        return [...document.querySelectorAll('.administratie-inhoud *')].filter(e => e.checkVisibility())
          .filter(e => { const r = e.getBoundingClientRect(); return r.width > 0 && r.right > rechts && getComputedStyle(e).display !== 'none'; })
          .slice(0, 3).map(e => `${e.tagName.toLowerCase()}.${[...e.classList].join('.')} tot ${Math.round(e.getBoundingClientRect().right)}px`);
      });
      // Geen kolom zo smal dat de tekst letter voor letter onder elkaar komt.
      const smal = await page.evaluate(() => [...document.querySelectorAll('.administratie-inhoud td, .administratie-inhoud th')]
        .filter(c => c.checkVisibility() && c.innerText.trim().length >= 6 && c.getBoundingClientRect().width < 30)
        .slice(0, 3).map(c => `"${c.innerText.trim().slice(0, 20)}" ${Math.round(c.getBoundingClientRect().width)}px`));
      const leeg = await page.evaluate(() => !!document.querySelector('.administratie-inhoud .leeg-blok'));
      const titel = await page.evaluate(() => document.querySelector('.administratie-inhoud h1')?.innerText.trim() ?? '');
      const kleuren = await page.evaluate(() => {
        const slecht = [];
        for (const e of document.querySelectorAll('.administratie-inhoud *')) {
          if (!e.checkVisibility() || !e.childNodes.length || ![...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim())) continue;
          const c = getComputedStyle(e).color.match(/\d+/g).map(Number);
          if (c[0] + c[1] + c[2] > 3 * 170) slecht.push(`${e.tagName.toLowerCase()} "${e.textContent.trim().slice(0, 30)}" ${getComputedStyle(e).color}`);
        }
        return slecht.slice(0, 3);
      });
      const pdf = path.join(uit, `${thema}-${wat}.pdf`);
      await page.pdf({ path: pdf, preferCSSPageSize: true, printBackground: true });
      await page.emulateMedia({ media: 'screen' }); await page.setViewportSize({ width: 1400, height: 900 });
      if (thema === 'donker') await page.evaluate(() => window.dispatchEvent(new Event('afterprint')));
      const tekst = execFileSync('pdftotext', ['-layout', pdf, '-']).toString();
      const eerste = execFileSync('pdftotext', ['-layout', '-f', '1', '-l', '1', pdf, '-']).toString();
      const paginasPdf = Number(/Pages:\s+(\d+)/.exec(execFileSync('pdfinfo', [pdf]).toString())[1]);
      const kort = `${thema} ${wat}`;
      if (!tekst.includes(naam)) meld(`${kort}: de administratie staat er niet op`);
      if (!tekst.includes(`Pagina ${paginasPdf} van ${paginasPdf}`)) meld(`${kort}: geen paginanummer "Pagina ${paginasPdf} van ${paginasPdf}"`);
      for (const w of menuwoorden) if (tekst.includes(w)) meld(`${kort}: "${w}" staat op papier`);
      if (titel && !eerste.includes(titel.split('\n')[0])) meld(`${kort}: de titel "${titel}" staat niet op de eerste pagina`);
      if (!leeg && eerste.replace(/\s+/g, ' ').trim().split(' ').length < 25) meld(`${kort}: de eerste pagina is (bijna) leeg`);
      if (smal.length) meld(`${kort}: te smalle kolom: ${smal.join('; ')}`);
      if (breed.length) meld(`${kort}: rechts van het papier: ${breed.join('; ')}`);
      if (kleuren.length) meld(`${kort}: lichte tekst: ${kleuren.join('; ')}`);
      console.log(`${kort}: ${paginasPdf} pagina's`);
    }
  }

  console.log(fouten.length === 0 ? 'Alles goed.' : `${fouten.length} fouten.`);
  await browser.close();
  process.exit(fouten.length === 0 ? 0 : 1);
})();
