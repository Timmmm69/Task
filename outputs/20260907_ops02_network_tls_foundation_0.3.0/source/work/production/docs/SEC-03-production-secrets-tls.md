# SEC-03 — production secrets and TLS

Status: source/deployment contract implemented. A release is cleared only after the same gate is
run against the real secret bundle and the deployed HTTPS endpoint; repository evidence alone is
not a customer-deployment attestation.

## Security decision

The system of record for production secrets is the company's authenticated secrets manager. Its
host agent renders a short-lived, owner-readable file bundle below `TASK_SECRET_ROOT` immediately
before deployment. Docker Compose, `.env`, CI variables, image layers, command lines, tickets and
the repository are not secret stores. The rendered directory is a delivery cache on an encrypted
host volume or tmpfs and is removed when the host is decommissioned.

This vendor-neutral boundary is deliberate: the application consumes files and does not receive a
Vault/AD/PKI token. A deployment record must name the approved manager, secret paths/versions,
machine identity and owners without recording values. If the company has not selected and operated
such a manager, `SEC-03` remains blocked; a local folder alone is not an acceptable substitute.

The certificate authority is the corporate CA or an approved internal CA whose root is deployed to
Windows clients by managed policy. Production self-signed leaf certificates are forbidden. The CA
must issue separate server-auth certificates for the employee-facing Task DNS name and PostgreSQL
DNS name, publish revocation information according to corporate policy and keep its signing key
outside the Task hosts.

## Deployed topology

`deployment/security/compose.production.yaml` is the normative topology:

- only `tls-proxy:8443` is published as the configured host address/port (normally TCP 443);
- the API has no host port and accepts clear-text HTTP only on the internal `application-edge`
  network from the proxy;
- PostgreSQL has no host port and only joins the internal `database` network;
- API, worker and migrator use Npgsql `SSL Mode=VerifyFull`, the approved CA bundle and an
  owner-readable `Passfile`; no database password is placed in process environment;
- PostgreSQL enables TLS 1.2+ and `pg_hba.conf` rejects every non-TLS TCP connection;
- the proxy permits TLS 1.2/1.3, disables session tickets, emits HSTS and logs `$uri`, not query
  strings;
- deployable image values must be immutable registry references containing `@sha256:<digest>`.

The reverse-proxy-to-API hop is the documented TLS termination boundary. It is isolated on an
internal Docker network and is not routable from the employee VLAN. Moving the API to another host
requires TLS on that hop and a new security review.

## Secret bundle

The agent creates this structure outside the checkout. Secret values are never printed by the
verification or rotation procedure.

```text
TASK_SECRET_ROOT/
  edge/
    tls.crt                 public leaf + intermediates
    tls.key                 private key, uid 101, mode 0400
    ca-chain.pem            trust chain used by the deployment gate
  database/
    postgres.crt            public leaf + intermediates
    postgres.key            private key, PostgreSQL uid, mode 0400
    postgres-ca.pem         approved root/intermediate bundle
    postgres-admin-password owner-readable; PostgreSQL bootstrap only
    task-migration.pgpass   postgres:5432:<db>:task_migration:<secret>, mode 0400
    task-runtime.pgpass     postgres:5432:<db>:task_runtime:<secret>, mode 0400
  identity/
    signing-current.pem     ECDSA P-256 private key, API uid, mode 0400
    password-pepper         at least 32 random non-whitespace characters, API uid, mode 0400
    verification/
      <kid>.pem             one or two public ECDSA P-256 SPKI keys
```

Public certificates/CA files may be mode 0444. Directories are traversable only by the deployment
account and the relevant service identity. Because file-backed Compose mounts do not remap
ownership/mode, the host agent must set the real ACL/UID before `docker compose up`; YAML `mode`
fields are not a substitute. Each service receives only its own bind mounts.

## First deployment

1. Create a dedicated deployment host, encrypted storage and service identities. Deny interactive
   users access to `TASK_SECRET_ROOT`; enable secret-manager access/audit logs without values.
2. Reserve the Task DNS name and a separate PostgreSQL DNS name (`postgres` in the supplied
   Compose network). Generate CSRs outside the repository:

   ```powershell
   ./work/production/deployment/security/New-TaskTlsCertificateRequest.ps1 `
     -Purpose edge -DnsName task.company.internal -OutputDirectory <secure-staging-directory>
   ./work/production/deployment/security/New-TaskTlsCertificateRequest.ps1 `
     -Purpose database -DnsName postgres -OutputDirectory <secure-staging-directory>
   ```

   Submit only `.csr` files to the approved CA. Import issued leaves/chains into the secrets
   manager and destroy secure staging after verified import.
3. Generate independent random database credentials, JWT P-256 key material and password pepper
   in the manager. Follow `jwt-key-management.md` for the exact JWT key-ring contract. The pepper
   is not routinely rotated; replacement requires a password rehash/reset migration.
4. Render the bundle with service-specific owners and permissions. Copy
   `production.env.example` to the deployment system and fill only non-secret values. Resolve every
   image to a reviewed immutable digest.
5. Start PostgreSQL. Using the admin passfile over the local socket, create/rotate
   `task_migration` and `task_runtime` with
   `deployment/containers/sql/initialize-validation-roles.sql`; never place passwords in shell
   history. Run the migrator `status`, `apply`, then `grant-runtime.sql`, and require final `Ready`.
6. Before starting API traffic, validate the real bundle:

   ```powershell
   ./work/production/verification/Test-ProductionSecretsTls.ps1 `
     -EnvironmentFile <deployment-env-file> `
     -SecretRoot <external-root> `
     -ExpectedServerName task.company.internal `
     -ExpectedDatabaseName postgres `
     -EvidenceDirectory <protected-evidence-directory>
   ```

7. Start `task-api`, `task-worker` and `tls-proxy`. The host firewall allows TCP 443 only from the
   approved employee/VPN CIDRs and management traffic only from the operations network. It denies
   employee access to 5432, 8080 and every Docker bridge subnet.
8. Install the corporate root/intermediate chain on managed Windows clients. Repeat the complete
   gate command from step 6 with `-Endpoint https://task.company.internal`; it must reach
   `/health/ready` with normal trust, no bypass and HSTS. Capture firewall rules, container port
   inventory and certificate metadata in the protected deployment evidence. Do not capture private
   paths or values.

## Rotation

### Edge or PostgreSQL certificate

Start renewal at least 30 days before expiry; alert at 45/30/14/7 days.

1. Generate a new key and CSR with the script. Never reuse a private key.
2. Obtain a server-auth leaf with the same approved DNS SAN and verify CA issuance/revocation data.
3. Write new files beside the active files in `TASK_SECRET_ROOT`, with final owner/mode. Run the
   gate against a temporary complete bundle before activation.
4. Atomically replace `tls.crt` then `tls.key` within the mounted directory. Reload the TLS proxy
   (`nginx -s reload`) or perform a one-at-a-time container replacement. For PostgreSQL, atomically
   replace `postgres.crt`/`postgres.key` and perform a controlled restart; verify API readiness.
5. Run the live gate from a managed client. Record old/new SHA-256 thumbprints, validity, issuer,
   change approval and timestamps. Retain the old key only for the approved rollback window, then
   delete it from the delivery cache and revoke it when compromise is suspected.

A reload/restart that fails validation is rolled back immediately. Never add a certificate bypass
to the desktop or weaken `VerifyFull` during an incident.

### Database password

Use a dual-principal rotation to avoid a shared-password cutover: create a temporary successor role
with the same explicit grants, render its new passfile, restart one consumer at a time and prove
readiness, then disable and drop the predecessor. For the fixed role names, use a short maintenance
window: change the database password from an owner-readable file through local administration,
atomically replace the passfile, restart the affected consumers and verify. Rotation is at least
quarterly and immediately after suspected exposure. Migration and runtime credentials are never
the same.

### JWT signing key and password pepper

JWT rotation is the bounded current/previous procedure in `jwt-key-management.md`; wait at least
the maximum access-token lifetime plus clock skew before removing the previous public key. A pepper
cannot be swapped independently because existing hashes depend on it; use an approved rehash/reset
migration or treat replacement as a credential reset incident.

## Release evidence and stop conditions

The source contract gate is run in CI. Production sign-off additionally requires:

- `checks.json` from the real bundle and live endpoint (no ephemeral test thumbprints);
- secret-manager audit event IDs and versions, without values;
- CA chain, SAN, EKU, expiry and SHA-256 thumbprints;
- `docker compose config` and runtime port/network inventory showing only approved TCP 443;
- host firewall export and a scan from employee and management VLANs;
- successful API/worker readiness, DB TLS inspection and post-rotation rollback exercise.

Stop deployment on an unpinned image, secret inside the checkout/environment/command line, broad
secret ACL, certificate mismatch/expiry, untrusted CA, non-TLS DB connection, published 5432/8080,
failed readiness or missing evidence. Source tests cannot waive these stop conditions.
