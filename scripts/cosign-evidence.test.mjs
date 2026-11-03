import { test } from 'node:test';
import assert from 'node:assert/strict';
import { generateProvenance, validateStatements } from './cosign-evidence.mjs';

const repository = 'ghcr.io/example/api';
const digest = 'a'.repeat(64);
const imageRef = `${repository}@sha256:${digest}`;
const predicate = { bomFormat: 'CycloneDX', components: [] };
function envelope(overrides = {}) {
  const statement = {
    _type: 'https://in-toto.io/Statement/v0.1',
    subject: [{ name: repository, digest: { sha256: digest } }],
    predicateType: 'https://cyclonedx.org/bom',
    predicate,
    ...overrides,
  };
  return { payloadType: 'application/vnd.in-toto+json', payload: Buffer.from(JSON.stringify(statement)).toString('base64') };
}

test('aceita sujeito, digest, tipo e predicate aprovados', () => {
  assert.equal(validateStatements('cyclonedx', [envelope()], predicate, imageRef).approvedPredicateMatched, true);
});
test('rejeita digest alterado', () => {
  assert.throws(() => validateStatements('cyclonedx', [envelope({ subject: [{ name: repository, digest: { sha256: 'b'.repeat(64) } }] })], predicate, imageRef), /Sujeito\/digest/);
});
test('rejeita sujeito de outro repositório', () => {
  assert.throws(() => validateStatements('cyclonedx', [envelope({ subject: [{ name: 'ghcr.io/other/api', digest: { sha256: digest } }] })], predicate, imageRef), /Sujeito\/digest/);
});
test('rejeita predicateType diferente', () => {
  assert.throws(() => validateStatements('cyclonedx', [envelope({ predicateType: 'https://spdx.dev/Document' })], predicate, imageRef), /predicateType/);
});
test('rejeita predicate diferente do SBOM aprovado', () => {
  assert.throws(() => validateStatements('cyclonedx', [envelope({ predicate: { ...predicate, components: ['unexpected'] } })], predicate, imageRef), /Predicate não corresponde/);
});
test('rejeita ausência de atestações e referência por tag', () => {
  assert.throws(() => validateStatements('cyclonedx', [], predicate, imageRef), /Nenhuma atestação/);
  assert.throws(() => validateStatements('cyclonedx', [envelope()], predicate, `${repository}:v1`), /digest ou tipo inválido/);
});
test('confere os três tipos de atestação', () => {
  for (const [type, predicateType] of Object.entries({ spdxjson: 'https://spdx.dev/Document', slsaprovenance: 'https://slsa.dev/provenance/v0.2' })) {
    assert.equal(validateStatements(type, [envelope({ predicateType })], predicate, imageRef).predicateType, predicateType);
  }
});
test('provenance registra commit e execução sem alegar garantia SLSA', () => {
  const env = {
    GITHUB_SERVER_URL: 'https://github.com', GITHUB_REPOSITORY: 'example/api', GITHUB_SHA: 'c'.repeat(40),
    GITHUB_WORKFLOW_REF: 'example/api/.github/workflows/ci.yml@refs/heads/main', GITHUB_RUN_ID: '123',
    GITHUB_RUN_ATTEMPT: '2', CI_IMAGE: `${repository}:1.2`, SIGNED_IMAGE: imageRef,
    BUILD_STARTED_ON: '2026-11-03T10:00:00Z', BUILD_FINISHED_ON: '2026-11-03T10:01:00Z',
  };
  const provenance = generateProvenance(env);
  assert.equal(provenance.invocation.parameters.demonstrationOnly, true);
  assert.equal(provenance.materials[0].digest.sha1, env.GITHUB_SHA);
  assert.equal(provenance.metadata.completeness.materials, false);
  assert.equal(provenance.metadata.reproducible, false);
  assert.ok(provenance.metadata.buildInvocationID.endsWith('/123/attempts/2'));
  assert.throws(() => generateProvenance({}), /Variável obrigatória/);
});