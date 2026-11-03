package kubernetes

import rego.v1

fixture := {
    "kind": "Deployment",
    "spec": {"replicas": 2, "template": {"spec": {
        "automountServiceAccountToken": false,
        "securityContext": {"runAsNonRoot": true, "runAsUser": 1654, "seccompProfile": {"type": "RuntimeDefault"}},
        "containers": [{
            "name": "api", "image": "ghcr.io/example/api:1.0.0",
            "securityContext": {"allowPrivilegeEscalation": false, "privileged": false, "readOnlyRootFilesystem": true, "capabilities": {"drop": ["ALL"]}},
            "resources": {"requests": {"cpu": "100m", "memory": "128Mi"}, "limits": {"cpu": "500m", "memory": "256Mi"}},
            "env": [{"name": "ASPNETCORE_ENVIRONMENT", "value": "Production"}],
            "ports": [{"name": "http", "containerPort": 8080}],
            "startupProbe": {"httpGet": {"path": "/health", "port": "http"}},
            "readinessProbe": {"httpGet": {"path": "/health", "port": "http"}},
            "livenessProbe": {"httpGet": {"path": "/health", "port": "http"}}
        }]
    }}}
}

has_code(code) if { some msg in deny; startswith(msg, code) }

test_allow_hardened_deployment if { count(deny) == 0 with input as fixture }

# Cada caso altera apenas uma proteção e exige a violação correspondente.
test_reject_security_regressions if {
    cases := [
        ["K001:", "/spec/template/spec/securityContext/runAsNonRoot", false],
        ["K002:", "/spec/template/spec/containers/0/securityContext/runAsUser", 0],
        ["K003:", "/spec/template/spec/containers/0/securityContext/allowPrivilegeEscalation", true],
        ["K004:", "/spec/template/spec/containers/0/securityContext/privileged", true],
        ["K005:", "/spec/template/spec/containers/0/securityContext/readOnlyRootFilesystem", false],
        ["K006:", "/spec/template/spec/containers/0/securityContext/capabilities/drop", []],
        ["K006:", "/spec/template/spec/containers/0/securityContext/capabilities/add", ["NET_ADMIN"]],
        ["K007:", "/spec/template/spec/securityContext/seccompProfile/type", "Unconfined"],
        ["K008:", "/spec/template/spec/hostNetwork", true],
        ["K008:", "/spec/template/spec/hostPID", true],
        ["K008:", "/spec/template/spec/hostIPC", true],
        ["K009:", "/spec/template/spec/volumes", [{"name": "host", "hostPath": {"path": "/"}}]],
        ["K010:", "/spec/template/spec/automountServiceAccountToken", true],
        ["K011:", "/spec/template/spec/containers/0/resources/limits/memory", "0Mi"],
        ["K012:", "/spec/template/spec/containers/0/image", "ghcr.io/example/api:latest"],
        ["K013:", "/spec/template/spec/containers/0/readinessProbe/httpGet/path", "/missing"],
        ["K013:", "/spec/template/spec/containers/0/readinessProbe/httpGet/port", "missing"],
        ["K014:", "/spec/template/spec/containers/0/env/0/value", "Development"],
        ["K015:", "/spec/template/spec/containers/0/env/1", {"name": "ConnectionStrings__Db", "value": "literal"}],
        ["K016:", "/spec/template/spec/containers/0/ports/0/hostPort", 8080],
        ["K016:", "/spec/template/spec/containers/0/ports/0/containerPort", 80]
    ]
    every case in cases {
        changed := json.patch(fixture, [{"op": "add", "path": case[1], "value": case[2]}])
        has_code(case[0]) with input as changed
    }
}
test_reject_missing_required_fields if {
    cases := [
        ["K001:", "/spec/template/spec/securityContext/runAsNonRoot"],
        ["K002:", "/spec/template/spec/securityContext/runAsUser"],
        ["K003:", "/spec/template/spec/containers/0/securityContext/allowPrivilegeEscalation"],
        ["K005:", "/spec/template/spec/containers/0/securityContext/readOnlyRootFilesystem"],
        ["K006:", "/spec/template/spec/containers/0/securityContext/capabilities"],
        ["K007:", "/spec/template/spec/securityContext/seccompProfile"],
        ["K010:", "/spec/template/spec/automountServiceAccountToken"],
        ["K011:", "/spec/template/spec/containers/0/resources"],
        ["K013:", "/spec/template/spec/containers/0/startupProbe"],
        ["K014:", "/spec/template/spec/containers/0/env"]
    ]
    every case in cases {
        changed := json.patch(fixture, [{"op": "remove", "path": case[1]}])
        has_code(case[0]) with input as changed
    }
}
test_reject_invalid_images if {
    every reference in ["ghcr.io/example/api", "ghcr.io/example/api@sha256:bad", "evil.example/api:1.0", "ghcr.io.evil.example/api:1.0", "ghcr.io/example/api:latest@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"] {
        changed := json.patch(fixture, [{"op": "replace", "path": "/spec/template/spec/containers/0/image", "value": reference}])
        has_code("K012:") with input as changed
    }
}
test_allow_cloud_registry_images if {
    every reference in [
        "example.azurecr.io/api:42.1",
        "example-abc123.azurecr.io/api:42.1",
        "123456789012.dkr.ecr.us-east-1.amazonaws.com/api:42.1",
        "123456789012.dkr.ecr.cn-north-1.amazonaws.com.cn/api:42.1",
        "public.ecr.aws/example/api:42.1",
        "us-central1-docker.pkg.dev/project/repository/api:42.1",
        "europe-west1-docker.pkg.dev/project/repository/api:42.1",
        "gcr.io/project/api:42.1",
        "us.gcr.io/project/api:42.1",
        "eu.gcr.io/project/api:42.1",
        "asia.gcr.io/project/api:42.1"
    ] {
        changed := json.patch(fixture, [{"op": "replace", "path": "/spec/template/spec/containers/0/image", "value": reference}])
        count(deny) == 0 with input as changed
    }
}
test_reject_spoofed_cloud_registries if {
    every host in [
        "example.azurecr.io.evil.example",
        "123456789012.dkr.ecr.us-east-1.amazonaws.com.evil.example",
        "123.dkr.ecr.us-east-1.amazonaws.com",
        "public.ecr.aws.evil.example",
        "us-central1-docker.pkg.dev.evil.example",
        "gcr.io.evil.example"
    ] {
        reference := concat("", [host, "/example/api:42.1"])
        changed := json.patch(fixture, [{"op": "replace", "path": "/spec/template/spec/containers/0/image", "value": reference}])
        has_code("K012:") with input as changed
    }
}
test_allow_secret_reference if {
    env := {"name": "API_KEY", "valueFrom": {"secretKeyRef": {"name": "api", "key": "key"}}}
    changed := json.patch(fixture, [{"op": "add", "path": "/spec/template/spec/containers/0/env/1", "value": env}])
    count(deny) == 0 with input as changed
}
test_check_init_and_ephemeral_containers if {
    every key in ["initContainers", "ephemeralContainers"] {
        changed := json.patch(fixture, [{"op": "add", "path": concat("", ["/spec/template/spec/", key]), "value": [{"name": "unsafe", "image": "ghcr.io/example/api:1.0", "securityContext": {"runAsUser": 0}}]}])
        has_code("K002:") with input as changed
    }
}
test_support_workload_shapes if {
    spec := fixture.spec.template.spec
    every kind in ["Deployment", "StatefulSet", "DaemonSet", "ReplicaSet", "Job"] {
        count(deny) == 0 with input as {"kind": kind, "spec": {"template": {"spec": spec}}}
    }
    count(deny) == 0 with input as {"kind": "Pod", "spec": spec}
    count(deny) == 0 with input as {"kind": "CronJob", "spec": {"jobTemplate": {"spec": {"template": {"spec": spec}}}}}
}
test_service_policy if {
    count(deny) == 0 with input as {"kind": "Service", "spec": {"type": "ClusterIP"}}
    has_code("K017:") with input as {"kind": "Service", "spec": {"type": "NodePort"}}
}
test_ignore_configmap if { count(deny) == 0 with input as {"kind": "ConfigMap", "data": {}} }
test_warn_one_replica if {
    changed := json.patch(fixture, [{"op": "replace", "path": "/spec/replicas", "value": 1}])
    count(warn) == 2 with input as changed
}