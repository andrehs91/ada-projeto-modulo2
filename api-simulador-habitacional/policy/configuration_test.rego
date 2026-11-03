package configuration

import rego.v1

# Fixtures verificam estrutura, não validade criptográfica de um certificado.
fixture := {"kind": "Secret", "type": "kubernetes.io/tls", "stringData": {
    "tls.crt": "-----BEGIN CERTIFICATE-----\nTEST\n-----END CERTIFICATE-----\n",
    "tls.key": "-----BEGIN PRIVATE KEY-----\nTEST\n-----END PRIVATE KEY-----\n"
}}

test_allow_pem_structure if { count(deny) == 0 with input as fixture }
test_warn_inline_secret if { count(warn) == 1 with input as fixture }
test_allow_base64_secret if {
    secret := {"kind": "Secret", "type": "kubernetes.io/tls", "data": {
        "tls.crt": base64.encode(fixture.stringData["tls.crt"]),
        "tls.key": base64.encode(fixture.stringData["tls.key"])
    }}
    count(deny) == 0 with input as secret
}
test_deny_placeholders if {
    every value in ["SUBSTITUA_PELO_CERTIFICADO", "seu-registry/api:1.0"] {
        deny["CFG001: substitua todos os placeholders antes da validação."] with input as {"data": {"nested": value}}
    }
}
test_deny_missing_tls_keys if {
    deny["CFG002: Secret TLS deve conter tls.crt e tls.key em formato PEM (validar criptografia fora do Rego)."] with input as {"kind": "Secret", "type": "kubernetes.io/tls", "data": {}}
}
test_deny_non_pem if {
    changed := object.union(fixture, {"stringData": {"tls.crt": "invalid", "tls.key": "invalid"}})
    count(deny) == 1 with input as changed
}
test_ignore_regular_configmap if {
    count(deny) == 0 with input as {"kind": "ConfigMap", "data": {"appsettings.json": "{}"}}
}