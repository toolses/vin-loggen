import { writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));

const svgIcon = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
  <!-- Background -->
  <rect width="512" height="512" fill="#1a0808"/>

  <!-- Wine glass bowl (outline/body in cream) -->
  <path d="
    M 168 72
    L 344 72
    C 344 72 380 180 380 224
    C 380 284 326 330 256 330
    C 186 330 132 284 132 224
    C 132 180 168 72 168 72
    Z
  " fill="#f5e6d3"/>

  <!-- Wine filling the lower part of the bowl -->
  <path d="
    M 154 248
    C 154 248 148 265 148 278
    C 148 310 198 330 256 330
    C 314 330 364 310 364 278
    C 364 265 358 248 358 248
    Z
  " fill="#7d1a35"/>

  <!-- Stem -->
  <rect x="243" y="330" width="26" height="104" rx="4" fill="#f5e6d3"/>

  <!-- Base -->
  <rect x="170" y="430" width="172" height="20" rx="10" fill="#f5e6d3"/>
</svg>`;

// Write the SVG itself too
writeFileSync(join(__dirname, '../public/icons/icon.svg'), svgIcon, 'utf8');
console.log('✓ icon.svg written');

// Use sharp to generate PNGs from the SVG
let sharp;
try {
  const require = createRequire(import.meta.url);
  sharp = require('sharp');
} catch {
  console.error('sharp not found – run: npm install --save-dev sharp');
  process.exit(1);
}

const svgBuffer = Buffer.from(svgIcon);

for (const size of [192, 512]) {
  const outPath = join(__dirname, `../public/icons/icon-${size}x${size}.png`);
  await sharp(svgBuffer)
    .resize(size, size)
    .png()
    .toFile(outPath);
  console.log(`✓ icon-${size}x${size}.png written`);
}

console.log('Done! Icons generated.');
