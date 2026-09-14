# Changelog

All notable changes are documented here. The product version is defined in
`Directory.Build.props`.

## [1.3.14] - 2026-09-14

### Added

- Normal and pixel boxes can switch between grid and list views per box.
- Installed application picker and Start-menu drop support for normal and mapping
  boxes without copying or moving system files.

## [1.3.13] - 2026-09-14

### Added

- One-click ZIP backup and safe restore to an empty data directory.
- Versioned database backups before schema upgrades.
- Mapping and smart-box broken-reference scanning.
- Diagnostic report generation with user-profile path redaction.
- Windows CI, manual-release approval, Dependabot, secret scanning, private
  vulnerability reporting, issue forms, and contribution/security guides.
- Optional Authenticode signing support in the release pipeline.

### Changed

- Updates now require a published SHA-256 checksum and are limited to explicit
  GitHub-owned asset hosts.
- Full backup copy/archive work and pre-schema database backup work run off the
  UI thread.
- Updater temporary directories are cleaned on a later startup instead of
  blocking the update completion path.
- Windows CI runs Core and App test projects sequentially for reliable WPF
  layout and process integration tests.

## [1.3.12] - 2026-09-14

### Added

- Inbox, smart box, todo workflow, batch operations, import preflight, undo
  history, pinyin search, per-box visual style, and the renewed user manual.

### Changed

- Rebranded the application and package as QHH Desktop Storage Box.
- Reduced startup queries, icon loading, layout writes, and background scans.
- Updated the update endpoint, installer metadata, support links, and branding.
