// Overgangen nalopen, met alleen verzonnen gegevens: welke vlakken zacht binnenkomen (vensters, menu's, panelen),
// of een paginawissel als overvloeier gaat (View Transitions, met de zijbalk die blijft staan), en schermafbeeldingen
// van de laadschets en de melding in licht en donker.
// Gebruik: scripts/ui/herstart.sh && node scripts/ui/overgangen.js <map> [voorvoegsel]
const { chromium } = require('playwright');
const crypto = require('crypto');
const fs = require('fs');
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
  const map = process.argv[2] || '/tmp/overgangen'; const voor = process.argv[3] || 'nu';
  fs.mkdirSync(map, { recursive: true });
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1400, height: 900 } });
  const page = await context.newPage();
  const fouten = []; page.on('pageerror', e => fouten.push(e.message));
  const basis = 'http://localhost:5090';

  await page.goto(basis + '/'); await page.waitForURL(/eerste-gebruiker/);
  await page.fill('#naam', 'Bea Beheer'); await page.fill('#initialen', 'BB'); await page.fill('#email', 'beheer@kantoor.test');
  await page.fill('#wachtwoord', 'Groene-Appel-Boom-42'); await page.fill('#herhaal', 'Groene-Appel-Boom-42');
  await page.click('button[type=submit]'); await page.waitForURL(/account\/beveiliging/);
  await page.fill('#code', totp(await page.textContent('code.fs-6'))); await page.click('button:has-text("Aanzetten")');
  await page.waitForSelector('text=Je herstelcodes');
  await page.goto(basis + '/'); await page.waitForTimeout(1500);
  await page.click('text=Nieuwe administratie'); await page.waitForSelector('#naam'); await page.waitForTimeout(800);
  await page.fill('#naam', 'Overgang Handel B.V.'); await page.click('text=Aanmaken');
  await page.waitForURL(/\/a\/[0-9a-f-]+$/); const id = page.url().split('/a/')[1];
  await page.goto(`${basis}/a/${id}/kolommenbalans`); await page.waitForTimeout(1500);

  // Welke vlakken komen zacht binnen: het echte Weergave-menu en het zoekvenster, en de vlakken die we nabouwen.
  const uitkomst = {};
  const menu = page.locator('details.weergavemenu summary').first();
  if (await menu.count()) { await menu.click(); await page.waitForTimeout(50); uitkomst['weergavemenu'] = await page.evaluate(() => getComputedStyle(document.querySelector('details.weergavemenu[open] .menu')).animationName); await menu.click(); }
  await page.keyboard.press('Control+k'); await page.waitForTimeout(50);
  uitkomst['zoekvenster'] = await page.evaluate(() => { const v = document.querySelector('.zoekvenster'); return v ? getComputedStyle(v).animationName : 'niet open'; });
  uitkomst['zoekvenster-achter'] = await page.evaluate(() => { const v = document.querySelector('.zoekvenster-achter'); return v ? getComputedStyle(v).animationName : 'niet open'; });
  await page.keyboard.press('Escape'); await page.waitForTimeout(300);
  Object.assign(uitkomst, await page.evaluate(() => {
    const plek = document.querySelector('.administratie-inhoud'); const r = {};
    for (const [naam, html] of [['venster-achtergrond', '<div class="venster-achtergrond"><div class="card venster"></div></div>'], ['venster', null],
      ['actiepaneel', '<div class="actiepaneel">x</div>'], ['verwerkpaneel', '<div class="bankwerk"><div class="card card-body verwerkpaneel">x</div></div>']]) {
      if (!html) continue;
      const d = document.createElement('div'); d.innerHTML = html; plek.appendChild(d);
      const el = d.firstElementChild; r[naam] = getComputedStyle(el).animationName;
      if (naam === 'venster-achtergrond') r['venster'] = getComputedStyle(el.querySelector('.venster')).animationName;
      if (naam === 'verwerkpaneel') r[naam] = getComputedStyle(el.querySelector('.verwerkpaneel')).animationName;
      d.remove();
    }
    return r;
  }));

  // Paginawissel: telt of de browser een overvloeier doet bij een klik in de zijbalk.
  await page.evaluate(() => { window.__overgangen = 0; if (document.startViewTransition) { const o = document.startViewTransition.bind(document); document.startViewTransition = (f) => { window.__overgangen++; const t0 = performance.now(); const v = o(f); v.finished.then(() => { window.__duur = Math.round(performance.now() - t0); }, () => { window.__duur = 'afgebroken'; }); return v; }; } });
  await page.click('.zijmenu a[href$="/journaal"], aside a[href$="/journaal"]'); await page.waitForTimeout(1200);
  uitkomst['paginawissel-overvloeier'] = await page.evaluate(() => window.__overgangen);
  uitkomst['paginawissel-duur-ms'] = await page.evaluate(() => window.__duur);
  // Nog een paar keer heen en weer, ook snel achter elkaar: geen afgebroken overgangen of fouten.
  for (const doel of ['/kolommenbalans', '/grootboek', '/journaal', '/bank', '/inkoop']) { await page.click(`aside a[href*="${doel}"] >> nth=0`); await page.waitForTimeout(120); }
  await page.waitForTimeout(1500);
  uitkomst['na-snel-klikken'] = page.url().replace(basis, '').split('/').pop();
  uitkomst['zijbalk-naam'] = await page.evaluate(() => { const z = document.querySelector('aside'); return z ? getComputedStyle(z).viewTransitionName : null; });
  uitkomst['url-na-klik'] = page.url().replace(basis, '');

  // Laadschets en melding, licht en donker.
  for (const thema of ['licht', 'donker']) {
    await page.evaluate((t) => { if (t === 'donker') document.documentElement.setAttribute('data-bs-theme', 'dark'); else document.documentElement.removeAttribute('data-bs-theme'); }, thema);
    await page.evaluate(() => {
      document.querySelectorAll('.proef').forEach(e => e.remove());
      const d = document.createElement('div'); d.className = 'proef card'; d.style.cssText = 'position:fixed;top:120px;left:320px;width:520px;z-index:2000';
      d.innerHTML = '<div class="laadschets"><span></span><span></span><span></span><span></span><span></span><span></span></div>';
      document.body.appendChild(d);
      const m = document.createElement('div'); m.className = 'proef melding-kort'; m.style.animation = 'none'; m.textContent = 'Boeking 2026-0012 opgeslagen';
      document.body.appendChild(m);
    });
    await page.waitForTimeout(500);
    await page.screenshot({ path: path.join(map, `${voor}-laadschets-melding-${thema}.png`) });
  }

  console.log(JSON.stringify(uitkomst, null, 1));
  if (fouten.length) console.log('Fouten in de console: ' + fouten.join(' | '));
  await browser.close();
})();
