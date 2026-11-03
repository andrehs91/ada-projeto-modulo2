package dockerfile

import rego.v1

fixture := [
    {"Cmd": "from", "Stage": 0, "Value": ["mcr.microsoft.com/dotnet/aspnet:10.0", "AS", "base"]},
    {"Cmd": "user", "Stage": 0, "Value": ["1654"]},
    {"Cmd": "expose", "Stage": 0, "Value": ["8080", "8081"]},
    {"Cmd": "from", "Stage": 1, "Value": ["mcr.microsoft.com/dotnet/sdk:10.0", "AS", "build"]},
    {"Cmd": "from", "Stage": 2, "Value": ["build", "AS", "publish"]},
    {"Cmd": "from", "Stage": 3, "Value": ["base", "AS", "final"]},
]

has_code(code) if { some msg in deny; startswith(msg, code) }

test_allow_inherited_nonroot if { count(deny) == 0 with input as fixture }
test_warn_mutable_bases if { count(warn) == 1 with input as fixture }
test_deny_no_from if { has_code("DF001:") with input as [] }
test_deny_root_override if {
    changed := array.concat(fixture, [{"Cmd": "user", "Stage": 3, "Value": ["0"]}])
    has_code("DF003:") with input as changed
}
test_deny_root_group if {
    changed := array.concat(fixture, [{"Cmd": "user", "Stage": 3, "Value": ["1654:0"]}])
    has_code("DF003:") with input as changed
}
test_deny_unknown_variable if {
    changed := json.patch(fixture, [{"op": "replace", "path": "/1/Value/0", "value": "$APP_UID"}])
    has_code("DF003:") with input as changed
}
test_deny_missing_user if {
    changed := json.patch(fixture, [{"op": "remove", "path": "/1"}])
    has_code("DF003:") with input as changed
}
test_no_inheritance_from_unrelated_stage if {
    changed := json.patch(fixture, [{"op": "replace", "path": "/5/Value/0", "value": "mcr.microsoft.com/dotnet/aspnet:10.0"}])
    has_code("DF003:") with input as changed
}
test_allow_numeric_stage_parent if {
    changed := json.patch(fixture, [{"op": "replace", "path": "/5/Value/0", "value": "0"}])
    count(deny) == 0 with input as changed
}
test_deny_invalid_bases if {
    every reference in ["ubuntu:24.04", "mcr.microsoft.com/dotnet/aspnet:latest", "mcr.microsoft.com/dotnet/aspnet", "mcr.microsoft.com/dotnet/aspnet-backdoor:10.0", "mcr.microsoft.com/dotnet/aspnet:9.0"] {
        changed := json.patch(fixture, [{"op": "replace", "path": "/0/Value/0", "value": reference}])
        has_code("DF002:") with input as changed
    }
}
test_allow_digest if {
    reference := "mcr.microsoft.com/dotnet/aspnet@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    changed := json.patch(fixture, [{"op": "replace", "path": "/0/Value/0", "value": reference}])
    count(deny) == 0 with input as changed
}
test_deny_add if {
    changed := array.concat(fixture, [{"Cmd": "add", "Stage": 3, "Value": ["https://example.com/file", "/app"]}])
    has_code("DF004:") with input as changed
}
test_deny_privileged_port if {
    changed := json.patch(fixture, [{"op": "replace", "path": "/2/Value/0", "value": "80/tcp"}])
    has_code("DF005:") with input as changed
}