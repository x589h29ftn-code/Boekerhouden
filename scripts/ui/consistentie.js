// Lay-out en consistentie van alle pagina's, op een laptop (1366x768) en een breed scherm (1920x1080): geen
// horizontale schuifbalk, precies één titel, hoogstens een paar knoppen in de kop en één primaire knop, geen losse tekens
// als icoon, geen Engelse woorden, geen afgekapte tekst zonder tooltip, elk veld met een label, geen fouten in de
// console. Meldt per pagina wat afwijkt; de schermafbeeldingen in <map>.
// Gebruik: scripts/ui/herstart.sh && node scripts/ui/consistentie.js <auditfile.xaf> <map>
const { chromium } = require('playwright');
const crypto = require('crypto');
const path = require('path');
const fs = require('fs');

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
  fs.mkdirSync(uit, { recursive: true });
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  const meldingen = []; const meld = (p, t) => { meldingen.push(`${p}: ${t}`); };
  let huidig = '';
  page.on('pageerror', e => meld(huidig, 'console: ' + e.message));
  page.on('console', m => { if (m.type() === 'error' && !/favicon|ERR_ABORTED/.test(m.text())) meld(huidig, 'console: ' + m.text().slice(0, 160)); });
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
  await page.waitForURL(/\/a\/[0-9a-f-]{36}/, { timeout: 120000 });
  const id = page.url().split('/a/')[1].split(/[/?#]/)[0];
  const j = 'van=2025-01-01&tot=2025-12-31';
  const a = p => `/a/${id}${p}`;
  const paginas = ['/', '/administraties/nieuw', '/hulp', '/beheer/gebruikers', '/beheer/btw', '/beheer/instellingen', '/beheer/wijzigingslog',
    a(''), a('/inkoop'), a('/verkoop'), a('/verkoop/nieuw'), a('/verkoop/terugkerend'), a('/facturen'), a('/factuurlayout'), a('/bank'), a('/bank/openstaand'),
    a('/memoriaal'), a(`/kolommenbalans?${j}`), a(`/kolommenbalans?${j}&weergave=maand`), a(`/grootboek?rekening=1100&${j}`), a(`/grootboek?${j}`),
    a(`/journaal?${j}`), a('/debiteuren'), a('/debiteuren?weergave=totalen'), a('/debiteuren?weergave=posten'), a('/crediteuren'), a('/btw?jaar=2025'), a('/btw/rondrekening'), a('/vpb?jaar=2025'), a('/activa'), a('/activa/staat'),
    a('/activa/nieuw'), a('/aansluitingen'), a(`/inkoopboek?${j}`), a(`/verkoopboek?${j}`), a('/relaties'), a('/regels'), a('/rekeningschema'),
    a('/perioden'), a('/instellingen'), a('/beginbalans'), a('/balans?jaar=2025'), a('/proefbalans?jaar=2025')];
  const engels = /\b(Save|Cancel|Delete|Loading|Submit|Search|Edit|Close|Settings|Error|Upload|Download|Back|Next|Previous)\b/;
  const losseTekens = /^[‹›×↻→←«»✕✓+\-]$/;

  for (const [breedte, hoogte] of [[1366, 768], [1920, 1080]]) {
    await page.setViewportSize({ width: breedte, height: hoogte });
    for (const pad of paginas) {
      huidig = `${pad.replace(`/a/${id}`, '/a/…')} @${breedte}`;
      await page.goto(basis + pad); await page.waitForTimeout(1800);
      const r = await page.evaluate(([engelsBron, losBron]) => {
        const engels = new RegExp(engelsBron), los = new RegExp(losBron);
        const zichtbaar = e => e.checkVisibility() && e.getBoundingClientRect().width > 0;
        const uit = {};
        uit.schuif = document.documentElement.scrollWidth - document.documentElement.clientWidth;
        uit.titels = [...document.querySelectorAll('h1')].filter(zichtbaar).length;
        const kop = document.querySelector('.paginakop')?.parentElement;
        if (kop) {
          const knoppen = [...kop.querySelectorAll('button, a.btn, label.btn, summary.btn, select')].filter(e => zichtbaar(e) && !e.closest('.paginakop') && !e.closest('.menu'));
          // Een groep schakelaars (Totaal | Per maand) of pijltjes (vorige | volgende) telt als één element.
          uit.kopknoppen = new Set(knoppen.map(e => e.closest('.btn-group, .periodekeuze, .bladeren') ?? e)).size;
          uit.primair = new Set(knoppen.filter(e => e.classList.contains('btn-primary')).map(e => e.closest('.splitsknop') ?? e)).size;
        }
        uit.los = [...document.querySelectorAll('button, a')].filter(e => zichtbaar(e) && los.test(e.textContent.trim())).map(e => e.textContent.trim()).slice(0, 3);
        const tekst = document.querySelector('main')?.innerText ?? document.body.innerText;
        uit.engels = (tekst.match(new RegExp(engels.source, 'g')) || []).slice(0, 3);
        uit.afgekapt = [...document.querySelectorAll('main *')].filter(e => zichtbaar(e) && e.children.length === 0 && e.scrollWidth > e.clientWidth + 1
          && getComputedStyle(e).textOverflow === 'ellipsis' && !e.closest('[title]') && !(e.tagName === 'INPUT' || e.tagName === 'SELECT'))
          .map(e => `"${e.textContent.trim().slice(0, 25)}"`).slice(0, 3);
        uit.zonderLabel = [...document.querySelectorAll('main input, main select, main textarea')].filter(e => zichtbaar(e)
          && e.getAttribute('aria-hidden') !== 'true' && !['hidden', 'checkbox', 'radio', 'file', 'submit', 'button'].includes(e.type)
          && !e.getAttribute('aria-label') && !e.getAttribute('aria-labelledby') && !e.closest('label')
          && !(e.id && document.querySelector(`label[for="${CSS.escape(e.id)}"]`)) && !e.title)
          .map(e => e.id || e.name || e.placeholder || e.type).slice(0, 4);
        return uit;
      }, [engels.source, losseTekens.source]);
      if (r.schuif > 1) meld(huidig, `horizontale schuifbalk (${r.schuif}px)`);
      if (r.titels !== 1) meld(huidig, `${r.titels} titels (h1)`);
      if (r.kopknoppen > 6) meld(huidig, `${r.kopknoppen} knoppen en keuzes in de kop`);
      if (r.primair > 1) meld(huidig, `${r.primair} primaire knoppen in de kop`);
      if (r.los.length) meld(huidig, `losse tekens als knop: ${r.los.join(' ')}`);
      if (r.engels.length) meld(huidig, `Engels: ${r.engels.join(', ')}`);
      if (r.afgekapt.length) meld(huidig, `afgekapt zonder tooltip: ${r.afgekapt.join(', ')}`);
      if (r.zonderLabel.length) meld(huidig, `velden zonder label: ${r.zonderLabel.join(', ')}`);
      if (breedte === 1366) await page.screenshot({ path: path.join(uit, pad.replace(`/a/${id}`, 'a').replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '') + '.png') || 'start.png' });
    }
  }

  for (const m of [...new Set(meldingen)]) console.log(m);
  console.log(meldingen.length === 0 ? 'Alles consistent.' : `${new Set(meldingen).size} meldingen.`);
  await browser.close();
})();
