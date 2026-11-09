/* Render promo/index.html jadi PNG frame-per-frame (tanpa drop frame), lalu MP4 via ffmpeg.
   Pakai browser yang sudah terpasang (Edge/Chrome) lewat puppeteer-core.

   node render.mjs                 -> render penuh 60 fps -> ../out/android-mirror-promo.mp4
   node render.mjs --snap 1 4 8.5  -> hanya potret detik tertentu -> ../out/snap/*.png (untuk cek)
   ENV: FPS=30 (default 60), URL=... (default ../index.html)
*/
import puppeteer from 'puppeteer-core';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { mkdir, rm, access } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');
const out = path.join(root, 'out');
const URL = process.env.URL || pathToFileURL(path.join(root, 'index.html')).href + '?clean=1';
const FPS = Number(process.env.FPS || 60);

const candidates = [
  process.env.BROWSER,
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
  `${process.env.LOCALAPPDATA}/Google/Chrome/Application/chrome.exe`,
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
].filter(Boolean);
let exe = null;
for (const c of candidates) { try { await access(c); exe = c; break; } catch {} }
if (!exe) { console.error('Chrome/Edge tidak ditemukan. Set BROWSER=path\\ke\\chrome.exe'); process.exit(1); }

const args = process.argv.slice(2);
const snapMode = args[0] === '--snap';

const browser = await puppeteer.launch({ executablePath: exe, headless: 'new',
  args: ['--enable-gpu', '--use-gl=angle', '--ignore-gpu-blocklist', '--allow-file-access-from-files'] });
const page = await browser.newPage();
page.on('pageerror', e => console.log('PAGEERROR', e.message));
await page.setViewport({ width: 1080, height: 1920, deviceScaleFactor: 1 });
await page.goto(URL, { waitUntil: 'networkidle0', timeout: 120000 });
await page.waitForFunction('window.OPENER && window.OPENER.ready', { timeout: 120000 });

const shoot = async (t, file) => {
  await page.evaluate(async (time) => {
    window.OPENER.seek(time);
    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
    window.OPENER.seek(time);
  }, t);
  await page.screenshot({ path: file, clip: { x: 0, y: 0, width: 1080, height: 1920 }, optimizeForSpeed: !snapMode });
};

if (snapMode) {
  const dir = path.join(out, 'snap');
  await mkdir(dir, { recursive: true });
  for (const s of args.slice(1)) {
    const t = Number(s);
    await shoot(t, path.join(dir, `t${t.toFixed(2).replace('.', '_')}.png`));
    process.stdout.write(`${t} `);
  }
  console.log(`\nselesai -> ${dir}`);
  await browser.close();
  process.exit(0);
}

const DURATION = await page.evaluate(() => window.OPENER.DURATION);
const total = Math.ceil(DURATION * FPS);
const frames = path.join(out, 'frames');
await rm(frames, { recursive: true, force: true });
await mkdir(frames, { recursive: true });
console.log(`Render ${total} frame @ ${FPS} fps (${DURATION} dtk) memakai ${path.basename(exe)}`);
const t0 = Date.now();
for (let i = 0; i < total; i++) {
  await shoot(i / FPS, path.join(frames, `f${String(i).padStart(5, '0')}.png`));
  if (i % FPS === 0) {
    const el = (Date.now() - t0) / 1000, eta = el / (i + 1) * (total - i - 1);
    process.stdout.write(`\r${i}/${total} frame  ·  sisa ±${Math.round(eta)} dtk   `);
  }
}
await browser.close();

const mp4 = path.join(out, 'android-mirror-promo.mp4');
console.log('\nMenggabungkan jadi MP4...');
const ff = spawnSync('ffmpeg', ['-y', '-framerate', String(FPS), '-i', path.join(frames, 'f%05d.png'),
  '-c:v', 'libx264', '-preset', 'slow', '-crf', '16', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', mp4],
  { stdio: 'inherit' });
if (ff.status !== 0) { console.error('ffmpeg gagal / tidak ada. Instal: winget install Gyan.FFmpeg'); process.exit(1); }
await rm(frames, { recursive: true, force: true });
console.log(`\nSELESAI -> ${mp4}`);
