# Changelog

All notable changes are documented here. The product version is defined in
`Directory.Build.props`.

## [Unreleased]

### Added

- Windows CI for restore, Release build, and the full test suite.
- Manual-release workflow with environment approval and optional Authenticode
  signing.
- Versioned database safety backups before schema upgrades.
- One-click ZIP export of the database and normal-box storage.
- Mapping and smart-box broken-reference scanning.
- Dependabot, issue forms, pull request template, contribution guide, and
  security policy.

### Changed

- Updates now require a published SHA-256 checksum before extraction.
- Release downloads are limited to explicit GitHub-owned asset hosts.
- README now documents installation, uninstallation, data locations, privacy,
  recovery, and known limitations.

## [1.3.12] - 2026-09-14

### Added

- Inbox, smart box, todo workflow, batch operations, import preflight, undo
  history, pinyin search, per-box visual style, and the renewed user manual.

### Changed

- Rebranded the application and package as QHH Desktop Storage Box.
- Reduced startup queries, icon loading, layout writes, and background scans.
- Updated the update endpoint, installer metadata, support links, and branding.
