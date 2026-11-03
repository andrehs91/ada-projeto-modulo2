#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/validation-common.sh

POLICY="api-simulador-habitacional/policy"
OUTPUT="${CI_OUTPUT_DIR:-.ci}/sbom"
IMAGE="${CI_IMAGE:-ghcr.io/local/api-simulador-habitacional:ci-local}"
mkdir -p "$OUTPUT"
for file in sbom.cdx.json sbom.spdx.json; do
  if [[ ! -s "$OUTPUT/$file" ]]; then
    mark_error "SBOM obrigatório ausente ou vazio: $OUTPUT/$file"
  fi
done
if ! docker image inspect "$IMAGE" --format '{{.Id}}' > "$OUTPUT/image-id.txt"; then
  mark_error 'Não foi possível obter o ID da imagem.'
fi

# Ler o manifesto realmente publicado na imagem, não arquivos locais de outro build.
if ! docker run --rm --network none --entrypoint cat "$IMAGE" \
  /app/api-simulador-habitacional.deps.json > "$OUTPUT/application.deps.json"; then
  mark_error 'Não foi possível extrair o .deps.json da imagem.'
fi

run_check 'Escopo SBOM' node scripts/sbom-scope.mjs "$OUTPUT/application.deps.json" "$OUTPUT/scope.json"

# Avalia cada formato separadamente: um arquivo inválido não oculta o outro.
for file in sbom.cdx.json sbom.spdx.json; do
  if ! conftest test "$OUTPUT/$file" \
    --policy "$POLICY" --data "$OUTPUT/scope.json" --namespace sbom --output json > "$OUTPUT/$file.validation.json"; then
    mark_error "Validação SBOM reprovada: $file"
  fi
done
run_check 'Relatório dos SBOMs' node - "$OUTPUT" <<'NODE'
const fs = require('node:fs');
const path = require('node:path');
const output = process.argv[2];
const results = [];
for (const file of ['sbom.cdx.json', 'sbom.spdx.json']) {
  try {
    results.push(...JSON.parse(fs.readFileSync(path.join(output, `${file}.validation.json`), 'utf8')));
  } catch (error) {
    results.push({ filename: file, failures: [{ msg: `Relatório indisponível ou inválido: ${error.message}` }] });
    process.exitCode = 1;
  }
}
fs.writeFileSync(path.join(output, 'validation.json'), JSON.stringify(results, null, 2));
for (const result of results) {
  const failures = result.failures || [];
  console.log(`${result.filename}: ${failures.length} violação(ões) SBOM.`);
  for (const failure of failures) console.log(`  ${failure.msg}`);
}
NODE
exit "$status"