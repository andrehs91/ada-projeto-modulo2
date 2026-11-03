#!/usr/bin/env bash
# As verificações continuam; o script chamador retorna falha ao final.
status=0

mark_error() {
  status=1
  export HAS_ERROR=true
  if [[ -n "${GITHUB_ENV:-}" ]]; then
    echo 'HAS_ERROR=true' >> "$GITHUB_ENV"
  fi
  echo "::error::$*" >&2
}

run_check() {
  local label="$1"
  shift
  echo "=== $label ==="
  if "$@"; then
    echo "OK: $label"
  else
    local code=$?
    mark_error "$label falhou (exit $code)."
  fi
}