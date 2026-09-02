import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { inspectOci, compareBuilds } from '../deployment/containers/verify-oci.mjs';

const expected = { target: 'task-api', revision: 'a'.repeat(40), version: '0.5.0', epoch: 1700000000 };
function fixture(change = () => {}) {
  const directory = mkdtempSync(join(tmpdir(), 'task-oci-test-'));
  mkdirSync(join(directory, 'blobs', 'sha256'), { recursive: true });
  function blob(value, mediaType = 'application/vnd.oci.image.manifest.v1+json') {
    const data = Buffer.from(JSON.stringify(value));
    const digest = createHash('sha256').update(data).digest('hex');
    writeFileSync(join(directory, 'blobs', 'sha256', digest), data);
    return { mediaType, digest: `sha256:${digest}`, size: data.length };
  }
  const config = { os: 'linux', architecture: 'amd64', created: '2023-11-14T22:13:20Z',
    config: { User: 'app', Labels: { 'org.opencontainers.image.revision': expected.revision,
      'org.opencontainers.image.version': expected.version } } };
  const provenance = { predicateType: 'https://slsa.dev/provenance/v0.2', subject: [], predicate: {
    invocation: { parameters: { args: { 'build-arg:GIT_SHA': expected.revision,
      'build-arg:VERSION': expected.version, 'build-arg:SOURCE_DATE_EPOCH': String(expected.epoch), target: expected.target } } },
    buildConfig: {}, materials: [{ uri: 'pinned-base' }] } };
  change(config, provenance);
  const image = { ...blob({ config: blob(config), layers: [blob('layer')] }), platform: { os: 'linux', architecture: 'amd64' } };
  if (!provenance.subject.length) provenance.subject.push({ digest: { sha256: image.digest.slice(7) } });
  const attestation = { ...blob({ config: blob({}), layers: [blob(provenance)] }), platform: { os: 'unknown', architecture: 'unknown' },
    annotations: { 'vnd.docker.reference.type': 'attestation-manifest', 'vnd.docker.reference.digest': image.digest } };
  writeFileSync(join(directory, 'oci-layout'), JSON.stringify({ imageLayoutVersion: '1.0.0' }));
  writeFileSync(join(directory, 'index.json'), JSON.stringify({ manifests: [image, attestation] }));
  return { directory, image };
}

test('accepts an image bound to its release provenance and rejects different builds', () => {
  const f = fixture();
  try {
    const result = inspectOci(f.directory, expected);
    compareBuilds(result, result);
    assert.throws(() => compareBuilds(result, { ...result, imageDigest: 'sha256:' + 'b'.repeat(64) }), /differ/);
  } finally { rmSync(f.directory, { recursive: true }); }
});
for (const [name, change, error] of [
  ['revision mismatch', c => { c.config.Labels['org.opencontainers.image.revision'] = 'b'.repeat(40); }, /labels/],
  ['root user', c => { c.config.User = '0'; }, /platform\/user/],
  ['timestamp drift', c => { c.created = '2026-01-01T00:00:00Z'; }, /timestamp/],
  ['wrong subject', (c, p) => { p.subject = [{ digest: { sha256: 'b'.repeat(64) } }]; }, /subject/],
  ['wrong build arguments', (c, p) => { p.predicate.invocation.parameters.args['build-arg:GIT_SHA'] = 'bad'; }, /parameters/],
  ['missing max provenance', (c, p) => { delete p.predicate.buildConfig; }, /mode=max/],
]) {
  test(`rejects ${name}`, () => {
    const f = fixture(change);
    try { assert.throws(() => inspectOci(f.directory, expected), error); }
    finally { rmSync(f.directory, { recursive: true }); }
  });
}
test('rejects a corrupted OCI blob', () => {
  const f = fixture();
  try {
    writeFileSync(join(f.directory, 'blobs', 'sha256', f.image.digest.slice(7)), 'corrupt');
    assert.throws(() => inspectOci(f.directory, expected), /checksum/);
  } finally { rmSync(f.directory, { recursive: true }); }
});
