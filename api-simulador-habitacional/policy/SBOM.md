# Controle SBOM e licenças

## Critério de licenças

O exemplo fornecido usa sintaxe Rego antiga e uma linha `rego` solta. A implementação
usa `import rego.v1`, `deny contains ... if` e package `sbom`, selecionado explicitamente
no Conftest. Além de CycloneDX, valida SPDX JSON 2.3.

**Não existe uma lista simples de licenças que sempre obrigam o consumidor a abrir
todo seu software.** As obrigações dependem do uso, modificação, combinação, linking,
distribuição e, em algumas licenças, acesso pela rede. Exemplos:

- GPL é copyleft forte, com obrigações ao distribuir obras abrangidas.
- AGPL acrescenta obrigações em determinados usos de versões modificadas pela rede.
- LGPL permite algumas formas de linking com software proprietário, mediante condições.
- MPL/CDDL possuem escopo por arquivo; EPL também não equivale automaticamente a GPL.
- Programas GPL do sistema operacional numa imagem não tornam automaticamente a API .NET
  uma obra derivada GPL, embora sua redistribuição possa impor obrigações sobre esses programas.

Para cumprir um perfil **conservador sem obrigações de disponibilização de fonte**,
o controle aprova somente IDs permissivos explicitamente revisados em `permissive`.
Todos os demais IDs bloqueiam, incluindo GPL/AGPL (only e or-later), LGPL, MPL, EPL,
CDDL, EUPL, OSL, RPL e SSPL e IDs explícitos novos/customizados.
Metadados de licença ausentes ou indeterminados geram avisos, sem bloquear o gate.
Portanto ele é **mais restritivo** que apenas bloquear copyleft forte, e não declara
que todas essas licenças contaminam automaticamente o software consumidor.

MIT, Apache-2.0, BSD, ISC e demais IDs listados podem ter obrigações de aviso,
atribuição e outras condições: “permitida” não significa “sem obrigações”. Revisão
jurídica continua necessária, inclusive para dependências de aplicações internas.
O escopo em `scope.json` é extraído do `.deps.json` publicado na imagem e inclui
pacotes e projetos. Não há aprovação específica de licença para a aplicação.

## Controles

| Código | Controle |
|---|---|
| SBOM001 | Formato não reconhecido ou estrutura de inventário ausente. |
| SBOM002 | Nenhum pacote do escopo da aplicação no inventário. |
| SBOM003 | Pacote sem PURL reconhecível. |
| SBOM004 | Pacote sem nome ou versão. |
| SBOM005 | Aviso: declaração de licença ausente ou vazia. |
| SBOM006 | Aviso para licença indeterminada; bloqueio para expressão desconhecida ou exceção não revisada. |
| SBOM007 | Licença copyleft bloqueada pelo perfil conservador. |
| SBOM008 | Escopo extraído do `.deps.json` ausente. |
| SBOM009 | Dependência/projeto do escopo não encontrado no SBOM. |

CycloneDX considera `components` inclusive aninhados, `license.id`, `license.name`
e `expression`. Componentes `type: file` são evidências de arquivos e não recebem
exigências de PURL/licença de pacote. SPDX considera `packages`, `versionInfo`,
`externalRefs` do tipo purl e `licenseDeclared`/`licenseConcluded`.
`NOASSERTION` e `NONE` não são licenças aprovadas: quando não há licença real,
geram aviso. Todos os campos com decisões reais precisam ser aprovados.

Expressões `MIT OR Apache-2.0` ou `MIT AND BSD-3-Clause` passam. Expressões contendo
qualquer ID não aprovado reprovam: `MIT OR GPL-3.0-only` é encaminhado à revisão,
mesmo que uma escolha permissiva possa ser juridicamente possível. Exceções `WITH`
também exigem revisão; não são usadas como bypass. A política classifica IDs de forma
conservadora, mas **não implementa um parser completo da gramática SPDX** nem valida
todo o schema CycloneDX/SPDX. PURL é verificado por estrutura básica, não resolução.

## Gerar e validar localmente

Pré-requisitos adicionais: Syft no PATH e a imagem final já construída. Execute
na pasta da solução. No CI Syft está fixado em v1.56.0; a validação local inicial
foi realizada com Syft v1.55.0.

```bash
mkdir -p .ci/sbom
syft scan docker:ghcr.io/local/api-simulador-habitacional:ci-local -o cyclonedx-json=.ci/sbom/sbom.cdx.json -o spdx-json=.ci/sbom/sbom.spdx.json
opa test api-simulador-habitacional/policy -v
bash scripts/validate-sbom.sh
```

Alternativa de validação individual:

```bash
conftest test .ci/sbom/sbom.cdx.json .ci/sbom/sbom.spdx.json --policy api-simulador-habitacional/policy --data .ci/sbom/scope.json --namespace sbom
```

O script reprova caso qualquer SBOM falte/esteja vazio, grava o ID da imagem e
o relatório completo em `.ci/sbom/validation.json`, resume violações e preserva
o código de saída do Conftest. Não modifica nem normaliza licenças para forçar aprovação.

## Resultado e efeito no CI

A imagem inclui pacotes Linux e bibliotecas .NET, mas o gate cobre os nomes de
`libraries` do `.deps.json` (tipos `package` e `project`), não todos os pacotes da base.
Itens do escopo sem licença ou com licença indeterminada geram avisos.
Licenças explícitas não aprovadas continuam reprovando a validação.

Para resolver:
1. Analise `validation.json` e confirme licenças com os fornecedores/NuGet e seus textos.
2. Corrija/enriqueça o SBOM com proveniência verificável quando o scanner não detectar
   a licença; mantenha o original e registre a origem da evidência.
3. Substitua dependências não compatíveis ou escolha uma base menor que reduza os
   pacotes desnecessários. Uma base chiseled/distroless **não garante** aprovação e
   exigirá adaptar a checagem `/bin/sh` em `validate.sh`.
4. Se a organização aceitar obrigações delimitadas, revise formalmente a política
   com exceções rastreáveis. Não remova o gate só para deixar o pipeline verde.

O workflow gera ambos os SBOMs pela action Anchore fixada por commit, desativa
uploads automáticos e envia seis arquivos explícitos após a avaliação:
CycloneDX, SPDX, relatório, ID da imagem, escopo e `.deps.json`.
Os artefatos podem ser reprovados; o status
do job é a referência. Não envia Secret TLS nem todo o diretório `.ci/`.

SBOM não prova ausência de vulnerabilidades, completude de inventário ou licença
efetiva: adicione scanner CVE, revisão jurídica, assinatura/attestation e controle
da imagem por digest. O ID salvo é metadado de rastreio, não assinatura criptográfica.

Referências: https://www.conftest.dev/ ; https://github.com/anchore/syft ;
https://spdx.org/licenses/ ; https://cyclonedx.org/specification/overview/