import { readFileSync, writeFileSync } from 'node:fs';

const [source, output] = process.argv.slice(2);
if (!source || !output) throw new Error('Uso: node scripts/sbom-scope.mjs deps.json scope.json');
const deps = JSON.parse(readFileSync(source, 'utf8'));
if (!deps.libraries || !deps.targets) throw new Error('.deps.json sem libraries/targets.');
const packages = [...new Set(Object.entries(deps.libraries)
  .filter(([, library]) => ['package', 'project'].includes(library.type))
  .map(([key]) => key.slice(0, key.lastIndexOf('/'))))].sort();
if (!packages.length || packages.some(name => !name)) throw new Error('Escopo vazio ou inválido.');
const scope = { packages };
writeFileSync(output, JSON.stringify({ sbom_scope: scope }, null, 2));
console.log(`Escopo SBOM: ${packages.length} dependência(s)/projeto(s) de libraries no .deps.json.`);