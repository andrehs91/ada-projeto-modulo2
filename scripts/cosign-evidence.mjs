import { readFileSync, writeFileSync } from 'node:fs';
import { isDeepStrictEqual } from 'node:util';
import { pathToFileURL } from 'node:url';

const predicateTypes = {
  cyclonedx: 'https://cyclonedx.org/bom',
  spdxjson: 'https://spdx.dev/Document',
  slsaprovenance: 'https://slsa.dev/provenance/v0.2',
};

export function generateProvenance(env) {
  for (const key of ['GITHUB_SERVER_URL', 'GITHUB_REPOSITORY', 'GITHUB_SHA', 'GITHUB_WORKFLOW_REF',
    'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'CI_IMAGE', 'SIGNED_IMAGE', 'BUILD_STARTED_ON', 'BUILD_FINISHED_ON']) {
    if (!env[key]) throw new Error(`Variável obrigatória ausente: ${key}`);
  }
  const source = `${env.GITHUB_SERVER_URL}/${env.GITHUB_REPOSITORY}`;
  return {
    builder: { id: `${source}/actions/runs/${env.GITHUB_RUN_ID}/attempts/${env.GITHUB_RUN_ATTEMPT}` },
    buildType: `${source}/demo-build/v1`,
    invocation: {
      configSource: {
        uri: `git+${source}`,
        digest: { sha1: env.GITHUB_SHA },
        entryPoint: env.GITHUB_WORKFLOW_REF.split('@')[0],
      },
      parameters: {
        image: env.CI_IMAGE,
        signedImage: env.SIGNED_IMAGE,
        dockerfile: 'api-simulador-habitacional/Dockerfile',
        target: 'final',
        demonstrationOnly: true,
        disclaimer: 'Provenance autodeclarada e demonstrativa; não produzida por builder SLSA confiável.',
      },
      environment: { runner: 'GitHub Actions', runAttempt: env.GITHUB_RUN_ATTEMPT },
    },
    metadata: {
      buildInvocationID: `${source}/actions/runs/${env.GITHUB_RUN_ID}/attempts/${env.GITHUB_RUN_ATTEMPT}`,
      buildStartedOn: env.BUILD_STARTED_ON,
      buildFinishedOn: env.BUILD_FINISHED_ON,
      completeness: { parameters: false, environment: false, materials: false },
      reproducible: false,
    },
    // Commit conhecido; imagens base e todas as dependências não foram rastreadas aqui.
    materials: [{ uri: `git+${source}`, digest: { sha1: env.GITHUB_SHA } }],
  };
}

// A identidade é validada pelo Cosign ANTES desta conferência de conteúdo.
export function validateStatements(type, envelopes, expectedPredicate, imageRef) {
  const match = /^(.*)@sha256:([a-f0-9]{64})$/.exec(imageRef ?? '');
  if (!match || !predicateTypes[type]) throw new Error('Imagem por digest ou tipo inválido.');
  if (!Array.isArray(envelopes) || !envelopes.length) throw new Error('Nenhuma atestação verificada.');
  const statements = envelopes.map(envelope => {
    if (envelope.payloadType !== 'application/vnd.in-toto+json') throw new Error('Envelope não é in-toto.');
    const statement = JSON.parse(Buffer.from(envelope.payload, 'base64').toString('utf8'));
    if (statement._type !== 'https://in-toto.io/Statement/v0.1' || statement.predicateType !== predicateTypes[type]) {
      throw new Error('Statement ou predicateType inesperado.');
    }
    if (!Array.isArray(statement.subject) || statement.subject.length !== 1 ||
      statement.subject[0].name !== match[1] || statement.subject[0].digest?.sha256 !== match[2]) {
      throw new Error('Sujeito/digest não corresponde à imagem publicada.');
    }
    return statement;
  });
  if (!statements.some(statement => isDeepStrictEqual(statement.predicate, expectedPredicate))) {
    throw new Error('Predicate não corresponde ao arquivo aprovado nesta execução.');
  }
  return { imageRef, predicateType: predicateTypes[type], verifiedStatements: statements.length,
    subjectAndDigestValidated: true, approvedPredicateMatched: true };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [operation, ...args] = process.argv.slice(2);
  let output;
  let result;
  if (operation === 'generate' && args.length === 1) {
    [output] = args;
    result = generateProvenance(process.env);
  } else if (operation === 'verify' && args.length === 4) {
    const [type, verifiedFile, predicateFile, outputFile] = args;
    output = outputFile;
    // Cosign 2.x pode escrever vários envelopes JSON, um por linha.
    const text = readFileSync(verifiedFile, 'utf8').trim();
    let envelopes;
    try {
      const parsed = JSON.parse(text);
      envelopes = Array.isArray(parsed) ? parsed : [parsed];
    } catch {
      envelopes = text.split(/\r?\n/).filter(Boolean).map(line => JSON.parse(line));
    }
    result = validateStatements(type, envelopes, JSON.parse(readFileSync(predicateFile, 'utf8')), process.env.SIGNED_IMAGE);
  } else {
    throw new Error('Uso: cosign-evidence.mjs generate arquivo | verify tipo envelopes predicate relatório');
  }
  writeFileSync(output, `${JSON.stringify(result, null, 2)}\n`);
  console.log(`Evidência ${operation}: ${output}`);
}