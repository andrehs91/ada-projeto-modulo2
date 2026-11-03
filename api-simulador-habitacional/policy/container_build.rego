package dockerfile

import rego.v1

# Entrada: parser dockerfile do Conftest. Stage é o índice do estágio.
approved_repositories := {"mcr.microsoft.com/dotnet/aspnet", "mcr.microsoft.com/dotnet/sdk"}

froms := [instruction | some instruction in input; lower(instruction.Cmd) == "from"]

parent(stage) := previous.Stage if {
    some instruction in froms
    instruction.Stage == stage
    some previous in froms
    previous.Stage < stage
    count(previous.Value) >= 3
    lower(previous.Value[1]) == "as"
    lower(previous.Value[2]) == lower(instruction.Value[0])
}

parent(stage) := previous.Stage if {
    some instruction in froms
    instruction.Stage == stage
    some previous in froms
    previous.Stage < stage
    sprintf("%d", [previous.Stage]) == instruction.Value[0]
}

stage_graph := {sprintf("%d", [instruction.Stage]): edges |
    some instruction in froms
    edges := {sprintf("%d", [p]) | p := parent(instruction.Stage)}
}

final_stage := max([instruction.Stage | some instruction in froms])
ancestors := graph.reachable(stage_graph, {sprintf("%d", [final_stage])})

# O último USER do estágio ancestral mais próximo prevalece.
user_indexes := {i |
    some i, instruction in input
    lower(instruction.Cmd) == "user"
    sprintf("%d", [instruction.Stage]) in ancestors
}

effective_user := concat("", input[max(user_indexes)].Value)
valid_final_user if { numeric_nonroot(effective_user) }

numeric_nonroot(user) if {
    regex.match(`^[1-9][0-9]*(:[1-9][0-9]*)?$`, user)
}

approved_external(reference) if {
    some repository in approved_repositories
    startswith(reference, concat("", [repository, ":"]))
    regex.match(`^10\.[0-9]+(\.[0-9]+)?(-[a-zA-Z0-9.-]+)?(@sha256:[a-f0-9]{64})?$`, trim_prefix(reference, concat("", [repository, ":"])))
}

approved_external(reference) if {
    some repository in approved_repositories
    startswith(reference, concat("", [repository, "@sha256:"]))
    regex.match(`^[a-f0-9]{64}$`, trim_prefix(reference, concat("", [repository, "@sha256:"])))
}

deny contains "DF001: o Dockerfile precisa de pelo menos um FROM." if {
    count(froms) == 0
}

deny contains sprintf("DF002: base externa não aprovada ou versão não permitida: %s.", [instruction.Value[0]]) if {
    some instruction in froms
    not parent(instruction.Stage)
    not approved_external(instruction.Value[0])
}

deny contains "DF003: o estágio final deve herdar ou declarar USER com UID numérico não-root (GID, se presente, também não-zero)." if {
    count(froms) > 0
    not valid_final_user
}

deny contains "DF004: não use ADD; prefira COPY e downloads verificados em etapa explícita." if {
    some instruction in input
    lower(instruction.Cmd) == "add"
}

deny contains "DF005: não exponha portas privilegiadas (<1024)." if {
    some instruction in input
    lower(instruction.Cmd) == "expose"
    some value in instruction.Value
    port := to_number(split(value, "/")[0])
    port < 1024
}

warn contains "DFW01: fixe bases externas por digest sha256 e automatize sua atualização." if {
    some instruction in froms
    not parent(instruction.Stage)
    not contains(instruction.Value[0], "@sha256:")
}