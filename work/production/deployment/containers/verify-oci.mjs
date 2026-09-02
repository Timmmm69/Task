import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

export function inspectOci(directory, expected) {
  const read = descriptor => {
    if (!/^sha256:[a-f0-9]{64}$/.test(descriptor.digest)) throw new Error('Invalid OCI digest');
    const data = readFileSync(join(directory, 'blobs', 'sha256', descriptor.digest.slice(7)));
    if (data.length !== descriptor.size || createHash('sha256').update(data).digest('hex') !== descriptor.digest.slice(7)) {
      throw new Error(`OCI blob checksum/size mismatch: ${descriptor.digest}`);
    }
    return data;
  };
  const json = descriptor => JSON.parse(read(descriptor));
  const layout = JSON.parse(readFileSync(join(directory, 'oci-layout')));
  if (layout.imageLayoutVersion !== '1.0.0') throw new Error('Unsupported OCI layout');
  const root = JSON.parse(readFileSync(join(directory, 'index.json')));
  // OCI exporter wraps the image/attestation index in a named layout index.
  const index = root.manifests.length === 1 && root.manifests[0].mediaType.endsWith('index.v1+json')
    ? json(root.manifests[0]) : root;
  const images = index.manifests.filter(d => d.platform?.os === 'linux' && d.platform?.architecture === 'amd64');
  if (images.length !== 1) throw new Error('Expected exactly one linux/amd64 image');
  const descriptor = images[0];
  const manifest = json(descriptor);
  const config = json(manifest.config);
  for (const layer of manifest.layers) read(layer);
  if (config.os !== 'linux' || config.architecture !== 'amd64' || config.config.User !== 'app') {
    throw new Error('Invalid runtime platform/user');
  }
  if (expected.target !== 'task-container-validation') {
    const labels = config.config.Labels;
    if (labels?.['org.opencontainers.image.revision'] !== expected.revision ||
        labels?.['org.opencontainers.image.version'] !== expected.version) throw new Error('Image labels do not match release');
  }
  if (config.created !== new Date(expected.epoch * 1000).toISOString().replace('.000Z', 'Z')) {
    throw new Error(`Image creation timestamp is not SOURCE_DATE_EPOCH: ${config.created}`);
  }
  const attestations = index.manifests.filter(d => d.annotations?.['vnd.docker.reference.type'] === 'attestation-manifest' &&
    d.annotations['vnd.docker.reference.digest'] === descriptor.digest);
  if (attestations.length !== 1) throw new Error('Missing or ambiguous image provenance');
  const attestationManifest = json(attestations[0]);
  json(attestationManifest.config);
  const statements = attestationManifest.layers.map(json);
  const provenance = statements.find(s => s.predicateType === 'https://slsa.dev/provenance/v0.2');
  if (!provenance || !provenance.subject?.some(s => s.digest?.sha256 === descriptor.digest.slice(7))) {
    throw new Error('Provenance subject does not bind the image digest');
  }
  const parameters = provenance.predicate.invocation?.parameters;
  if (parameters?.args?.['build-arg:GIT_SHA'] !== expected.revision ||
      parameters?.args?.['build-arg:VERSION'] !== expected.version ||
      parameters?.args?.['build-arg:SOURCE_DATE_EPOCH'] !== String(expected.epoch) ||
      parameters?.args?.target !== expected.target) throw new Error('Provenance build parameters do not match release');
  if (!provenance.predicate.buildConfig || !provenance.predicate.materials?.length) {
    throw new Error('Expected mode=max provenance with build configuration and materials');
  }
  return { target: expected.target, imageDigest: descriptor.digest, configDigest: manifest.config.digest,
    layerDigests: manifest.layers.map(l => l.digest), manifest, config, index, provenance };
}

export function compareBuilds(first, second) {
  if (first.target !== second.target || first.imageDigest !== second.imageDigest ||
      first.configDigest !== second.configDigest || JSON.stringify(first.layerDigests) !== JSON.stringify(second.layerDigests)) {
    throw new Error(`Independent builds differ: ${first.target}`);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const [directory, expectedPath, outputPath] = process.argv.slice(2);
  const result = inspectOci(directory, JSON.parse(readFileSync(expectedPath)));
  mkdirSync(resolve(outputPath, '..'), { recursive: true });
  writeFileSync(outputPath, JSON.stringify(result, null, 2) + '\n');
  console.log(`${result.target}: ${result.imageDigest}; provenance verified`);
}
