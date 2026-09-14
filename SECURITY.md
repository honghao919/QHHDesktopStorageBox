# Security Policy

## Supported versions

Security fixes are provided for the latest published release. Older releases may
not receive fixes.

## Reporting a vulnerability

Please use GitHub's private vulnerability reporting:

https://github.com/honghao919/QHHDesktopStorageBox/security/advisories/new

Do not open a public issue for vulnerabilities involving arbitrary file writes,
path traversal, update verification, credential exposure, or installation
privilege boundaries.

Include the affected version, reproduction steps, impact, and any suggested
mitigation. Do not include personal file contents or credentials.

## Update trust model

Release downloads are restricted to this GitHub repository and GitHub release
asset hosts. A release asset is only applied when a matching SHA-256 checksum is
published. Production releases should also be Authenticode-signed.
