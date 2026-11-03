# Policy as Code: .NET + Docker + Kubernetes

Este conjunto é um baseline didático para esta API, não uma certificação de segurança.
Combina práticas do **Kubernetes Pod Security Standards (Restricted)** com decisões
da aplicação: .NET 10, UID explícito, probes `/health`, ambiente Production e Service
interno. Não é uma implementação completa do padrão Restricted nem serve, sem
adaptação, para todos os workloads do cluster.

## Arquivos e decisões

| Arquivo / pacote | Verificações |
|---|---|
| `container_build.rego` / `dockerfile` | Bases oficiais ASP.NET/SDK .NET 10, tag explícita ou digest; aliases e índices de estágios; USER final herdado ou explícito não-root; sem ADD; portas >=1024. |
| `image.rego` / `image` | `Config.User` numérico não-root e comando de execução na imagem construída. |
| `kubernetes.rego` / `kubernetes` | Segurança, recursos, imagens, probes, ambiente e exposição, conforme tabela abaixo. |
| `configuration.rego` / `configuration` | Placeholders proibidos e estrutura PEM de Secret TLS, inclusive em `data` base64. |
| `sbom.rego` / `sbom` | CycloneDX/SPDX: inventário não vazio, nome/versão/PURL de pacotes e licenças permissivas revisadas. |
| `*_test.rego` | Testes positivos, negativos, campos ausentes e regressões; entradas artificiais, sem acesso ao Docker ou ao cluster. |

Regras `deny` bloqueiam o CI. Regras `warn` informam melhorias, mas **não bloqueiam**
por padrão. Para bloqueá-las também, adicione `--fail-on-warn` ao Conftest somente
depois de resolver os avisos ou definir exceções revisadas.

### Baseline Kubernetes

| Código | Proteção |
|---|---|
| K001–K002 | `runAsNonRoot: true` e UID numérico >0; herança do Pod, mas override do container prevalece. |
| K003–K005 | Sem elevação de privilégios, sem privileged, raiz somente leitura. |
| K006–K007 | Remover ALL capabilities, não adicionar capabilities; seccomp RuntimeDefault/Localhost. |
| K008–K010 | Sem namespaces do host, sem hostPath, sem token automático de ServiceAccount. |
| K011 | Requests e limits positivos de CPU e memória. |
| K012 | Registry aprovado, tag explícita diferente de latest ou digest sha256 válido. |
| K013 | Startup/readiness/liveness HTTP `/health`, porta declarada não privilegiada. |
| K014–K015 | Production; nomes de variáveis sensíveis devem referenciar Secret. |
| K016–K017 | Sem hostPort/portas privilegiadas; Service ClusterIP. |

São avaliados Pod, Deployment, StatefulSet, DaemonSet, ReplicaSet, Job e CronJob.
Segurança/recursos/imagem também são verificados em initContainers e
ephemeralContainers. Probes não são exigidas para Job/CronJob, initContainers nem
ephemeralContainers. Arquivos devem conter os recursos individualmente: não use
`kind: List` ou `--combine` sem adaptar as políticas.

**Ajustes organizacionais:** restrinja os prefixes aprovados de imagens aos seus
repositórios/owners, em vez de permitir todo GHCR/Docker Hub. Integração com API do
Kubernetes, sidecars, exec probes, outros endpoints, Ingress/LoadBalancer e imagens
sem shell exigem adaptações explícitas. .NET 10 é uma escolha desta aplicação, não
uma regra universal. Digest sem tag não informa a versão .NET: valide provenance/SBOM.

## Um arquivo por política?

Não é obrigatório. OPA agrupa regras pelo `package`, não pelo nome do arquivo.
Neste exemplo há **um arquivo por domínio**, contendo várias regras identificadas
por código, e um arquivo de testes separado por domínio. É simples de estudar.

Para uma organização grande, separe também por controle (`non_root.rego`,
`resources.rego`, etc.), com testes próximos e mesmo package quando fizer sentido.
Evite um arquivo enorme ou um arquivo para cada condição trivial. Mantenha coesão,
mensagens estáveis, responsáveis e exceções com justificativa/prazo.

Os packages são diferentes porque as entradas diferem. Por isso os comandos abaixo
selecionam `--namespace`: `main` é o padrão do Conftest, mas não é usado aqui.
Não use `--all-namespaces` para misturar Dockerfile, image inspect e YAML Kubernetes.

## Executar localmente

Diretório de trabalho: pasta da **solução**, contendo `api-simulador-habitacional.slnx`,
`k8s/` e `scripts/` (um nível acima deste projeto `.csproj`).
Pré-requisitos: .NET SDK 10, Docker Linux ativo, OPA 1.22.0, Conftest 0.71.1,
Node.js 22+ e OpenSSL. No Windows, os comandos Bash completos podem ser executados
no Git Bash; OPA/Conftest/Node/.NET devem estar no PATH. Nenhum cluster é necessário.

### 1. Testar a própria política (não testa a aplicação)

```bash
opa check --strict api-simulador-habitacional/policy
opa test api-simulador-habitacional/policy -v
opa test api-simulador-habitacional/policy --coverage --format=json
# Alternativa para os mesmos testes:
conftest verify --policy api-simulador-habitacional/policy
```

Os testes têm fixtures que imitam as entradas. Um `test_` com `every` cobre múltiplos
casos negativos; o total de testes OPA não é igual ao total de cenários.
`opa test` e `conftest verify` verificam a lógica. `conftest test` verifica arquivos reais.

### 2. Gerar entradas válidas apenas para CI

```bash
node scripts/render-ci.mjs
```

Gera cópias em `.ci/k8s/`, sem modificar os templates em `k8s/`:
- Imagem local: `ghcr.io/local/api-simulador-habitacional:ci-local`.
- Certificado RSA autossinado, SAN do serviço/localhost, validade de um dia e chave
  correspondente; OpenSSL valida certificado/chave e correspondência de chave pública.
- Nenhum segredo de produção, login em registry ou acesso a Kubernetes.

**Não aplique esses arquivos a produção.** O nome GHCR é somente uma tag local: a
imagem não é publicada. A chave é real, porém descartável. `.ci/` está no `.gitignore`
e no `.dockerignore`; não envie esse diretório como artefato de pipeline.
Políticas PEM só verificam estrutura; não comprovam assinatura, confiança ou validade.

### 3. Build e validação completa

```bash
dotnet restore api-simulador-habitacional.slnx
dotnet build api-simulador-habitacional.slnx -c Release --no-restore
docker build --pull -f api-simulador-habitacional/Dockerfile --target final -t ghcr.io/local/api-simulador-habitacional:ci-local .
bash scripts/validate.sh
```

O script executa os testes, gera cobertura em `.ci/reports/`, valida o Dockerfile e
os manifestos renderizados, exige que os templates com placeholders **falhem**,
inspeciona a imagem final e verifica nela a ausência de `/usr/share/dotnet/sdk`.
Essa última checagem usa `/bin/sh`, presente na base ASP.NET deste exemplo; adapte-a
se mudar para uma imagem chiseled/distroless.

Após a execução local, apague a chave temporária:

```bash
node -e "require('node:fs').rmSync('.ci/k8s/secret.yaml', {force:true})"
```

### Validações individuais

```bash
conftest test api-simulador-habitacional/Dockerfile --policy api-simulador-habitacional/policy --namespace dockerfile
conftest test .ci/k8s/ --policy api-simulador-habitacional/policy --namespace kubernetes --namespace configuration
conftest test .ci/reports/image-inspect.json --policy api-simulador-habitacional/policy --namespace image
```

O Dockerfile agora usa `USER 1654`, UID do usuário `app` das bases Linux oficiais .NET,
em vez de `$APP_UID`. Isso permite verificação estática sem adivinhar o valor de ENV
na base. O estágio final herda esse usuário de `base`; os estágios de build podem
usar root. A verificação da imagem final confirma o resultado real.

Avisos esperados no exemplo: bases e imagem por tag e uma réplica.
Em produção, fixe digests e automatize atualização; use >=2 réplicas com PDB e
topology spread, conforme a necessidade e custo; configure TLS no Ingress.

## Aplicar a uma aplicação real

As políticas **não são inseridas no código C# nem no contêiner**. Elas avaliam
artefatos e bloqueiam o fluxo quando há violações.

1. Publique a imagem realmente construída, aprovada e assinada no registry.
2. Renderize os manifestos do ambiente (Helm/Kustomize ou outro processo), substituindo
  a imagem pelo digest publicado. O backend usa apenas HTTP; TLS fica no Ingress.
3. Execute Conftest contra o resultado renderizado e contra a inspeção da imagem
   correspondente. Para Helm/Kustomize, valide o YAML final, não apenas templates.
4. Valide também schema Kubernetes e, em ambiente autorizado, faça um dry-run no servidor.
5. Somente após sucesso, prossiga para `kubectl apply`/GitOps. Este exemplo não faz deploy.

Para validar arquivos já preparados para produção, o comando é:

```bash
conftest test caminho-dos-manifestos-renderizados/ --policy api-simulador-habitacional/policy --namespace kubernetes --namespace configuration
```

**CI não protege contra bypass** (alguém pode aplicar manualmente). Torne este job
obrigatório nas rulesets/branch protection, exija revisão de alterações das políticas
e use admissão no cluster como segunda barreira. Pod Security Admission `restricted`
é uma boa proteção nativa. Para controles Rego customizados use **OPA Gatekeeper**:
estas políticas precisam ser adaptadas para `ConstraintTemplate`/`Constraint`, para a
entrada `input.review.object`, regras `violation` e a versão Rego suportada pela
instalação. Copiar estes `.rego` diretamente com `kubectl apply` **não funciona**.
Gatekeeper não avalia Dockerfiles no cluster; isso continua no CI.

## GitHub Actions

Workflow em `api-simulador-habitacional/.github/workflows/api-simulador-habitacional-ci.yml`.
O GitHub só descobre workflows em `.github/workflows/` **na raiz Git**. Não há raiz
Git identificada neste workspace. Se publicar somente a pasta da solução como repo,
copie/mova o workflow para `.github/workflows/` dessa raiz. Ele detecta solução na
raiz ou em `api-simulador-habitacional/`.

Executa em push, pull_request e workflow_dispatch, com `contents: read` e sem secrets:
1. Renderização dos placeholders, com imagem `ghcr.io/owner/repo/api-simulador-habitacional:sha-COMMIT`
  em `.ci/k8s/`, sem geração de certificados ou Secret TLS para a aplicação.
2. Restore/build .NET Release e build Docker do estágio final.
3. `opa check`, testes Rego, cobertura, Conftest para Dockerfile/Kubernetes/configuração,
   teste negativo dos placeholders e inspeção da imagem construída.
4. Syft gera CycloneDX JSON e SPDX JSON da imagem Docker local, não de uma tag remota.
5. Conftest valida os dois SBOMs; qualquer violação bloqueia o job.
6. Publicação apenas dos SBOMs, ID da imagem e relatório, com retenção de 14 dias,
   inclusive quando a política reprova, para diagnóstico. Não é aprovação para deploy.

Actions estão fixadas por commit; OPA/Conftest por versão com checksum da release.
Os checksums vêm da mesma release: detectam corrupção, mas não substituem uma
verificação independente de assinatura/proveniência. Configure Dependabot/Renovate
para manter actions, imagens, SDK e ferramentas atualizados.

## Limites e próximos controles recomendados

- Rego não descobre CVEs: adicione scanner de imagem/dependências (Trivy/Grype),
  políticas de vulnerabilidades e assinatura (Cosign). Geração SBOM e licença já estão integradas.
- A regra de variáveis sensíveis é heurística por nome. Ela não descobre todo segredo,
  `envFrom`, conteúdos de ConfigMaps, layers nem credenciais no código. Use secret scanning.
- O baseline não verifica todos os campos de schema, limites >= requests, limites de
  capacidade do cluster, network policies, RBAC, TLS externo ou disponibilidade real.
- Os probes só são conferidos estaticamente. A API já possui `/health`; ele verifica
  saúde básica, não dependências nem HTTPS. Adicione testes de runtime e de readiness
  específicos quando houver banco/filas ou outras dependências.
- Segredos inline reais não devem ser commitados, mesmo em base64. Prefira Secret
  Store CSI/External Secrets ou outro gerenciador com RBAC e rotação.

## Referências

- Kubernetes Pod Security Standards: https://kubernetes.io/docs/concepts/security/pod-security-standards/
- Kubernetes Security Checklist: https://kubernetes.io/docs/concepts/security/security-checklist/
- .NET containers: https://learn.microsoft.com/dotnet/core/docker/introduction
- Conftest: https://www.conftest.dev/
- OPA policy testing: https://www.openpolicyagent.org/docs/policy-testing
- Gatekeeper: https://open-policy-agent.github.io/gatekeeper/website/docs/