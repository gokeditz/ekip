// Emoji paketini (emoji.zip) üretir. Depoya girmez: Twemoji ve Unicode verisi npm'den alınır.
//
// Kullanım (depo dışında geçici bir klasörde):
//   mkdir gecici && cd gecici && npm init -y
//   npm i @twemoji/svg emojibase-data cldr-annotations-full cldr-annotations-derived-full sharp adm-zip
//   node <depo>/Ekip.Uygulama/Varliklar/emoji/uret.mjs <depo>/Ekip.Uygulama/Varliklar/emoji/emoji.zip
//
// Zip içeriği:
//   veri.tsv      : her satır = emoji \t kategori \t tr adı \t tr anahtar kelimeler (|) \t en adı \t en anahtar kelimeler (|) \t kısa kodlar (|)
//   <anahtar>.png : 72x72 Twemoji (CC-BY 4.0, https://github.com/jdecked/twemoji). anahtar = kod noktaları (FE0F atılmış), "-" ile.
// Kategoriler: 0 İfadeler, 1 İnsanlar, 2 Hayvanlar ve doğa, 3 Yiyecek, 4 Seyahat, 5 Etkinlik, 6 Nesneler, 7 Semboller, 8 Bayraklar
import { createRequire } from 'node:module';
import fs from 'node:fs';
import path from 'node:path';
const require = createRequire(path.join(process.cwd(), 'x.js'));
const sharp = require('sharp');
const AdmZip = require('adm-zip');

const cikis = process.argv[2] ?? 'emoji.zip';
const nm = path.join(process.cwd(), 'node_modules');
const svgKlasoru = path.join(nm, '@twemoji', 'svg');
const veri = require('emojibase-data/en/data.json');
const kisaKod = require('emojibase-data/en/shortcodes/cldr.json');
const githubKod = require('emojibase-data/en/shortcodes/github.json');
const trAn = require('cldr-annotations-full/annotations/tr/annotations.json').annotations.annotations;
const trTuretilmis = require('cldr-annotations-derived-full/annotationsDerived/tr/annotations.json').annotationsDerived.annotations;
const enAn = require('cldr-annotations-full/annotations/en/annotations.json').annotations.annotations;

// Bayrak olarak alınacak ülkeler (arkadaş grubu için yaygın olanlar).
const ULKELER = ('TR AZ DE FR GB US IT ES NL BE AT CH GR RU UA PL RO BG SE NO DK FI PT IE CZ HU RS HR BA AL MK ME SK SI LT LV EE BY MD GE AM CY JP KR CN IN BR AR MX CA AU NZ SA AE EG IQ IR SY LB JO PS KW QA OM YE DZ MA TN LY PK AF UZ KZ KG TM TJ ID MY SG TH VN PH CO CL PE VE CU ZA NG KE ET IL EU UN').split(' ');
const ulkeBayragi = new Set(ULKELER.map(k => String.fromCodePoint(...[...k].map(c => 0x1F1E6 + c.charCodeAt(0) - 65))));

const kategoriHaritasi = { 0: 0, 1: 1, 3: 2, 4: 3, 5: 4, 6: 5, 7: 6, 8: 7, 9: 8 };
const anahtarOlustur = s => [...s].map(c => c.codePointAt(0)).filter(c => c !== 0xFE0F).map(c => c.toString(16)).join('-');
const temiz = s => s.replace(/[\t\r\n|]/g, ' ').trim();
const ara = (tablo, e) => tablo[e] ?? tablo[e.replaceAll('️', '')] ?? tablo[e.replace('‍', '‍️')];

const zip = new AdmZip();
const satirlar = [];
let sayi = 0, atlanan = 0;
for (const e of veri) {
  if (e.group === undefined || e.group === 2) continue; // bileşenler (ten rengi, saç) hariç
  const emoji = e.emoji;
  const harfler = [...emoji];
  if (e.group === 9) {
    const bolgesel = harfler.length === 2 && harfler.every(c => c.codePointAt(0) >= 0x1F1E6 && c.codePointAt(0) <= 0x1F1FF);
    if (bolgesel && !ulkeBayragi.has(emoji)) continue;
    // İskoçya, Galler gibi alt bölge bayrakları (siyah bayrak + etiket harfleri) hariç.
    if (harfler.some(c => c.codePointAt(0) >= 0xE0020 && c.codePointAt(0) <= 0xE007F)) continue;
  }
  const anahtar = anahtarOlustur(emoji);
  const aday = [anahtar, harfler.map(c => c.codePointAt(0).toString(16)).join('-')];
  const dosya = aday.map(a => path.join(svgKlasoru, a + '.svg')).find(fs.existsSync);
  if (!dosya) { atlanan++; continue; }

  const png = await sharp(fs.readFileSync(dosya), { density: 288 })
    .resize(72, 72, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
    .png({ palette: true, quality: 92, colours: 192, effort: 10, compressionLevel: 9 })
    .toBuffer();
  zip.addFile(anahtar + '.png', png);

  const tr = ara(trAn, emoji) ?? ara(trTuretilmis, emoji);
  const en = ara(enAn, emoji);
  const trAd = tr?.tts?.[0] ?? e.label;
  const trKelime = (tr?.default ?? []).filter(k => k !== trAd);
  const enAd = en?.tts?.[0] ?? e.label;
  const enKelime = [...new Set([...(en?.default ?? []), ...(e.tags ?? [])])].filter(k => k !== enAd);
  const kodlar = [...new Set([...[kisaKod[e.hexcode] ?? []].flat(), ...[githubKod[e.hexcode] ?? []].flat()])];
  satirlar.push([emoji, kategoriHaritasi[e.group], temiz(trAd), trKelime.map(temiz).join('|'), temiz(enAd), enKelime.map(temiz).join('|'), kodlar.join('|')].join('\t'));
  sayi++;
}
zip.addFile('veri.tsv', Buffer.from(satirlar.join('\n'), 'utf8'));
zip.writeZip(cikis);
console.log(`${sayi} emoji, ${atlanan} atlandı (Twemoji'de yok), zip ${(fs.statSync(cikis).size / 1048576).toFixed(2)} MB`);
process.exit(0);
