# Security Policy

## Reporting a vulnerability

Do not open a public issue for security-sensitive reports. Send a private
report to the repository owner with:

- affected version
- reproduction steps
- impact assessment
- suggested mitigation, if available

## Secret handling

Never commit GitHub tokens, API keys, signing certificates, credentials or
user media. Release build automation must read credentials from environment
variables or a secret store.
