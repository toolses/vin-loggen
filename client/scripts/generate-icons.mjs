import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const __dirname = dirname(fileURLToPath(import.meta.url));

const svgPath = join(__dirname, '../public/icons/icon.svg');
const svgBuffer = readFileSync(svgPath);

let sharp;
try {
  const require = createRequire(import.meta.url);
  sharp = require('sharp');
} catch {
  console.error('sharp not found – run: npm install --save-dev sharp');
  process.exit(1);
}

for (const size of [192, 512]) {
  const outPath = join(__dirname, `../public/icons/icon-${size}x${size}.png`);
  await sharp(svgBuffer)
    .resize(size, size)
    .png()
    .toFile(outPath);
  console.log(`✓ icon-${size}x${size}.png written`);
}

console.log('Done! Icons generated from icon.svg');
