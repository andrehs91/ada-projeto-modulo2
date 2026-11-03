package sbom

import rego.v1

component := {"name": "demo", "version": "1.0.0", "purl": "pkg:nuget/demo@1.0.0", "licenses": [{"license": {"id": "MIT"}}]}
fixture := {"bomFormat": "CycloneDX", "components": [component]}
spdx_pkg := {"name": "demo", "versionInfo": "1.0.0", "licenseDeclared": "MIT", "licenseConcluded": "NOASSERTION",
    "externalRefs": [{"referenceCategory": "PACKAGE-MANAGER", "referenceType": "purl", "referenceLocator": "pkg:nuget/demo@1.0.0"}]}
spdx_fixture := {"spdxVersion": "SPDX-2.3", "packages": [spdx_pkg]}

result(document) := violations if {
    violations := (deny | warn) with input as document with data.sbom_scope.packages as ["demo"]
}
has_code(violations, code) if { some msg in violations; startswith(msg, code) }
cdx_with(item) := object.union(fixture, {"components": [item]})
spdx_with(item) := object.union(spdx_fixture, {"packages": [item]})

test_missing_license_warns_without_denial if {
    every document in [cdx_with(object.remove(component, ["licenses"])), spdx_with(object.remove(spdx_pkg, ["licenseDeclared", "licenseConcluded"]))] {
        warnings := warn with input as document with data.sbom_scope.packages as ["demo"]
        failures := deny with input as document with data.sbom_scope.packages as ["demo"]
        has_code(warnings, "SBOM005:")
        count(failures) == 0
    }
}

test_allow_cyclonedx if { count(result(fixture)) == 0 }
test_allow_spdx if { count(result(spdx_fixture)) == 0 }
test_allow_permissive_ids if {
    every id in permissive {
        changed := object.union(component, {"licenses": [{"license": {"id": id}}]})
        count(result(cdx_with(changed))) == 0
    }
}
test_deny_known_copyleft_precise if {
    every id in known_copyleft {
        changed := object.union(component, {"licenses": [{"license": {"id": id}}]})
        violations := result(cdx_with(changed))
        has_code(violations, "SBOM007:")
        not has_code(violations, "SBOM005:")
        not has_code(violations, "SBOM006:")
    }
}
test_license_absent_precise if {
    every item in [object.remove(component, ["licenses"]), object.union(component, {"licenses": []}), object.union(component, {"licenses": [{"license": {"id": ""}}]})] {
        violations := result(cdx_with(item))
        has_code(violations, "SBOM005:")
        not has_code(violations, "SBOM006:")
        not has_code(violations, "SBOM007:")
    }
}
test_license_unknown_precise if {
    every licenses in [[{"license": {"name": "custom license"}}], [{"license": {"id": "LicenseRef-Proprietary"}}], [{"license": {"id": "NOASSERTION"}}], [{"license": {"id": "NONE"}}]] {
        violations := result(cdx_with(object.union(component, {"licenses": licenses})))
        has_code(violations, "SBOM006:")
        not has_code(violations, "SBOM005:")
        not has_code(violations, "SBOM007:")
    }
}
test_allow_permissive_expressions if {
    every expression in ["MIT OR Apache-2.0", "MIT AND BSD-3-Clause", "(MIT OR Apache-2.0) AND ISC"] {
        count(result(cdx_with(object.union(component, {"licenses": [{"expression": expression}]})))) == 0
    }
}
test_reject_mixed_expression_precise if {
    violations := result(cdx_with(object.union(component, {"licenses": [{"expression": "MIT OR GPL-3.0-only"}]})))
    has_code(violations, "SBOM007:")
    not has_code(violations, "SBOM005:")
    not has_code(violations, "SBOM006:")
}
test_reject_unreviewed_expression if {
    every expression in ["GPL-2.0-only WITH Classpath-exception-2.0", "MIT WITH LLVM-exception", "MIT OR LicenseRef-Custom", "MIT; Apache-2.0", "AND OR"] {
        has_code(result(cdx_with(object.union(component, {"licenses": [{"expression": expression}]}))), "SBOM006:")
    }
}
test_reject_multiple_licenses if {
    changed := object.union(component, {"licenses": [{"license": {"id": "MIT"}}, {"license": {"id": "GPL-3.0-only"}}]})
    has_code(result(cdx_with(changed)), "SBOM007:")
}
test_invalid_purls if {
    every value in ["", "demo@1.0", "pkg:", "pkg:nuget/", "pkg:nuget/demo with spaces"] {
        has_code(result(cdx_with(object.union(component, {"purl": value}))), "SBOM003:")
    }
}
test_missing_name if { has_code(result(cdx_with(object.remove(component, ["name"]))), "SBOM009:") }
test_missing_version if { has_code(result(cdx_with(object.remove(component, ["version"]))), "SBOM004:") }
test_missing_scope_fail_closed if {
    violations := deny with input as fixture with data.sbom_scope.packages as []
    has_code(violations, "SBOM008:")
}
test_no_application_packages_fail_closed if {
    has_code(result({"bomFormat": "CycloneDX", "components": []}), "SBOM002:")
    has_code(result({"spdxVersion": "SPDX-2.3", "packages": []}), "SBOM009:")
}
test_invalid_document if {
    every document in [{}, {"components": []}, {"bomFormat": "CycloneDX"}, {"spdxVersion": "SPDX-3.0", "packages": []}] {
        has_code(result(document), "SBOM001:")
    }
}
test_undetermined_license_warns_without_denial if {
    every value in ["NOASSERTION", "NONE"] {
        cdx := cdx_with(object.union(component, {"licenses": [{"license": {"id": value}}]}))
        spdx := spdx_with(object.union(spdx_pkg, {"licenseDeclared": value, "licenseConcluded": value}))
        every document in [cdx, spdx] {
            warnings := warn with input as document with data.sbom_scope.packages as ["demo"]
            failures := deny with input as document with data.sbom_scope.packages as ["demo"]
            has_code(warnings, "SBOM006:")
            not has_code(warnings, "SBOM005:")
            count(failures) == 0
        }
    }
}
test_explicit_unknown_license_remains_denied if {
    every value in ["custom license", "LicenseRef-Proprietary"] {
        cdx := cdx_with(object.union(component, {"licenses": [{"license": {"name": value}}]}))
        spdx := spdx_with(object.union(spdx_pkg, {"licenseDeclared": value}))
        every document in [cdx, spdx] {
            failures := deny with input as document with data.sbom_scope.packages as ["demo"]
            warnings := warn with input as document with data.sbom_scope.packages as ["demo"]
            has_code(failures, "SBOM006:")
            count(warnings) == 0
        }
    }
}
test_spdx_absent_precise if {
    has_code(result(spdx_with(object.remove(spdx_pkg, ["licenseDeclared", "licenseConcluded"]))), "SBOM005:")
}
test_spdx_concluded_copyleft if {
    has_code(result(spdx_with(object.union(spdx_pkg, {"licenseConcluded": "GPL-3.0-only"}))), "SBOM007:")
}
test_spdx_missing_purl if { has_code(result(spdx_with(object.remove(spdx_pkg, ["externalRefs"]))), "SBOM003:") }
test_ignore_base_packages_cyclonedx if {
    base := [{"name": "dpkg", "purl": "pkg:deb/ubuntu/dpkg@1.0", "licenses": [{"license": {"id": "GPL-2.0-only"}}]},
        {"name": "ubuntu", "type": "operating-system"}, {"name": "bash"},
        {"name": "Microsoft.NETCore.App.Runtime.linux-x64", "purl": "pkg:nuget/Microsoft.NETCore.App.Runtime.linux-x64@10.0"}]
    count(result(object.union(fixture, {"components": array.concat([component], base)}))) == 0
}
test_ignore_base_packages_spdx if {
    base := [{"name": "dpkg", "licenseDeclared": "GPL-2.0-only"}, {"name": "Microsoft.AspNetCore.App.Runtime.linux-x64"},
        {"name": "ghcr.io/local/api-simulador-habitacional"}]
    count(result(object.union(spdx_fixture, {"packages": array.concat([spdx_pkg], base)}))) == 0
}
test_nested_third_party_dependency if {
    child := object.union(component, {"name": "child", "licenses": [{"license": {"id": "AGPL-3.0-only"}}]})
    parent := object.union(component, {"components": [child]})
    violations := deny with input as cdx_with(parent) with data.sbom_scope.packages as ["demo", "child"]
    has_code(violations, "SBOM007:")
}
test_missing_transitive_dependency if {
    violations := deny with input as fixture with data.sbom_scope.packages as ["demo", "child"]
    has_code(violations, "SBOM009:")
}
