# Implantação no Kubernetes

## Pré-requisitos

- Configurar a variável de repositório `REGISTRY` no GitHub em
  **Settings > Secrets and variables > Actions > Variables** (ex.: `ghcr.io/owner/repo`
  ou `docker.io/owner`, sem protocolo ou barra final). O CI renderiza
  `SUBSTITUA_REGISTRY` e `SUBSTITUA_BUILD_VERSION` em `.ci/k8s/deployment.yaml`,
  usando `github.run_number.github.run_attempt` como tag da build.
- O CI somente constrói e valida a imagem local; não a publica nem aplica os
  manifestos no cluster. Para implantação, publicar a imagem construída com o
  `Dockerfile` existente e substituir os placeholders em `deployment.yaml` pela
  referência real (preferencialmente fixada por digest). Para registry privado, configurar
  `imagePullSecrets` no Pod conforme o ambiente.

## Objetos

- `configmap.yaml`: substitui `/app/appsettings.json` com montagem somente leitura
  e configura apenas o listener HTTP em 8080.
- `deployment.yaml`: uma réplica para estudos, usuário/grupo 1654 da imagem oficial
  .NET Linux, sem elevação de privilégios, sem capabilities, seccomp padrão,
  filesystem raiz somente leitura e `/tmp` gravável. Não monta token de acesso à
  API do Kubernetes. Requests/limits são valores iniciais para ajustar com medições.
- `service.yaml`: `ClusterIP`, disponível dentro do cluster, com `80 -> 8080`.

Aplicar o ConfigMap, o Deployment e o Service no mesmo namespace.

## Health checks e configuração

Startup, Readiness e Liveness utilizam o path `/health` na porta HTTP 8080.
A startup probe permite aproximadamente 150 segundos para inicializar;
as outras probes começam após seu sucesso. Essa verificação simples não testa
dependências.

Todas as rotas da aplicação atendem por HTTP, sem redirecionamento HTTPS.

A montagem de `appsettings.json` usa `subPath`: alterações no ConfigMap exigem
recriar os Pods (por exemplo, um rollout restart do Deployment).

Variáveis de ambiente ainda têm precedência sobre `appsettings.json`.