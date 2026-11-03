#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

POLICY="api-simulador-habitacional/policy"
OUTPUT="${CI_OUTPUT_DIR:-.ci}"
IMAGE="${CI_IMAGE:-ghcr.io/local/api-simulador-habitacional:ci-local}"
mkdir -p "$OUTPUT/reports"

opa check --strict "$POLICY"
opa test "$POLICY" -v
opa test "$POLICY" --coverage --format=json > "$OUTPUT/reports/coverage.json"
conftest test api-simulador-habitacional/Dockerfile --policy "$POLICY" --namespace dockerfile
conftest test "$OUTPUT/k8s/" --policy "$POLICY" --namespace kubernetes --namespace configuration

# Os templates não renderizados devem falhar: comprova a proteção contra placeholders.
if conftest test k8s/deployment.yaml --policy "$POLICY" --namespace configuration --output json > "$OUTPUT/reports/templates-negative.json"; then
  echo 'ERRO: templates com placeholders foram aprovados.' >&2
  exit 1
fi

# Inspeção de um objeto individual: evita depender do formato array de docker inspect.
docker image inspect "$IMAGE" --format '{{json .}}' > "$OUTPUT/reports/image-inspect.json"
conftest test "$OUTPUT/reports/image-inspect.json" --policy "$POLICY" --namespace image

# Opa avalia metadados; verificar arquivos da imagem exige uma checagem real.
docker run --rm --network none --entrypoint /bin/sh "$IMAGE" -c 'test ! -d /usr/share/dotnet/sdk'
echo 'Políticas e ausência de SDK na imagem de execução validadas.'