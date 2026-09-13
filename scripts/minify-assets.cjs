// Minify release assets without changing URLs, bundling, or modifying source files.
// Usage: npm run build:assets -- --outdir <publish-directory>/wwwroot
const fs = require('node:fs/promises');
const path = require('node:path');
const { transform } = require('esbuild');
const root = path.resolve(__dirname, '..');
const source = path.join(root, 'wwwroot');
const args = process.argv.slice(2);
if (args.length && (args.length !== 2 || args[0] !== '--outdir')) {
  throw new Error('Usage: node scripts/minify-assets.cjs [--outdir <directory>]');
}
const output = path.resolve(args[1] || path.join(root, 'obj', 'minified-assets'));
if (output === source || output.startsWith(source + path.sep)) {
  throw new Error('Release assets must be written outside the source wwwroot.');
}
async function files(directory) {
  const entries = await fs.readdir(directory, { withFileTypes: true });
  const groups = await Promise.all(entries.map(entry => {
    const file = path.join(directory, entry.name);
    return entry.isDirectory() ? files(file) : entry.isFile() && /\.(js|css)$/.test(entry.name) ? [file] : [];
  }));
  return groups.flat().sort();
}
async function main() {
  let before = 0, after = 0;
  const inputs = await files(source);
  for (const file of inputs) {
    const relative = path.relative(source, file);
    const input = await fs.readFile(file, 'utf8');
    const result = await transform(input, {
      loader: path.extname(file).slice(1), sourcefile: relative,
      minify: true, legalComments: 'inline', charset: 'utf8',
      // Keep existing browser support and classic-script global bindings.
      target: ['es2020'],
    });
    if (result.warnings.length) throw new Error(JSON.stringify(result.warnings));
    const destination = path.join(output, relative);
    await fs.mkdir(path.dirname(destination), { recursive: true });
    await fs.writeFile(destination, result.code);
    before += Buffer.byteLength(input); after += Buffer.byteLength(result.code);
  }
  console.log(`Minified ${inputs.length} assets: ${before} -> ${after} bytes (${Math.round((1 - after / before) * 100)}% smaller).`);
}
main().catch(error => { console.error(error); process.exitCode = 1; });
