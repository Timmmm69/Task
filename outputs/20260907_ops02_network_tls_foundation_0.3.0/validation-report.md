# OPS-02 network/TLS foundation validation report — 0.3.0

Result: STEPS 1–3 IMPLEMENTATION PASS. Base commit: f9623e4d0e9191abf70d39982dab9c722dd8d82e.

## Verified

The machine-readable parameter contract rejected repository-local private-key output and mutable
DNS images. A synthetic RSA root CA issued separate edge/PostgreSQL server-auth certificates; the
gate verified SAN, EKU, lifetime, chain and private-key match. It generated an authoritative DNS
zone, a hardened pinned CoreDNS Compose definition and parameterized production subnets.

All 9 source/synthetic checks passed. Docker Engine
29.6.1 then reproduced the authoritative DNS answer and three bridge
networks: database/internal, application-edge/internal and frontend/non-internal. The DNS container
used a read-only root filesystem, no-new-privileges and only TCP/UDP 53. Temporary containers and
networks were removed after the test.

The firewall plan permits approved employee/management CIDRs to the explicit HTTPS destination
before denying direct Docker-subnet traffic, ports 5432/8080 and unapproved HTTPS sources. Host
INPUT permits only established traffic plus configured management CIDR/ports on the external
interface. The apply path requires Linux/root, an existing Docker `DOCKER-USER` chain, explicit
lockout acknowledgement, captures pre/post rules and restores the snapshot on failure.

No generated CA key, leaf private key, password or reusable credential is included in this package.

## Validation boundary

The available runtime was Windows Docker Desktop, so live Linux-host iptables mutation was not
performed. The implementation and exact rule ordering are validated, but deployment acceptance
must still capture protected before/after firewall exports on the clean Linux application host.
Application deployment, HTTPS readiness, PostgreSQL `VerifyFull`, TLS negative cases, certificate
rotation/rollback and a second clean-room replay are OPS-02 steps 4–6. Therefore the overall OPS-02
roadmap item remains in progress and is not release-complete.
