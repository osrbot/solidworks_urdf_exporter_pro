# SW2-7 Latest preparation

## Decision

Prepare a private GitHub draft for `v20260913`; do not claim public Latest
publication or full candidate acceptance. The request authorizes release work,
but the existing release audit records missing evidence, not merely a missing
button confirmation. Keep the current public Latest until those gaps close.

## Candidate identity

- Source: `a0af2c5a1ea934fe027fb935df94516880c7a33f`.
- Source tree: `93a77efd07036dfd6cf354249b3d3fc4195e9477`.
- Installer: `sw2urdfSetup_20260911_a0af2c5.exe`, 67,694,476 bytes.
- SHA-256: `bf7c25db6779b9eb1d6c4b1bfa1d369f4155e1c9a1799320283a9b2bc245eb0f`.
- Official immutable-worktree provenance: Release x64, 1,466 payload entries;
  plugin/Core/runtime results passed. No diagnostic-issue build markers.
- Build log: 1,195 plugin tests in 326.241 seconds, zero failed/skipped;
  134 Core and 9 MuJoCo integration tests passed.

The existing installer is reused byte-for-byte. This documentation commit is
not its build source. No source code, CAD documents, installation or native
SolidWorks execution is changed by this preparation.

## Included fixes

Since `c1f2e65`: `c0c75d6` decoded legacy XML, `eb2e172` structural migration,
`6d6ee3c` mixed-configuration mass reads, and `a0af2c5` collision STL references.
The bilingual release notes also summarize earlier changes since stable
`v20260906`, without repeating predecessor native tests as candidate results.

## Outstanding evidence

1. Close the existing [mesh-runtime release audit](2026-09-11-mesh-runtime-release-audit.md):
   payload-to-source/license mapping, missing notices and matching source access
   for the bundled native components. Version strings or a generic source ZIP
   are not the evidence requested by that audit. This run does not issue a legal
   compliance verdict or contact upstream on the user's behalf.
2. Record a supported x64 VC++ runtime prerequisite and demonstrate the complete
   worker on clean supported Windows. The existing developer environment has
   runtime dependencies installed; it cannot establish clean-system completeness.
3. Complete exact-candidate native installation/upgrade/uninstall, configuration
   save/reopen, collision preview/export and target-asset acceptance described in
   [the release process](../wiki/Release-Process-zh-CN.md). Predecessor native
   acceptance is useful regression evidence but does not close this item.

If these require changing the installer, build a new immutable candidate and
replace the draft's candidate facts before publication. Do not edit provenance
or rename this installer to imply a new build. No public tag or asset may be
overwritten. The existing draft and diagnostic prerelease remain separate.
