#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/validation-common.sh

POLICY="api-simulador-habitacional/policy"
OUTPUT="${CI_OUTPUT_DIR:-.ci}"
IMAGE="${CI_IMAGE:-ghcr.io/local/api-simulador-habitacional:ci-local}"
mkdir -p "$OUTPUT/reports"

run_check 'Sintaxe Rego' opa check --strict "$POLICY"
run_check 'Testes Rego' opa test "$POLICY" -v
if ! opa test "$POLICY" --coverage --format=json > "$OUTPUT/reports/coverage.json"; then
  mark_error 'Cobertura/testes Rego reprovados.'
fi
run_check 'Políticas Dockerfile' conftest test api-simulador-habitacional/Dockerfile --policy "$POLICY" --namespace dockerfile
run_check 'Políticas Kubernetes' conftest test "$OUTPUT/k8s/" --policy "$POLICY" --namespace kubernetes
run_check 'Políticas de configuração' conftest test "$OUTPUT/k8s/" --policy "$POLICY" --namespace configuration

# Os templates não renderizados devem falhar: comprova a proteção contra placeholders.
if conftest test k8s/deployment.yaml --policy "$POLICY" --namespace configuration --output json > "$OUTPUT/reports/templates-negative.json"; then
  mark_error 'Templates com placeholders foram aprovados indevidamente.'
else
  code=$?
  if [[ "$code" != 1 ]]; then
    mark_error "Erro de execução no teste negativo de placeholders (exit $code)."
  fi
fi

# Inspeção de um objeto individual: evita depender do formato array de docker inspect.
if ! docker image inspect "$IMAGE" --format '{{json .}}' > "$OUTPUT/reports/image-inspect.json"; then
  mark_error 'Não foi possível inspecionar a imagem.'
fi
run_check 'Políticas da imagem' conftest test "$OUTPUT/reports/image-inspect.json" --policy "$POLICY" --namespace image

# Opa avalia metadados; verificar arquivos da imagem exige uma checagem real.
run_check 'Ausência de SDK na imagem' docker run --rm --network none --entrypoint /bin/sh "$IMAGE" -c 'test ! -d /usr/share/dotnet/sdk'
echo "Validações concluídas (status=$status)."
exit "$status"