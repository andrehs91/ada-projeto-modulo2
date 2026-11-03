# Assinatura, atestações e evidências da CI

A publicação ocorre somente na branch padrão, fora de pull requests, após os gates.
Cosign 2.6.0 usa assinatura keyless, identidade OIDC exata do workflow, emissor
`https://token.actions.githubusercontent.com` e registro no Rekor. Não são geradas
chaves locais, nem desabilitada a verificação do transparency log.

## Objetos publicados no registry

- Imagem versionada, assinada pela referência imutável `repository@sha256:…`.
- Atestação CycloneDX (`https://cyclonedx.org/bom`).
- Atestação SPDX JSON (`https://spdx.dev/Document`).
- Provenance demonstrativa SLSA v0.2 (`https://slsa.dev/provenance/v0.2`),
  autodeclarada pelo próprio workflow, com commit, Dockerfile, execução e horários
  do build. **Não demonstra builder confiável nem um nível de conformidade SLSA.**
  Os materiais estão incompletos; a reprodutibilidade não é garantida.

A verificação criptográfica e da identidade é feita pelo Cosign. Em seguida,
`scripts/cosign-evidence.mjs` confere o nome do sujeito, digest, tipo do predicate
e a igualdade do predicate com o arquivo aprovado/gerado nesta execução.

## Demonstração de alteração

A CI publica também uma imagem de laboratório com tag terminada em `-tampered`.
Uma LABEL nova modifica a configuração e o digest, sem alterar o conteúdo da imagem
original assinada. O novo digest não recebe assinatura ou atestações e deve ser
rejeitado pelo Cosign. Falhas de rede, autenticação ou timeout não contam como
sucesso do teste. Essa imagem não deve ser implantada; fica no registry como evidência
e pode ser removida posteriormente conforme a política de retenção do laboratório.

O teste demonstra que a assinatura original não cobre conteúdo alterado. Não prova
proteção contra comprometimento do workflow autorizado ou de sua identidade.

## Evidências para revisão

O artefato `cosign-diagnostics-<run_number>-<run_attempt>` guarda referência original,
provenance, envelopes verificados das três atestações, relatórios de conferência,
progresso e referência/erro da imagem alterada. Os logs do Actions registram a
assinatura e sua verificação; os SBOMs aprovados ficam no artefato separado `sboms-*`.
As assinaturas e atestações permanecem no registry, não nas camadas da imagem.
Os artefatos do Actions têm retenção de 14 dias: para evidência de longo prazo,
exporte-os juntamente com os logs e mantenha os objetos do registry.

Policy-controller é opcional na conversa e o projeto final dispensa Kubernetes;
por isso sua instalação não foi acrescentada à CI. A assinatura não substitui os
gates Rego, SBOM/licenças ou Trivy, e não garante ausência de vulnerabilidades.