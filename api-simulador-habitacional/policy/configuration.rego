package configuration

import rego.v1

deny contains "CFG001: substitua todos os placeholders antes da validação." if {
    some path, value in walk(input)
    is_string(value)
    regex.match(`SUBSTITUA_|seu-registry/`, value)
}

tls_value(key) := value if {
    input.kind == "Secret"
    input.type == "kubernetes.io/tls"
    value := input.stringData[key]
}
tls_value(key) := value if {
    input.kind == "Secret"
    input.type == "kubernetes.io/tls"
    not object.get(input, "stringData", {})[key]
    value := base64.decode(input.data[key])
}

deny contains "CFG002: Secret TLS deve conter tls.crt e tls.key em formato PEM (validar criptografia fora do Rego)." if {
    input.kind == "Secret"
    input.type == "kubernetes.io/tls"
    not valid_certificate
}
deny contains "CFG002: Secret TLS deve conter tls.crt e tls.key em formato PEM (validar criptografia fora do Rego)." if {
    input.kind == "Secret"
    input.type == "kubernetes.io/tls"
    not valid_private_key
}
valid_certificate if {
    value := tls_value("tls.crt")
    startswith(value, "-----BEGIN CERTIFICATE-----\n")
    contains(value, "\n-----END CERTIFICATE-----")
}
valid_private_key if {
    value := tls_value("tls.key")
    regex.match(`^-----BEGIN (RSA |EC )?PRIVATE KEY-----\n`, value)
    regex.match(`\n-----END (RSA |EC )?PRIVATE KEY-----`, value)
}

warn contains "CFW01: Secret com dados inline: não versione segredos reais; use um gerenciador de segredos." if {
    input.kind == "Secret"
    count(object.get(input, "stringData", {})) + count(object.get(input, "data", {})) > 0
}