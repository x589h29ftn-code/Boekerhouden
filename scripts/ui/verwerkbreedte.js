// Het verwerkscherm (inkoop) op verschillende schermbreedtes, met vier verzonnen facturen (werkvoorraad links) en een
// onthouden breedte van de factuur die niet past (BREEDTE=1400): de pagina mag nooit breder zijn dan het venster, het
// formulier houdt 520 pixels. Gebruik: scripts/ui/herstart.sh && BREEDTE=1400 node scripts/ui/verwerkbreedte.js <map> [voorvoegsel]
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

async function maakFactuur(browser, uit, n) {
  const p = await browser.newPage();
  await p.setContent(`<html><body style="font-family:sans-serif;padding:40px">
  <div style="display:flex;justify-content:space-between"><div style="font-size:28px">denkwerk</div>
  <div>Denkwerk Coaching B.V.<br>Molenweg 1<br>8912 AA Leeuwarden<br>info@denkwerk-voorbeeld.nl<br>KVK: 12345678<br>Btw: NL123456782B01<br>Bank: NL20 INGB 0001 2345 67</div></div>
  <div style="margin:60px 0">Voorbeeld Handel B.V.<br>Kerkpad 7<br>1234 AB Ergens</div>
  <h1>Factuur 2026-04${n}</h1><div>Factuurdatum: 20-11-2026 &nbsp; Vervaldatum: 04-12-2026</div>
  <table style="width:100%;margin-top:30px"><tr><th align=left>Omschrijving</th><th align=right>Bedrag</th><th>Btw</th></tr>
  <tr><td>1 x Coachingstraject 90 dagen</td><td align=right>€ 1.000,00</td><td>21%</td></tr>
  <tr><td></td><td align=right>Subtotaal € 1.000,00</td></tr><tr><td></td><td align=right>21% btw € 210,00</td></tr><tr><td></td><td align=right>Totaal € 1.210,00</td></tr></table>
  </body></html>`);
  await p.pdf({ path: uit, format: 'A4' }); await p.close();
}

(async () => {
  const map = process.argv[2] || '/tmp/verwerkbreedte'; const voor = process.argv[3] || 'nu';
  fs.mkdirSync(map, { recursive: true });
  const browser = await chromium.launch();
  const pdf = path.join(map, 'f1.pdf');
  for (let n = 1; n <= 4; n++) await maakFactuur(browser, path.join(map, `f${n}.pdf`), n);
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
  await page.fill('#naam', 'Breedte Handel B.V.'); await page.selectOption('#schema', 'Sifer'); await page.click('text=Aanmaken');
  await page.waitForURL(/\/a\/[0-9a-f-]+\/instellingen$/); const id = page.url().split('/a/')[1].split('/')[0];

  // Vier facturen: dan staat de werkvoorraad links.
  await page.goto(`${basis}/a/${id}/inkoop`); await page.waitForTimeout(1500);
  for (let i = 1; i <= 4; i++) { await page.setInputFiles('#bestanden', pdf.replace('f1', 'f' + i)); await page.waitForTimeout(2500); }
  if (process.env.BREEDTE) await page.evaluate((b) => localStorage.setItem('boekerhouden.inkoop.viewerbreedte', b), process.env.BREEDTE);
  await page.goto(`${basis}/a/${id}/inkoop`); await page.waitForTimeout(1500);
  await page.locator('a[href*="/inkoop/"]').first().click(); await page.waitForTimeout(3500);
  for (const [b, h] of [[1920, 1040], [1600, 900], [2560, 1400]]) {
    await page.setViewportSize({ width: b, height: h }); await page.waitForTimeout(1500);
    const m = await page.evaluate(() => {
      const r = s => { const e = document.querySelector(s); if (!e) return null; const x = e.getBoundingClientRect(); return `${Math.round(x.left)}-${Math.round(x.right)}`; };
      return { venster: innerWidth, scroll: document.documentElement.scrollWidth, inhoud: r('.administratie-inhoud'), werk: r('.werkvoorraad'), formulier: r('.verwerken-formulier'), viewer: r('.factuurviewer'), canvas: r('.factuurviewer canvas') };
    });
    console.log(b, JSON.stringify(m)); if (m.scroll > m.venster) console.log(`FOUT: de pagina is ${m.scroll} breed bij een venster van ${m.venster}`);
    await page.screenshot({ path: `${map}/${voor}-${b}.png` });
  }
  if (fouten.length) console.log('Fouten in de console: ' + fouten.join(' | '));
  await browser.close();
})();
