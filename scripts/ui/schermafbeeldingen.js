// Browsercontrole en schermafbeeldingen van het administratiepakket, met alleen verzonnen testdata uit tests/Fictief.
// Gebruik: scripts/ui/herstart.sh && node scripts/ui/schermafbeeldingen.js <map> [voorvoegsel]
// Maakt de eerste gebruiker (met tweestapsverificatie), een administratie, boekt een inkoopfactuur (UBL) en een
// memoriaalboeking, leest een bankafschrift in en bewaart per scherm een PNG: <map>/<voorvoegsel>-<scherm>.png.
const { chromium } = require('playwright');
const crypto = require('crypto');
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
  const uit = process.argv[2] || '.'; const pre = process.argv[3] || 'scherm';
  const fictief = path.resolve(__dirname, '../../tests/Fictief/administratie');
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
  const log = []; page.on('pageerror', e => log.push(e.message)); page.on('console', m => { if (m.type() === 'error') log.push(m.text()); else if (process.env.DEBUG) console.log('console:', m.text()); });
  const basis = 'http://localhost:5090';

  // Eerste gebruiker met tweestapsverificatie, dan een administratie.
  await page.goto(basis + '/'); await page.waitForURL(/eerste-gebruiker/);
  await page.fill('#naam', 'Bea Beheer'); await page.fill('#initialen', 'BB'); await page.fill('#email', 'beheer@kantoor.test');
  await page.fill('#wachtwoord', 'Groene-Appel-Boom-42'); await page.fill('#herhaal', 'Groene-Appel-Boom-42');
  await page.click('button[type=submit]'); await page.waitForURL(/account\/beveiliging/);
  await page.fill('#code', totp(await page.textContent('code.fs-6'))); await page.click('button:has-text("Aanzetten")');
  await page.waitForSelector('text=Je herstelcodes');
  await page.goto(basis + '/'); await page.waitForTimeout(1500);
  await page.click('text=Nieuwe administratie'); await page.waitForSelector('#naam'); await page.waitForTimeout(800);
  await page.fill('#naam', 'Voorbeeld Handel B.V.'); await page.click('text=Aanmaken');
  await page.waitForURL(/\/a\/[0-9a-f-]+$/); const id = page.url().split('/a/')[1];

  // Inkoopfactuur (UBL) inlezen en boeken.
  await page.goto(`${basis}/a/${id}/inkoop`); await page.waitForTimeout(1200);
  await page.setInputFiles('#bestanden', path.join(fictief, 'belnet-2026-0411.xml')); await page.waitForTimeout(3000);
  await page.goto(`${basis}/a/${id}/inkoop`); await page.waitForTimeout(1200);
  if (await page.locator('table tbody a').count() > 0) {
    await page.locator('table tbody a').first().click(); await page.waitForSelector('#nummer'); await page.waitForTimeout(1500);
    const rek = page.locator('input[aria-label=Rekening]').first();
    if (!(await rek.inputValue())) { await rek.click(); await rek.fill('4310'); await page.keyboard.press('Tab'); }
    await page.click('button.btn-primary:has-text("oeken")'); await page.waitForTimeout(2000);
  }

  // Memoriaalboeking.
  await page.goto(`${basis}/a/${id}/memoriaal`); await page.waitForTimeout(1500);
  await page.fill('input[aria-label=Datum]', '15-03-2026');
  await page.fill('input[placeholder=Omschrijving]', 'Afschrijving inventaris maart');
  const reks = page.locator('input[aria-label=Rekening]');
  await reks.nth(0).click(); await reks.nth(0).fill('4300'); await page.keyboard.press('Tab');
  await page.locator('input[aria-label=Debet]').nth(0).fill('250');
  await reks.nth(1).click(); await reks.nth(1).fill('1100'); await page.keyboard.press('Tab');
  await page.locator('input[aria-label=Credit]').nth(1).fill('250');
  await page.click('button.btn-primary:has-text("Boeken")'); await page.waitForTimeout(1500);
  await page.screenshot({ path: `${uit}/${pre}-memoriaal.png` });

  // Verkoop: eigen adres en btw-nummer, een nieuwe klant en een factuur die definitief wordt.
  await page.goto(`${basis}/a/${id}/instellingen`); await page.waitForSelector('#straat'); await page.waitForTimeout(800);
  await page.fill('#btw', 'NL123456782B01'); await page.fill('#ibans', 'NL20INGB0001234567'); await page.fill('#straat', 'Dorpsstraat 1'); await page.fill('#postcode', '1234 AB');
  await page.fill('#plaats', 'Ergens'); await page.click('button:has-text("Opslaan")'); await page.waitForTimeout(1000);
  await page.goto(`${basis}/a/${id}/verkoop/nieuw`); await page.waitForSelector('#klant'); await page.waitForTimeout(1200);
  await page.click('button:has-text("Nieuwe klant")'); await page.fill('#kn', 'J. de Vries Installatie'); await page.fill('#ks', 'Laan 2');
  await page.fill('#kp', '5678 CD'); await page.fill('#kpl', 'Elders'); await page.click('.nieuwe-klant button:has-text("Toevoegen")'); await page.waitForTimeout(1000);
  await page.fill('#omschrijving', 'Werkzaamheden maart 2026');
  await page.locator('input[aria-label=Omschrijving]').first().fill('Advies en ontwerp');
  await page.locator('input[aria-label=Aantal]').first().fill('12');
  await page.locator('input[aria-label=Eenheid]').first().fill('uur');
  await page.locator('input[aria-label="Prijs per eenheid exclusief btw"]').first().fill('85');
  await page.click('button:has-text("Nog een regel")');
  await page.locator('input[aria-label=Omschrijving]').nth(1).fill('Materiaal');
  await page.locator('input[aria-label=Aantal]').nth(1).fill('1');
  await page.locator('input[aria-label="Prijs per eenheid exclusief btw"]').nth(1).fill('240');
  await page.waitForTimeout(500);
  await page.screenshot({ path: `${uit}/${pre}-verkoopfactuur-concept.png` });
  await page.click('button:has-text("Definitief maken")'); await page.waitForTimeout(2500);
  await page.screenshot({ path: `${uit}/${pre}-verkoopfactuur-definitief.png` });

  // Afdrukken opent het afdrukvenster in een verborgen frame; hier alleen kijken of het frame de factuur bevat.
  await page.evaluate(() => { window.print = () => {}; });
  await page.click('button[title=Afdrukken]'); await page.waitForTimeout(1200);
  const afdruk = await page.evaluate(() => [...document.querySelectorAll('iframe')].map(f => f.contentDocument?.body?.innerText || '').join(' '));
  if (!afdruk.includes('Factuurnummer')) log.push('afdrukken: de factuur staat niet in het frame');

  // Terugkerend: van de factuur een maandelijkse maken.
  await page.click('a:has-text("Herhalen")'); await page.waitForSelector('#naam'); await page.waitForTimeout(800);
  await page.fill('#naam', 'Onderhoud installatie'); await page.fill('#omschrijving', 'Onderhoud #periode');
  await page.click('fieldset button:has-text("Opslaan")'); await page.waitForTimeout(1200);
  await page.screenshot({ path: `${uit}/${pre}-terugkerend.png` });

  // De factuurontwerper: een blok kiezen en verslepen.
  await page.goto(`${basis}/a/${id}/factuurlayout`); await page.waitForSelector('.ontwerpvlak .fp'); await page.waitForTimeout(1200);
  await page.screenshot({ path: `${uit}/${pre}-factuurlayout.png` });
  const klant = page.locator('.ontwerpvlak [data-blok=titel]'); const vak = await klant.boundingBox();
  await page.mouse.move(vak.x + 10, vak.y + 5); await page.mouse.down(); await page.mouse.move(vak.x + 90, vak.y + 25, { steps: 8 }); await page.mouse.up();
  await page.waitForTimeout(800);
  const titel = await page.evaluate(() => document.querySelector('.ontwerpvlak [data-blok=titel]').getAttribute('style'));
  if (titel.includes('top:92mm;left:20mm')) log.push('ontwerper: titel niet versleept (' + titel + ')');
  await page.screenshot({ path: `${uit}/${pre}-factuurlayout-gesleept.png` });

  // Een verkoopfactuur uit een ander pakket inlezen (klant onbekend: concept met de reden).
  await page.goto(`${basis}/a/${id}/verkoop`); await page.waitForTimeout(1500);
  await page.setInputFiles('#verkoopbestanden', path.join(fictief, 'verkoop-ander-pakket.xml')); await page.waitForTimeout(2500);
  await page.screenshot({ path: `${uit}/${pre}-verkoop-ingelezen.png` });
  await page.locator('tr:has-text("ingelezen")').first().click(); await page.waitForSelector('#nummer'); await page.waitForTimeout(1200);
  await page.screenshot({ path: `${uit}/${pre}-verkoopfactuur-ingelezen.png` });

  // Bankafschrift inlezen (slepen kan ook; het bestandsveld bedekt dan de pagina).
  await page.goto(`${basis}/a/${id}/bank`); await page.waitForTimeout(1500);
  await page.setInputFiles('#afschriften', path.join(fictief, 'ing-2026-03.mt940')); await page.waitForTimeout(3000);

  const schermen = [['bank', 'bank'], ['grootboek', 'grootboek?rekening=1600&van=2026-01-01&tot=2026-12-31'], ['kolommenbalans', 'kolommenbalans'],
    ['inkoop', 'inkoop'], ['journaal', 'journaal'], ['aansluitingen', 'aansluitingen'], ['verkoop', 'verkoop'], ['verkoopboek', 'verkoopboek'],
    ['debiteuren', 'debiteuren']];
  for (const [naam, pad] of schermen) {
    await page.goto(`${basis}/a/${id}/${pad}`); await page.waitForTimeout(1800);
    if (naam === 'bank' && await page.locator('table.bankmutaties tbody tr').count() > 0) {
      await page.locator('table.bankmutaties tbody tr').first().click(); await page.waitForTimeout(1200);
    }
    await page.screenshot({ path: `${uit}/${pre}-${naam}.png` });
  }

  console.log('administratie', id, log.length ? 'fouten in de browser: ' + log.join(' | ') : 'geen fouten in de browser');
  await browser.close();
})();
