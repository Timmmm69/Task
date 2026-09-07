# OPS-02 production-like network/TLS validation report — 1.0.0

Result: PASS. OPS-02 complete. Base commit: f9623e4d0e9191abf70d39982dab9c722dd8d82e.

All 9 foundation checks passed. Docker Engine
29.6.1 reproduced authoritative DNS and the isolated three-network
topology. A temporary Linux network namespace accepted and removed the deny-by-default INPUT and
DOCKER-USER rules while retaining before/after exports.

Two independent synthetic Docker-in-Docker runs generated fresh CA/credentials and volumes. Both
initialized PostgreSQL, applied migration version 13, started API/Worker/TLS proxy, returned HTTPS
readiness with HSTS, connected to PostgreSQL using verify-full/TLS 1.3, rejected plaintext DB
traffic, untrusted CA, wrong SAN and TLS 1.1, rotated the edge certificate, rejected the deliberately
bad rotation, rolled back, and exposed no host port except 127.0.0.1:18443.

The evidence and package contain no private key or reusable credential. All addresses, names,
certificates and credentials used by acceptance were disposable synthetic values; no company data
or company infrastructure was used or is required.
