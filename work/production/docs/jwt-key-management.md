# JWT signing key and pepper operations

Status: SEC-01 production key-material baseline 1.0.0.

## Runtime contract

`Task.Api` accepts identity key material only through external `file:` references. No private key,
pepper or reusable token belongs in tracked configuration, an image layer, the database, logs or
audit metadata. A configured API validates all identity options and key material during startup and
does not begin serving when validation fails.

The active signing key is an ECDSA P-256 private PEM file. Its file name without extension is the
JWT `kid`. `VerificationKeysDirectory` contains exactly one or two ECDSA P-256 public SPKI PEM
files named `<kid>.pem`: the active key and, during rotation, at most one previous key. The private
key must not be placed in that directory. Startup verifies that the active public key exists and
cryptographically matches the private key.

The password pepper file must contain at least 32 non-whitespace characters. Pepper rotation is
not a JWT operation: it requires an approved password rehash/reset migration plan and must not be
performed by replacing the file in place.

## Bounded JWT rotation

1. Generate a new P-256 key pair outside the repository. Grant the API service account read access
   only to the private key; grant no access to interactive users that do not operate the service.
2. Put the new public `<new-kid>.pem` beside the current public key. There must now be exactly two
   public files. Keep the new private key outside the verification directory.
3. Atomically change `Task:Identity:SigningKeyReference` to the new private key and restart the API.
   Startup must pass before traffic is enabled. New access tokens now carry `<new-kid>`.
4. Retain the previous public key for at least the maximum five-minute token lifetime plus the
   30-second validation clock skew and the deployment overlap. Then remove it and restart the API.
5. Run `verification/Test-SecurityGate.ps1` and record the deployment-specific key IDs and restart
   timestamps in the protected operations log. Never record private paths or key material.

Replacing a key file in place without a restart is unsupported because key material is loaded once
per process. A missing active public key, mismatched pair, malformed/private verification file,
empty key ring, ring larger than current + previous, unreadable secret, or short pepper fails startup.

## Required configuration shape

```text
Task:Identity:Issuer=https://task.company.internal
Task:Identity:Audience=task-desktop
Task:Identity:SigningKeyReference=file:<external-private-key-path>
Task:Identity:PepperReference=file:<external-pepper-path>
Task:Identity:VerificationKeysDirectory=file:<external-public-key-directory>
```

The deployment owner remains responsible for approved TLS termination and operating-system ACLs.
The validation gate proves application behavior; it cannot prove host ACLs or the physical handling
of externally mounted secrets.
