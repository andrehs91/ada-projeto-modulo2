// Renderiza os templates Kubernetes com a imagem da build para validação pelas políticas.
// Preserva os manifestos de origem; não publica imagem nem aplica recursos no cluster.
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { resolve, join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = resolve(root, process.env.CI_OUTPUT_DIR || '.ci');
const image = process.env.CI_IMAGE || 'ghcr.io/local/api-simulador-habitacional:ci-local';
// ACR (inclusive nomes com hash), ECR privado/público e Artifact Registry/GCR.
const approvedRegistry = /^(ghcr\.io|docker\.io|mcr\.microsoft\.com|[a-z0-9]+(-[a-z0-9]+)?\.azurecr\.io|[0-9]{12}\.dkr\.ecr\.[a-z0-9]+(-[a-z0-9]+)+\.amazonaws\.com(\.cn)?|public\.ecr\.aws|([a-z0-9]+(-[a-z0-9]+)+)-docker\.pkg\.dev|(us\.|eu\.|asia\.)?gcr\.io)$/;
if (!approvedRegistry.test(image.split('/')[0]) ||
    !/^[a-z0-9.-]+\/[a-z0-9_.-]+(\/[a-z0-9_.-]+)*:[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,127}$/.test(image) ||
    /:latest$/i.test(image)) {
  throw new Error('CI_IMAGE deve usar GHCR, Docker Hub, MCR, Azure ACR, AWS ECR ou GCP Artifact Registry/GCR e tag explícita diferente de latest.');
}
if (output === root || output === join(root, 'k8s')) {
  throw new Error('A saída não pode sobrescrever os manifestos de origem.');
}
const manifestDir = join(output, 'k8s');
mkdirSync(manifestDir, { recursive: true });
for (const file of ['configmap.yaml', 'deployment.yaml', 'service.yaml']) {
  let content = readFileSync(join(root, 'k8s', file), 'utf8');
  if (file === 'deployment.yaml') {
    const placeholder = 'SUBSTITUA_REGISTRY/api-simulador-habitacional:SUBSTITUA_BUILD_VERSION';
    if (!content.includes(placeholder)) throw new Error('Placeholder de imagem não encontrado.');
    content = content.replaceAll(placeholder, image);
  }
  if (/SUBSTITUA_|seu-registry\//.test(content)) throw new Error(`Placeholder restante em ${file}.`);
  writeFileSync(join(manifestDir, file), content, { mode: 0o600 });
}
console.log(`Manifestos Kubernetes renderizados em ${manifestDir} com a imagem ${image}; backend somente HTTP.`);