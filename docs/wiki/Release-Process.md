# Release Process

**English** | [简体中文](Release-Process-zh-CN)

This process separates source commits, proprietary local API builds, CI verification, manual
SolidWorks validation, and public publication. It prevents untested candidates from becoming public
releases.

## 1. Prepare Source

1. Update `CHANGELOG.md` with completed work only.
2. Add `.github/release-notes/vYYYYMMDD.md` with reviewed `## English` and `## 简体中文` sections.
3. Run relevant pure, serialization, export, layout, and available Live SolidWorks tests.
4. Commit source.
5. Except for ignored/generated `INSTALL/OUTPUT` files, the source worktree must be clean.

## 2. Build the Installer

```powershell
.\scripts\BuildInstaller.ps1 -Configuration Release -Platform x64 `
  -SolidWorksInstallDir "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS" `
  -DotNetPath "C:\Path\To\dotnet-sdk-8.0.424\dotnet.exe" `
  -InnoCompilerPath "C:\Path\To\Inno Setup 6.3.3\ISCC.exe"
```

Hard requirements:

- `Release|x64`;
- exact .NET SDK 8.0.424;
- Inno Setup 6.3.0-6.3.3;
- API inputs matching the local SolidWorks installation;
- locked NuGet CLI, source, SDK package locks, and legacy test-package archive hashes;
- pinned OpenUSD and official MuJoCo runtime inputs with verified hashes;
- resolvable source commit, tree, and commit time.

The script restores dependencies in a detached temporary worktree, stages SolidWorks API inputs,
cleans Release intermediates, builds the production DLL, rejects Python bytecode/cache artifacts,
and rejects source changes during build. The resulting provenance is traceable build evidence; the
process does not claim bit-for-bit reproducibility.

## 3. Artifacts

```text
sw2urdfSetup_YYYYMMDD_<source-commit>.exe
sw2urdfSetup_YYYYMMDD_<source-commit>.exe.sha256
sw2urdfSetup_YYYYMMDD_<source-commit>.exe.provenance.json
```

Provenance records source commit/tree, build mode, tool/dependency hashes, SolidWorks API inputs, and
installed-payload hashes. It is traceability metadata, not:

- an Authenticode signature;
- proof that a GitHub-hosted runner rebuilt against proprietary SolidWorks assemblies;
- certification for every SolidWorks version.

## 4. Artifact Commit

- The artifact commit may contain only `.exe`, `.sha256`, and `.provenance.json`.
- The filename commit must identify the installer's actual source parent.
- Never overwrite an existing immutable candidate.
- Do not mix unrelated CAD files, logs, PDBs, test assemblies, or old-installer deletions into it.

## 5. CI Checks

The workflow validates:

- installer filename to source-commit relationship;
- SHA-256 and provenance;
- locked dependencies and tools;
- extracted Inno payload list and every payload hash;
- existence of reviewed bilingual Release Notes in the source commit;
- immutable daily tag/Release rules.

Hosted CI does not have proprietary SolidWorks API build inputs. It verifies and promotes a trusted
maintainer build instead of rebuilding the plug-in.

## 6. Bilingual Release Notes

`.github/release-notes/vYYYYMMDD.md` is the only Release body source. It must include:

- one non-empty title containing English and Simplified Chinese;
- a non-empty `## English` section with changes, validation scope, limitations, and manual-test gate;
- a non-empty `## 简体中文` section containing the same facts;
- one date placeholder in the bilingual title;
- exactly one installer filename, installer SHA-256, source-commit, and artifact-commit placeholder in
  each language section.

CI replaces both sections' fact placeholders from the same verified artifact/provenance values; it
does not translate or invent changes from `CHANGELOG.md`. Missing or empty language sections,
monolingual titles, and asymmetric fact placeholders fail closed. The GitHub Release title is
bilingual as well. Legacy public Releases are not rewritten; this policy applies to the current
Draft and future candidates.

## 7. Manual Gate

Before invoking the release workflow, the maintainer must validate the exact candidate installer at
least as follows. CI may then verify that committed candidate and create a Draft Release only:

- install/upgrade/uninstall and COM registration;
- Link Tree save, close recovery, and reopen;
- per-Link frame, COM, inertia tensor, and principal moments;
- Collision selection, preview, formal output, and fallback reports;
- complete ROS1/ROS2 URDF and meshes;
- OpenUSD target files plus a passed bundled-runtime reopen report;
- MJCF target files plus a passed official MuJoCo compile/save/reload/one-step report;
- topmost export progress and completion summary;
- deep/hidden Link inertia and Collision previews in live SolidWorks;
- basic production-model loading in the intended viewer/simulator, recorded separately from
  generation and automated runtime checks.

The Release Notes must not claim Isaac Sim/Isaac Lab execution unless that exact application test
was run and recorded. Likewise, the one-step MJCF check is not controller, contact-tuning,
long-horizon, task, performance, or reinforcement-learning validation.

The workflow confirmation means the candidate passed this manual gate; it is not permission to make
the Draft public. This workflow can only create a Draft and contains no publication step. Publishing
requires a separate maintainer-controlled process outside this workflow. A commit message containing
“fixed” is not publication approval.

## 8. Tags and Immutability

- Tags use `vYYYYMMDD`.
- A public daily Release is not moved or overwritten.
- A second same-day candidate must wait for a later date or explicit maintenance policy; it cannot
  replace published assets.
- Release Notes list fixes, features, limits, validation scope, and source commit separately in both
  supported languages.
