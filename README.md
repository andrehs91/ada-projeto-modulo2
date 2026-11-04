# API Simulador Habitacional — Mapa de ameaças

A aplicação demonstra como controles de dependências, hardening de imagens e execução segura no Kubernetes bloqueiam ou evidenciam riscos antes da publicação.

## Antes, ataque e depois

Para demonstrar o impacto dos controles implementados, foram utilizadas três branches:

| Branch | Papel na demonstração | O que demonstra |
|---|---|---|
| `unsafe` | **Antes e ataque** | Pipeline simples, sem verificações de políticas nem assinatura de artefatos; pacotes vulneráveis e más práticas na criação da imagem e na configuração de execução do container. Sem gates, esses problemas não impedem o avanço do pipeline. |
| `error` | **Depois: controles ativos** | Mantém os problemas da branch `unsafe`, mas introduz os gates de segurança. As reprovações evidenciam o risco e impedem que a execução avance até a publicação/assinatura. |
| `main` | **Depois: correções implementadas** | Vulnerabilidades corrigidas; Pipeline executa todas as verificações, todas passam, imagem é gerada e artefatos são assinados e publicados |

> Os entregáveis solicitados para o projeto estão na pasta **entregaveis**.  
> Foram adicionados os logs das execuções de cada branch e os artefatos gerados na branch main.

## Mapa de ameaças

| Onde um atacante entraria | Ameaça e impacto | Controle que bloqueia ou evidencia | Caso real relacionado |
|---|---|---|---|
| **Dependências e imagem base** | Exploração de componente vulnerável incorporado ao build, com execução de código ou comprometimento da aplicação. | Syft gera SBOMs; Rego confere inventário e licenças; Trivy bloqueia CVEs **HIGH/CRITICAL** em bibliotecas e pacotes do sistema, inclusive sem correção disponível. | **Log4Shell — CVE-2021-44228 (2021):** uma falha no Log4j permitiu execução remota de código em aplicações que usavam a biblioteca vulnerável. Inventário ajuda a localizar componentes afetados; análise de CVEs evidencia versões conhecidamente vulneráveis. |
| **Dockerfile e configuração da imagem** | Uma imagem maliciosa ou aplicação comprometida executa como root, ampliando o impacto de falhas no isolamento. | Rego exige bases .NET aprovadas e UID numérico não-root, proíbe `ADD` e portas privilegiadas; a inspeção da imagem final confere usuário e comando de inicialização. Build multi-stage separa SDK e runtime. | **runc — CVE-2019-5736 (2019):** execução como root em um container controlado pelo atacante podia permitir sobrescrever o binário runc do host e obter root no host. Não-root reduz essa condição de risco; atualizar o runtime continua indispensável. |
| **Manifestos e execução no Kubernetes** | Container comprometido usa privilégios, capabilities, recursos do host ou credenciais do Pod para ampliar acesso e tentar escapar do isolamento. | Rego exige não-root, `privileged: false`, `allowPrivilegeEscalation: false`, `drop: ALL`, seccomp e raiz somente leitura; bloqueia `hostPath` e namespaces de host e exige token de ServiceAccount desabilitado. | **Linux — CVE-2022-0185 (2022):** falha no kernel permitia escalada de privilégios, com risco ao isolamento de containers. Capabilities mínimas e seccomp reduzem exposição; user namespaces e kernel vulnerável exigem atenção, e hardening não substitui patches nos nós. |
| **Registry e distribuição do artefato** | Substituição do conteúdo publicado ou de uma tag por outro digest, sem a aprovação da CI. | Cosign keyless assina o digest e verifica a identidade OIDC; atestações vinculam SBOMs e provenance ao sujeito/digest, com conferência do predicate. | A lição de **SolarWinds (2020)** é também um limite: comprometimento do build autorizado pode produzir artefatos maliciosos legitimamente assinados. Assinatura prova vínculo com uma identidade e conteúdo, não ausência de código malicioso. |

## Controles implementados e limites

- **Dependências:** `policy/sbom.rego` valida inventário e licenças das dependências
  da aplicação identificadas no `.deps.json`. Não detecta CVEs nem comprova que um
  pacote é benigno. Licença indeterminada gera aviso; licenças proibidas reprovam.
- **Vulnerabilidades:** o gate Trivy no workflow analisa bibliotecas e pacotes OS
  da imagem. Pode detectar vulnerabilidades conhecidas; não garante detecção de
  zero-days ou de toda dependência maliciosa.
- **Imagem:** `policy/dockerfile.rego` e `policy/image.rego` conferem o Dockerfile
  e a configuração efetiva da imagem final. Bases aprovadas não são sinônimo de
  bases sem falhas; fixar a base por digest é recomendação, não gate obrigatório.
- **Kubernetes:** `policy/kubernetes.rego` valida os manifestos renderizados antes
  de qualquer deploy. Inclui requests/limits, probes e restrição de registry.
  Esses gates são **verificações de CI**, não um admission controller instalado
  no cluster. Execuções fora da CI requerem enforcement próprio no cluster.
- **Integridade:** assinatura e atestações são verificadas após publicação e apenas
  quando os gates anteriores aprovam. A provenance é autodeclarada e demonstrativa;
  não comprova builder confiável ou nível SLSA.

## Compliance — se a cadeia fosse atacada amanhã

As primeiras evidências seriam o commit, a branch, o identificador da execução e
os logs do GitHub Actions. As saídas de OPA/Conftest indicariam quais políticas
reprovaram; o Trivy identificaria CVE, pacote, versão e severidade quando seu gate
fosse alcançado. Os SBOMs CycloneDX e SPDX registrariam o inventário da imagem,
enquanto os relatórios de inventário/licenças documentariam sua avaliação.

Para uma execução aprovada, o digest identificaria o conteúdo exato publicado.
Assinatura keyless, identidade OIDC e registro no Rekor permitiriam verificar seu
vínculo com o workflow autorizado. As atestações verificadas associariam esse digest
aos SBOMs e à provenance demonstrativa, incluindo commit e execução. Uma mudança de
tag não alteraria retroativamente a assinatura do digest original.

## Referências

- [Log4Shell — CVE-2021-44228 (NVD)](https://nvd.nist.gov/vuln/detail/CVE-2021-44228)
- [runc — CVE-2019-5736 (NVD)](https://nvd.nist.gov/vuln/detail/CVE-2019-5736)
- [Linux — CVE-2022-0185 (NVD)](https://nvd.nist.gov/vuln/detail/CVE-2022-0185)
- [SolarWinds — orientação da CISA, AA20-352A](https://www.cisa.gov/news-events/cybersecurity-advisories/aa20-352a)