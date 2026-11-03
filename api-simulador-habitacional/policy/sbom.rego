package sbom

import rego.v1

# Perfil conservador: somente identificadores permissivos explicitamente revisados.
# IDs não listados (incluindo copyleft e LicenseRef) exigem revisão e bloqueiam.
permissive := {
    "MIT", "MIT-0", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause",
    "BSD-3-Clause-Clear", "0BSD", "ISC", "Unlicense", "CC0-1.0",
    "Zlib", "BSL-1.0", "BlueOak-1.0.0", "Python-2.0", "PSF-2.0",
    "Unicode-DFS-2016", "Unicode-3.0", "WTFPL", "PostgreSQL",
}

supported if { input.bomFormat == "CycloneDX"; is_array(input.components) }
supported if { input.spdxVersion == "SPDX-2.3"; is_array(input.packages) }

default scope_names := []
scope_names := data.sbom_scope.packages

inventory contains component if {
    input.bomFormat == "CycloneDX"
    walk(input, [path, value])
    count(path) > 0
    path[0] == "components"
    path[count(path) - 1] == "components"
    is_array(value)
    some component in value
    object.get(component, "type", "library") != "file"
}
inventory contains pkg if { input.spdxVersion == "SPDX-2.3"; some pkg in input.packages }

# Escopo exato da aplicação, gerado de libraries no .deps.json publicado.
items contains item if {
    some item in inventory
    lower(item_name(item)) in {lower(name) | some name in scope_names}
}

item_name(item) := object.get(item, "name", "<sem nome>")

valid_purl(value) if {
    is_string(value)
    regex.match(`^pkg:[a-z][a-z0-9.+-]*/[^\s?#]+(\?[^\s#]+)?(#[^\s]+)?$`, value)
}
has_purl(item) if {
    input.bomFormat == "CycloneDX"
    valid_purl(object.get(item, "purl", ""))
}
has_purl(item) if {
    input.spdxVersion == "SPDX-2.3"
    some ref in object.get(item, "externalRefs", [])
    ref.referenceCategory == "PACKAGE-MANAGER"
    ref.referenceType == "purl"
    valid_purl(ref.referenceLocator)
}

cdx_expressions(item) := {expression |
    input.bomFormat == "CycloneDX"
    some entry in object.get(item, "licenses", [])
    expression := object.get(entry, "expression", object.get(object.get(entry, "license", {}), "id",
        object.get(object.get(entry, "license", {}), "name", "")))
}
spdx_expressions(item) := {expression |
    input.spdxVersion == "SPDX-2.3"
    some key in {"licenseDeclared", "licenseConcluded"}
    expression := object.get(item, key, "")
    expression != ""
}

expressions(item) := cdx_expressions(item) | spdx_expressions(item)

# Não seleciona automaticamente o ramo permissivo de OR e não aprova exceções WITH.
# Faz classificação conservadora de IDs, não valida toda a gramática SPDX.
reviewed_expression(expression) if {
    is_string(expression)
    identifiers := regex.find_all_string_submatch_n(`[A-Za-z0-9][A-Za-z0-9.+:-]*`, expression, -1)
    count(identifiers) > 0
    some token in identifiers
    token[0] in permissive
    every token in identifiers { token[0] in permissive | {"AND", "OR"} }
    not regex.match(`[^A-Za-z0-9.()+:\s-]`, expression)
}

known_copyleft := {
    "GPL-1.0-only", "GPL-1.0-or-later", "GPL-2.0-only", "GPL-2.0-or-later",
    "GPL-3.0-only", "GPL-3.0-or-later", "AGPL-1.0-only", "AGPL-1.0-or-later",
    "AGPL-3.0-only", "AGPL-3.0-or-later", "LGPL-2.0-only", "LGPL-2.0-or-later",
    "LGPL-2.1-only", "LGPL-2.1-or-later", "LGPL-3.0-only", "LGPL-3.0-or-later",
    "MPL-1.0", "MPL-1.1", "MPL-2.0", "EPL-1.0", "EPL-2.0", "CDDL-1.0",
    "CDDL-1.1", "EUPL-1.1", "EUPL-1.2", "OSL-3.0", "SSPL-1.0", "RPL-1.5",
}

concrete_expressions(item) := {expression |
    some expression in expressions(item)
    is_string(expression)
    trim_space(expression) != ""
    not expression in {"NOASSERTION", "NONE"}
}

has_license_information(item) if {
    some expression in expressions(item)
    expression != ""
    expression != null
}

has_concrete_license(item) if { count(concrete_expressions(item)) > 0 }

deny contains "SBOM008: escopo da aplicação ausente; forneça os nomes extraídos do .deps.json via --data." if {
    count(scope_names) == 0
}

deny contains sprintf("SBOM009: dependência da aplicação '%s' não encontrada no SBOM.", [name]) if {
    some name in scope_names
    not present(name)
}
present(name) if { some item in items; lower(item_name(item)) == lower(name) }

deny contains "SBOM001: esperado SBOM CycloneDX com components ou SPDX-2.3 com packages." if {
    not supported
}
deny contains "SBOM002: nenhum pacote da aplicação foi encontrado no inventário." if {
    supported
    count(items) == 0
}
deny contains sprintf("SBOM003: '%s' sem PURL reconhecível.", [item_name(item)]) if {
    some item in items
    not has_purl(item)
}
deny contains sprintf("SBOM004: '%s' sem nome ou versão.", [item_name(item)]) if {
    some item in items
    not identified(item)
}
identified(item) if {
    is_string(item.name)
    trim_space(item.name) != ""
    version := object.get(item, "version", object.get(item, "versionInfo", ""))
    is_string(version)
    trim_space(version) != ""
}
warn contains sprintf("SBOM005: '%s' sem declaração de licença no SBOM (campo ausente ou vazio).", [item_name(item)]) if {
    some item in items
    not has_license_information(item)
}
warn contains sprintf("SBOM006: '%s' com licença não determinada pelo SBOM; valores informados: %v.", [item_name(item), expressions(item)]) if {
    some item in items
    has_license_information(item)
    not has_concrete_license(item)
}
deny contains sprintf("SBOM007: '%s' com licença não aprovada: %s (copyleft bloqueado pelo perfil conservador).", [item_name(item), id]) if {
    some item in items
    some expression in concrete_expressions(item)
    some token in regex.find_all_string_submatch_n(`[A-Za-z0-9][A-Za-z0-9.+:-]*`, expression, -1)
    id := token[0]
    id in known_copyleft
}
deny contains sprintf("SBOM006: '%s' com expressão de licença não reconhecida ou exceção não revisada: %s.", [item_name(item), expression]) if {
    some item in items
    some expression in concrete_expressions(item)
    not reviewed_expression(expression)
    not only_known_unapproved(expression)
}
only_known_unapproved(expression) if {
    tokens := regex.find_all_string_submatch_n(`[A-Za-z0-9][A-Za-z0-9.+:-]*`, expression, -1)
    count(tokens) > 0
    some token in tokens
    token[0] in known_copyleft
    every token in tokens { token[0] in permissive | known_copyleft | {"AND", "OR"} }
    not regex.match(`[^A-Za-z0-9.()+:\s-]`, expression)
}