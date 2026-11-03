#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

POLICY="api-simulador-habitacional/policy"
OUTPUT="${CI_OUTPUT_DIR:-.ci}/sbom"
IMAGE="${CI_IMAGE:-ghcr.io/local/api-simulador-habitacional:ci-local}"
for file in sbom.cdx.json sbom.spdx.json; do
  if [[ ! -s "$OUTPUT/$file" ]]; then
    echo "SBOM obrigatório ausente ou vazio: $OUTPUT/$file" >&2
    exit 1
  fi
done
docker image inspect "$IMAGE" --format '{{.Id}}' > "$OUTPUT/image-id.txt"

# Ler o manifesto realmente publicado na imagem, não arquivos locais de outro build.
docker run --rm --network none --entrypoint cat "$IMAGE" \
  /app/api-simulador-habitacional.deps.json > "$OUTPUT/application.deps.json"

node scripts/sbom-scope.mjs "$OUTPUT/application.deps.json" "$OUTPUT/scope.json"

# Preserva o código de reprovação; registra os dois formatos para revisão.
status=0
conftest test "$OUTPUT/sbom.cdx.json" "$OUTPUT/sbom.spdx.json" \
  --policy "$POLICY" --data "$OUTPUT/scope.json" --namespace sbom --output json > "$OUTPUT/validation.json" || status=$?
node - "$OUTPUT/validation.json" <<'NODE'
const fs = require('node:fs');
const results = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
for (const result of results) {
  const failures = result.failures || [];
  console.log(`${result.filename}: ${failures.length} violação(ões) SBOM.`);
  for (const failure of failures.slice(0, 20)) console.log(`  ${failure.msg}`);
  if (failures.length > 20) console.log('  Consulte validation.json para todas as violações.');
}
NODE
exit "$status"