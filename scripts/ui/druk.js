// De schermen onder druk, met alleen verzonnen gegevens: dubbel- en driedubbelklikken op Definitief maken en Boeken
// (ook een extra afschrijving op een vast actief),
// dezelfde factuur in twee tabbladen tegelijk definitief maken, de terugknop na boeken, en dat alles ook met een
// trage verbinding (400 ms vertraging). Meldt wat er misgaat (twee nummers, twee boekingen, fouten in de console).
// Gebruik: scripts/ui/herstart.sh && node scripts/ui/druk.js
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
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1600, height: 1000 } });
  const page = await context.newPage();
  const fouten = []; const meld = (t) => { fouten.push(t); console.log('FOUT: ' + t); };
  const consolefouten = [];
  context.on('page', p => p.on('pageerror', e => consolefouten.push(e.message)));
  page.on('pageerror', e => consolefouten.push(e.message));
  const basis = 'http://localhost:5090';

  // Eerste gebruiker met tweestapsverificatie, een administratie met eigen gegevens.
  await page.goto(basis + '/'); await page.waitForURL(/eerste-gebruiker/);
  await page.fill('#naam', 'Bea Beheer'); await page.fill('#initialen', 'BB'); await page.fill('#email', 'beheer@kantoor.test');
  await page.fill('#wachtwoord', 'Groene-Appel-Boom-42'); await page.fill('#herhaal', 'Groene-Appel-Boom-42');
  await page.click('button[type=submit]'); await page.waitForURL(/account\/beveiliging/);
  await page.fill('#code', totp(await page.textContent('code.fs-6'))); await page.click('button:has-text("Aanzetten")');
  await page.waitForSelector('text=Je herstelcodes');
  await page.goto(basis + '/'); await page.waitForTimeout(1500);
  await page.click('text=Nieuwe administratie'); await page.waitForSelector('#naam'); await page.waitForTimeout(800);
  await page.fill('#naam', 'Druk Handel B.V.'); await page.selectOption('#schema', 'Sifer'); await page.click('text=Aanmaken');
  await page.waitForURL(/\/a\/[0-9a-f-]+\/instellingen$/); const id = page.url().split('/a/')[1].split('/')[0];
  await page.goto(`${basis}/a/${id}/instellingen`); await page.waitForSelector('#straat'); await page.waitForTimeout(800);
  await page.fill('#ibans', 'NL20INGB0001234567'); await page.fill('#straat', 'Dorpsstraat 1'); await page.fill('#postcode', '1234 AB');
  await page.fill('#plaats', 'Ergens'); await page.click('button:has-text("Opslaan")'); await page.waitForTimeout(1000);
  await page.goto(`${basis}/a/${id}/instellingen/btw`); await page.waitForSelector('#btw'); await page.waitForTimeout(800);
  await page.fill('#btw', 'NL123456782B01'); await page.click('button:has-text("Opslaan")'); await page.waitForTimeout(1000);

  async function concept(p, eersteKeer) {
    await p.goto(`${basis}/a/${id}/verkoop/nieuw`); await p.waitForSelector('#klant'); await p.waitForTimeout(1200);
    if (eersteKeer) {
      await p.click('button:has-text("Nieuwe klant")'); await p.fill('#kn', 'J. de Vries Installatie'); await p.fill('#ks', 'Laan 2');
      await p.fill('#kp', '5678 CD'); await p.fill('#kpl', 'Elders'); await p.click('.nieuwe-klant button:has-text("Toevoegen")'); await p.waitForTimeout(1000);
    } else {
      await p.selectOption('#klant', { index: 1 }).catch(() => {});
      await p.waitForTimeout(500);
    }
    await p.fill('#omschrijving', 'Werkzaamheden');
    await p.locator('input[aria-label=Omschrijving]').first().fill('Advies');
    await p.locator('input[aria-label=Aantal]').first().fill('2');
    await p.locator('input[aria-label="Prijs per eenheid exclusief btw"]').first().fill('85');
    await p.waitForTimeout(600);
  }

  // Het aantal uitgegeven factuurnummers in de verkooplijst (alle jaren).
  async function definitief() {
    await page.goto(`${basis}/a/${id}/facturen`); await page.waitForTimeout(1500);
    const tekst = await page.locator('main').innerText();
    if (process.env.DEBUG) console.log(tekst.slice(0, 1500));
    return new Set(tekst.match(/\b20\d{6}\b/g) || []).size;
  }

  // 1. Dubbel- en driedubbelklikken op Definitief maken.
  for (const [wat, klikken] of [['dubbelklik', 2], ['driedubbelklik', 3]]) {
    const voor = await definitief();
    await concept(page, voor === 0);
    await page.click('button:has-text("Definitief maken")', { clickCount: klikken, delay: 20 }); await page.waitForTimeout(2500);
    const na = await definitief();
    console.log(`${wat} op Definitief maken: ${na - voor} nieuwe definitieve factuur`);
    if (na - voor !== 1) meld(`${wat} op Definitief maken gaf ${na - voor} definitieve facturen`);
  }

  // 2. Dubbelklikken op Boeken in het memoriaal.
  async function memoriaalBoekingen() {
    await page.goto(`${basis}/a/${id}/memoriaal`); await page.waitForTimeout(1500);
    return await page.locator('.memoriaal-lijst tbody tr, table.boekingen tbody tr').count();
  }

  const memVoor = await memoriaalBoekingen();
  await page.fill('input[aria-label=Datum]', '15-03-2026');
  await page.fill('input[placeholder=Omschrijving]', 'Dubbelklik test');
  const reks = page.locator('input[aria-label=Rekening]');
  await reks.nth(0).click(); await reks.nth(0).fill('4300'); await page.keyboard.press('Tab');
  await page.locator('input[aria-label=Debet]').nth(0).fill('25');
  await reks.nth(1).click(); await reks.nth(1).fill('4900'); await page.keyboard.press('Tab');
  await page.locator('input[aria-label=Credit]').nth(1).fill('25');
  await page.click('button.btn-primary:has-text("Boeken")', { clickCount: 2, delay: 20 }); await page.waitForTimeout(2000);
  const journaal = await page.evaluate(async (u) => (await fetch(u)).ok, `${basis}/a/${id}/journaal`).catch(() => false);
  await page.goto(`${basis}/a/${id}/journaal`); await page.waitForTimeout(1800);
  const dubbel = await page.locator('tbody tr', { hasText: 'Dubbelklik test' }).count();
  console.log(`dubbelklik op Boeken (memoriaal): ${dubbel} boeking(en) in het journaal${journaal ? '' : ' (journaal niet gevonden)'}; memoriaallijst eerst ${memVoor}`);
  if (dubbel > 1) meld(`dubbelklik op Boeken gaf ${dubbel} boekingen`);

  // 3. Dezelfde factuur in twee tabbladen tegelijk definitief maken.
  await concept(page, false);
  await page.click('button:has-text("Bewaren")').catch(() => {}); await page.waitForTimeout(1500);
  const adres = page.url();
  const tweede = await context.newPage();
  await tweede.goto(adres); await tweede.waitForTimeout(1500);
  const voorTwee = await definitief();
  await page.goto(adres); await page.waitForTimeout(1500);
  await Promise.all([page.click('button:has-text("Definitief maken")'), tweede.click('button:has-text("Definitief maken")')]);
  await page.waitForTimeout(2500);
  const naTwee = await definitief();
  console.log(`twee tabbladen tegelijk: ${naTwee - voorTwee} nieuwe definitieve factuur (adres ${adres.replace(basis, '')})`);
  if (naTwee - voorTwee > 1) meld(`twee tabbladen gaven ${naTwee - voorTwee} definitieve facturen`);
  await tweede.close();

  // 4. De terugknop na Definitief maken: niet nog eens.
  const voorTerug = await definitief();
  await concept(page, false);
  await page.click('button:has-text("Definitief maken")'); await page.waitForTimeout(2500);
  await page.goBack(); await page.waitForTimeout(1500);
  if (await page.locator('button:has-text("Definitief maken")').count() > 0) {
    await page.click('button:has-text("Definitief maken")').catch(() => {}); await page.waitForTimeout(2000);
  }
  const naTerug = await definitief();
  console.log(`terugknop en nog eens: ${naTerug - voorTerug} nieuwe definitieve factuur`);
  if (naTerug - voorTerug !== 1) meld(`terugknop gaf ${naTerug - voorTerug} definitieve facturen`);

  // 5. Trage verbinding: dubbelklik met 400 ms vertraging.
  // Vast actief: een inkoopfactuur op de inventaris wordt een actief; dubbelklik op Boeken bij een extra afschrijving.
  await page.goto(`${basis}/a/${id}/inkoop`); await page.waitForTimeout(1200);
  await page.setInputFiles('#bestanden', path.resolve(__dirname, '../../tests/Fictief/administratie/belnet-2026-0411.xml')); await page.waitForTimeout(3000);
  await page.goto(`${basis}/a/${id}/inkoop`); await page.waitForTimeout(1200);
  await page.locator('table tbody a').first().click(); await page.waitForSelector('#nummer'); await page.waitForTimeout(1500);
  const inventaris = page.locator('input[aria-label=Rekening]').first();
  await inventaris.click(); await inventaris.fill('0200'); await page.keyboard.press('Tab'); await page.waitForTimeout(500);
  await page.click('button.btn-primary:has-text("oeken")'); await page.waitForTimeout(2000);
  await page.goto(`${basis}/a/${id}/activa`); await page.waitForTimeout(1500);
  await page.locator('table tbody tr').first().click(); await page.waitForTimeout(2500);
  await page.click('.wijzigmenu > button'); await page.click('.wijzigmenu .menu button:has-text("Extra afschrijving")'); await page.waitForTimeout(500);
  await page.fill('#extra-bedrag', '1'); await page.fill('#extra-datum', '15-06-2026');
  await page.click('.actiepaneel button.btn-primary:has-text("Boeken")', { clickCount: 2, delay: 20 }); await page.waitForTimeout(2500);
  const extra = await page.locator('table.extraafschrijvingen tbody tr').count();
  console.log(`dubbelklik op Boeken (extra afschrijving): ${extra} extra afschrijving(en)`);
  if (extra !== 1) meld(`dubbelklik op Boeken bij een extra afschrijving gaf ${extra} boekingen`);

  const cdp = await context.newCDPSession(page);
  await cdp.send('Network.enable');
  await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: 400, downloadThroughput: 500 * 1024, uploadThroughput: 250 * 1024 });
  const voorTraag = await definitief();
  await concept(page, false);
  await page.click('button:has-text("Definitief maken")', { clickCount: 2, delay: 50 }); await page.waitForTimeout(6000);
  const naTraag = await definitief();
  console.log(`trage verbinding, dubbelklik: ${naTraag - voorTraag} nieuwe definitieve factuur`);
  if (naTraag - voorTraag !== 1) meld(`trage verbinding gaf ${naTraag - voorTraag} definitieve facturen`);

  for (const c of [...new Set(consolefouten)]) meld('console: ' + c);
  console.log(fouten.length === 0 ? 'Alles goed.' : `${fouten.length} fouten.`);
  await browser.close();
  process.exit(fouten.length === 0 ? 0 : 1);
})();
