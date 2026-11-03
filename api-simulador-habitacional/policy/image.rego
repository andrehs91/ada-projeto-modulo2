package image

import rego.v1

# Entrada: docker image inspect --format '{{json .}}' (um objeto, não um array).
deny contains "IMG001: a imagem final deve configurar um UID numérico não-root." if {
    user := object.get(object.get(input, "Config", {}), "User", "")
    not regex.match(`^[1-9][0-9]*(:[1-9][0-9]*)?$`, user)
}

has_command if { count(input.Config.Entrypoint) > 0 }
has_command if { count(input.Config.Cmd) > 0 }

deny contains "IMG002: a imagem deve definir Entrypoint ou Cmd." if {
    not has_command
}