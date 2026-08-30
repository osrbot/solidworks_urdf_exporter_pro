# Contributing

**English** | [简体中文](Contributing-zh-CN)

Reproducible issues, tests, documentation, and focused code fixes are welcome. This page documents
existing repository practice; it does not invent a CLA, response-time promise, or branch policy.

## Development Environment

- Windows x64;
- Visual Studio 2017 with `.NET desktop development`;
- .NET Framework 4.8;
- SolidWorks plus matching API Tools/Interop assemblies;
- administrator access may be required for COM registration or SolidWorks debugging.

Open `SW2URDF.sln`. Configure Debug to start `SLDWORKS.exe` from the target SolidWorks installation.

## Build

```powershell
MSBuild.exe SW2URDF\SW2URDF.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 `
  "/p:SolidWorksInstallDir=C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
```

Do not commit proprietary SolidWorks API DLLs into inappropriate locations. The Release script stages
matching inputs from a local installation and records their versions and hashes in provenance.

## Tests

After a Debug build:

```powershell
TestRunner\bin\x64\Debug\net48\TestRunner.exe
```

Filter by test class/name:

```powershell
TestRunner\bin\x64\Debug\net48\TestRunner.exe TestMassPropertyFrameConverter
```

- Pure unit tests must run without SolidWorks.
- Live COM tests require local SolidWorks and primarily use models under `examples`.
- `SW2URDF_TEST_ASSEMBLY` can point tests at an isolated plug-in assembly.
- Live tests may terminate only a SolidWorks process they started themselves.

## Code Boundaries

- Formal Link Tree code lives under `SW2URDF/UI/LinkTreeCanvas` plus its session/store boundaries.
- Do not restore or copy retired standalone implementations under `prototypes`.
- UI, configuration persistence, and URDF output must share canonical Joint types and validation.
- Release SolidWorks COM objects explicitly; preview, cancel, and failure paths must all clean
  temporary bodies and selection state.
- Frame transforms, inertia conventions, and Collision fallback changes need independent tests, not
  screenshots alone.

## Issue Content

Include:

- SolidWorks release and service pack;
- exporter commit or exact installer filename;
- exact reproduction steps;
- expected and actual behavior;
- the smallest redistributable assembly that reproduces the issue;
- logs, `export_report.md`, `inertial_validation.csv`, and `mesh_manifest.csv`;
- for URDF errors, verifiable expected frame/mass/tensor values or a comparison URDF.

A viewer screenshot alone is insufficient evidence of an inertia algorithm defect. Pair it with
exported values, frame definitions, and SolidWorks Mass Properties results.

## Documentation

- `README.md` is the English project entry; `README.zh-CN.md` is the Simplified Chinese entry.
- Detailed behavior lives in paired English and `-zh-CN` Wiki pages.
- Keep Wiki sources under `docs/wiki` and synchronize them to the separate GitHub Wiki repository.
- Keep `CHANGELOG.md` factual; do not record plans as completed work.
- Distinguish conceptual references, code sources, and historical credits.
- Every release candidate body must contain both `## English` and `## 简体中文` sections.

## License

Contributions are published under the repository
[MIT License](https://github.com/osrbot/solidworks_urdf_exporter_pro/blob/master/LICENSE). Preserve the
original copyright notice and license terms.
