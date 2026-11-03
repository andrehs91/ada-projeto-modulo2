package kubernetes

import rego.v1

pod_spec := input.spec if { input.kind == "Pod" }
pod_spec := input.spec.template.spec if {
    input.kind in {"Deployment", "StatefulSet", "DaemonSet", "ReplicaSet", "Job"}
}
pod_spec := input.spec.jobTemplate.spec.template.spec if { input.kind == "CronJob" }

containers contains c if { some c in object.get(pod_spec, "containers", []) }
containers contains c if { some c in object.get(pod_spec, "initContainers", []) }
containers contains c if { some c in object.get(pod_spec, "ephemeralContainers", []) }

effective(c, key, fallback) := object.get(object.get(c, "securityContext", {}), key,
    object.get(object.get(pod_spec, "securityContext", {}), key, fallback))

container_context(c, key, fallback) := object.get(object.get(c, "securityContext", {}), key, fallback)

approved_image(reference) if {
    host := split(reference, "/")[0]
    regex.match(`^(ghcr\.io|docker\.io|mcr\.microsoft\.com|[a-z0-9]+(-[a-z0-9]+)?\.azurecr\.io|[0-9]{12}\.dkr\.ecr\.[a-z0-9]+(-[a-z0-9]+)+\.amazonaws\.com(\.cn)?|public\.ecr\.aws|([a-z0-9]+(-[a-z0-9]+)+)-docker\.pkg\.dev|(us\.|eu\.|asia\.)?gcr\.io)$`, host)
    regex.match(`^[a-z0-9.-]+/[a-z0-9_./-]+(:[a-zA-Z0-9_][a-zA-Z0-9_.-]*)?@sha256:[a-f0-9]{64}$|^[a-z0-9.-]+/[a-z0-9_./-]+:[a-zA-Z0-9_][a-zA-Z0-9_.-]*$`, reference)
    not regex.match(`:latest(@|$)`, lower(reference))
}

deny contains "K001: runAsNonRoot deve ser true (Pod ou container)." if {
    some c in containers
    effective(c, "runAsNonRoot", false) != true
}
deny contains "K002: runAsUser deve ser um UID numérico maior que zero (Pod ou container)." if {
    some c in containers
    user := effective(c, "runAsUser", 0)
    not valid_uid(user)
}
valid_uid(user) if { is_number(user); user > 0; floor(user) == user }

deny contains "K003: allowPrivilegeEscalation deve ser false no container." if {
    some c in containers
    container_context(c, "allowPrivilegeEscalation", true) != false
}
deny contains "K004: privileged não pode ser true." if {
    some c in containers
    container_context(c, "privileged", false) != false
}
deny contains "K005: readOnlyRootFilesystem deve ser true no container." if {
    some c in containers
    container_context(c, "readOnlyRootFilesystem", false) != true
}
deny contains "K006: capabilities.drop deve conter ALL e capabilities.add deve estar vazio." if {
    some c in containers
    caps := container_context(c, "capabilities", {})
    not "ALL" in object.get(caps, "drop", [])
}
deny contains "K006: capabilities.drop deve conter ALL e capabilities.add deve estar vazio." if {
    some c in containers
    count(object.get(container_context(c, "capabilities", {}), "add", [])) > 0
}
deny contains "K007: seccompProfile.type deve ser RuntimeDefault ou Localhost." if {
    some c in containers
    profile := effective(c, "seccompProfile", {})
    not object.get(profile, "type", "") in {"RuntimeDefault", "Localhost"}
}
deny contains "K008: hostNetwork, hostPID e hostIPC são proibidos." if {
    some key in {"hostNetwork", "hostPID", "hostIPC"}
    object.get(pod_spec, key, false) != false
}
deny contains "K009: volumes hostPath são proibidos." if {
    some volume in object.get(pod_spec, "volumes", [])
    "hostPath" in object.keys(volume)
}
deny contains "K010: automountServiceAccountToken deve ser false; exceções precisam de revisão." if {
    object.get(pod_spec, "automountServiceAccountToken", true) != false
}
deny contains "K011: cada container precisa de requests e limits de CPU e memória positivos." if {
    some c in containers
    some group in {"requests", "limits"}
    some resource in {"cpu", "memory"}
    value := object.get(object.get(object.get(c, "resources", {}), group, {}), resource, "")
    not positive_quantity(value)
}
positive_quantity(value) if {
    is_string(value)
    regex.match(`^([0-9]+(\.[0-9]+)?|\.[0-9]+)([eE][+-]?[0-9]+|[numkKMGTPE]|[KMGTPE]i)?$`, value)
    number := regex.find_n(`^([0-9]+(\.[0-9]+)?|\.[0-9]+)`, value, 1)[0]
    to_number(number) > 0
}
positive_quantity(value) if { is_number(value); value > 0 }

deny contains "K012: imagem deve vir de registry aprovado e ter tag explícita diferente de latest ou digest sha256." if {
    some c in containers
    not approved_image(object.get(c, "image", ""))
}
deny contains "K013: serviços de longa duração precisam de startupProbe, readinessProbe e livenessProbe HTTP /health." if {
    input.kind in {"Pod", "Deployment", "StatefulSet", "DaemonSet", "ReplicaSet"}
    some c in object.get(pod_spec, "containers", [])
    some key in {"startupProbe", "readinessProbe", "livenessProbe"}
    not valid_probe(object.get(c, key, {}), c)
}
valid_probe(probe, c) if {
    probe.httpGet.path == "/health"
    object.get(probe.httpGet, "scheme", "HTTP") == "HTTP"
    some port in object.get(c, "ports", [])
    port.containerPort >= 1024
    probe.httpGet.port in {port.containerPort, object.get(port, "name", "")}
}
deny contains "K014: ambiente ASPNETCORE_ENVIRONMENT deve ser Production nos containers da aplicação." if {
    some c in object.get(pod_spec, "containers", [])
    not production(c)
}
production(c) if {
    some env in object.get(c, "env", [])
    env.name == "ASPNETCORE_ENVIRONMENT"
    env.value == "Production"
}
deny contains "K015: variáveis sensíveis devem usar valueFrom.secretKeyRef, nunca value literal." if {
    some c in containers
    some env in object.get(c, "env", [])
    regex.match(`(?i)(password|passwd|secret|token|api[_-]?key|connectionstrings?)`, env.name)
    not secret_reference(env)
}
secret_reference(env) if {
    env.valueFrom.secretKeyRef.name != ""
    env.valueFrom.secretKeyRef.key != ""
    not "value" in object.keys(env)
}
deny contains "K016: hostPort e portas privilegiadas no container são proibidos." if {
    some c in containers
    some port in object.get(c, "ports", [])
    object.get(port, "hostPort", 0) != 0
}
deny contains "K016: hostPort e portas privilegiadas no container são proibidos." if {
    some c in containers
    some port in object.get(c, "ports", [])
    port.containerPort < 1024
}
deny contains "K017: Service deve ser ClusterIP; exposição externa exige política específica." if {
    input.kind == "Service"
    object.get(input.spec, "type", "ClusterIP") != "ClusterIP"
}
warn contains "KW01: use ao menos duas réplicas e um PodDisruptionBudget para alta disponibilidade." if {
    input.kind in {"Deployment", "StatefulSet"}
    object.get(input.spec, "replicas", 1) < 2
}
warn contains "KW02: tags são mutáveis; em produção prefira imagens fixadas por digest." if {
    some c in containers
    not contains(object.get(c, "image", ""), "@sha256:")
}