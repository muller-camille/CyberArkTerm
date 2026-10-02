# Security Policy

CyberArkTerm handles CyberArk credentials and opens privileged sessions, so security reports are welcome and
taken seriously.

## Supported Versions

Only the latest release receives security fixes. Please update to it before reporting.

| Version | Supported          |
| ------- | ------------------ |
| 0.2.x   | :white_check_mark: |
| < 0.2   | :x:                |

## Reporting a Vulnerability

**Please do not open a public issue.** Report it privately through GitHub:
[Security → Report a vulnerability](https://github.com/muller-camille/CyberArkTerm/security/advisories/new).

Include, if possible:

- the CyberArkTerm version and Windows version;
- the steps to reproduce, or a proof of concept;
- the impact you see (what an attacker could read, change or execute).

The report is acknowledged and investigated. If it is confirmed, a fix is released in a new version and a
GitHub security advisory is published, crediting you unless you prefer otherwise. If it is declined, you get
an explanation.

## Scope

In scope: the CyberArkTerm code in this repository (PVWA client, PSM / PSMP connections, SSH terminal,
SFTP / SCP file transfers, settings storage, release executables).

Out of scope, to be reported to their maintainers:

- vulnerabilities in CyberArk products (PVWA, PSM, PSMP) → CyberArk;
- vulnerabilities in [SSH.NET](https://github.com/sshnet/SSH.NET) or .NET → their projects.

The security measures built into the application are described in the
[README](README.en.md#security).
