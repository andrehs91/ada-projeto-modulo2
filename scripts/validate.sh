#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

POLICY="policy"
OUTPUT=".ci"
IMAGE="${CI_IMAGE:-ghcr.io/local/api-simulador-habitacional:ci-local}"
mkdir -p "$OUTPUT/reports"

opa check --strict "$POLICY"
opa test "$POLICY" --coverage --format=json > "$OUTPUT/reports/coverage.json"
conftest test api-simulador-habitacional/Dockerfile --policy "$POLICY" --namespace dockerfile
conftest test "$OUTPUT/k8s/" --policy "$POLICY" --namespace kubernetes

# Inspeção de um objeto individual: evita depender do formato array de docker inspect.
docker image inspect "$IMAGE" --format '{{json .}}' > "$OUTPUT/reports/image-inspect.json"
conftest test "$OUTPUT/reports/image-inspect.json" --policy "$POLICY" --namespace image