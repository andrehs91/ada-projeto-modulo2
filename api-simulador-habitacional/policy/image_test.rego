package image

import rego.v1

test_allow_nonroot_image if {
    count(deny) == 0 with input as {"Config": {"User": "1654", "Entrypoint": ["dotnet", "app.dll"], "Cmd": null}}
}
test_allow_cmd if {
    count(deny) == 0 with input as {"Config": {"User": "1654:1654", "Entrypoint": null, "Cmd": ["dotnet", "app.dll"]}}
}
test_deny_invalid_users if {
    every user in ["", "root", "0", "0:0", "1654:0", "$APP_UID", "app"] {
        deny["IMG001: a imagem final deve configurar um UID numérico não-root."] with input as {"Config": {"User": user, "Entrypoint": ["dotnet"]}}
    }
}
test_deny_missing_config if { count(deny) == 2 with input as {} }
test_deny_no_command if {
    deny["IMG002: a imagem deve definir Entrypoint ou Cmd."] with input as {"Config": {"User": "1654", "Entrypoint": null, "Cmd": []}}
}