# Security Policy

## Report a vulnerability

Do not open a public issue for a suspected vulnerability. Use the repository's
private security-advisory reporting form. If private reporting is unavailable,
email <security@lunaris.digital> with a concise description, affected version
or commit, impact, and reproduction conditions.

The canonical machine-readable security contact is published at
<https://lunaris.digital/.well-known/security.txt>. See the
[Lunaris security policy](https://lunaris.digital/security) for organization-wide
reporting guidance.

Do not include live credentials, customer data, or unrelated personal data.
Use synthetic examples and redact logs before attaching them.

Maintainers will acknowledge a report, assess affected supported versions, and
coordinate remediation and disclosure. Response and remediation times depend on
severity and maintainer availability; this project does not promise a fixed SLA.

## Supported versions

Until the first public release, only the current default branch is evaluated for
security fixes. After release, this section will list supported release lines.
Older and prerelease builds may be asked to reproduce against a supported build.

## Security boundaries

Pack manifests and pack files are untrusted input. Lifecycle scripts execute
with the invoking user's authority only after the selected script policy and
trust checks permit execution. Review a pack and its source before granting
trust. Run Luna with the least filesystem and credential access needed for the
target repository.

Security advisories and fixes will avoid publishing unnecessary exploit detail
before users can update.

See the public [threat model](docs/developer/threat-model.md) for trust
boundaries, existing controls, and residual risks.
