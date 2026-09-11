# Mesh Runtime Public Release Audit

Date: 2026-09-11. Audited project revision:
`5656c5d77e18faf56d76309af0e5fa83f707b125`.

**Decision: public installer distribution remains blocked.** Draft release
preparation and documentation do not close the prerequisites below. This is
an engineering evidence review, not legal advice or compliance certification.

## Proven Release Identity

The inspected package is `pymeshlab-2025.7.post1-cp311-cp311-win_amd64.whl`.
Its SHA256 is:

```text
3c1b87f3d6e6c8b129311b44f5c75a22df23c969b9d4300843286220a0d2a49c
```

The local archive, project runtime lock, [PyPI release metadata](https://pypi.org/pypi/pymeshlab/2025.7.post1/json)
and [official GitHub release asset digest](https://api.github.com/repos/cnr-isti-vclab/PyMeshLab/releases/tags/v2025.7.post1)
agree. The release was published on 2026-01-30; PyPI records this wheel's
upload at `2026-01-30T08:40:51.288225Z`.

Canonical wheel URL:
<https://files.pythonhosted.org/packages/e6/56/c4383522d9603cca76ad0c1c886014bf6ac4e79df96018e13025c669b068/pymeshlab-2025.7.post1-cp311-cp311-win_amd64.whl>

The exact tag is [v2025.7.post1](https://github.com/cnr-isti-vclab/PyMeshLab/releases/tag/v2025.7.post1),
not the earlier `v2025.7` discovery reference.

| Source | Exact Commit |
| --- | --- |
| [PyMeshLab](https://github.com/cnr-isti-vclab/PyMeshLab/tree/1dc199f9b6c43e58b6db346ba4600866b950b8ae) | `1dc199f9b6c43e58b6db346ba4600866b950b8ae` |
| [src/meshlab](https://github.com/cnr-isti-vclab/meshlab/tree/d876376e3cc4f92d257e248023d82cbac5b03c7d) | `d876376e3cc4f92d257e248023d82cbac5b03c7d` |
| [src/pymeshlab/pybind11](https://github.com/pybind/pybind11/tree/a2e59f0e7065404b44dfe92a28aca47ba1378dc4) | `a2e59f0e7065404b44dfe92a28aca47ba1378dc4` |
| [src/meshlab/src/vcglib](https://github.com/cnr-isti-vclab/vcglib/tree/c94ef4e12e9ea3ae986d9af91005be8328d13719) | `c94ef4e12e9ea3ae986d9af91005be8328d13719` |

GitHub recursive tree APIs establish those gitlinks. No further gitlinks
were returned for the recorded pybind11 and VCGLib trees. This does not
include dependencies fetched by CMake.

## Build Evidence and Limits

[Release run 21508274041](https://github.com/cnr-isti-vclab/PyMeshLab/actions/runs/21508274041)
started at `c3f062acc7758ca8a27ab23a4413f3dca5e5adfc`. Its version-update
step produced `1dc199f9b6c43e58b6db346ba4600866b950b8ae` at
`2026-01-30T07:41:01Z`. Windows CPython 3.11 jobs reported success:

- [Native build](https://github.com/cnr-isti-vclab/PyMeshLab/actions/runs/21508274041/job/61969025966).
- [Wheel build](https://github.com/cnr-isti-vclab/PyMeshLab/actions/runs/21508274041/job/61972792512).
- [Native tests](https://github.com/cnr-isti-vclab/PyMeshLab/actions/runs/21508274041/job/61972792481).
- [Wheel tests](https://github.com/cnr-isti-vclab/PyMeshLab/actions/runs/21508274041/job/61973020526).

The [release workflow](https://github.com/cnr-isti-vclab/PyMeshLab/blob/1dc199f9b6c43e58b6db346ba4600866b950b8ae/.github/workflows/CreateAndTestRelease.yml)
checks out `main` after the version update, recursively initializes
submodules, selects Qt 5.15.2 and uses a shared external-library cache.
The [PyPI upload workflow](https://github.com/cnr-isti-vclab/PyMeshLab/blob/1dc199f9b6c43e58b6db346ba4600866b950b8ae/.github/workflows/UploadLastRelease.yml)
downloads release assets and uploads them with Twine.

The intermediate native and wheel artifacts are now marked expired.
The [PyPI wheel provenance endpoint](https://pypi.org/integrity/pymeshlab/2025.7.post1/pymeshlab-2025.7.post1-cp311-cp311-win_amd64.whl/provenance)
returned HTTP 404. The evidence establishes published release identity,
but does not independently reconstruct all cached inputs or exact compiler
versions. Lack of an attestation is not itself a claim of license failure.

## Materials Not Yet Closed

Read-only archive inspection found 97 DLL/PYD files and only one
license-named file: `pymeshlab-2025.7.post1.dist-info/licenses/LICENSE`.
Keeping that GPL text does not supply other bundled components' notices.

PE version resources identify Qt 5.15.2, Embree 4.3.3, TBB 2021.11,
GMP 5.0.1 and MPFR 3.0.0. `opengl32sw.dll` contains the string
`Mesa 12.0.0-rc2`. These observations do not identify all patches.

- [Qt 5.15.2 complete source archives](https://download.qt.io/archive/qt/5.15/5.15.2/single/) are available; source and applicable bundled notices still need to be included in the release materials.
- [MeshLab external dependency recipes](https://github.com/cnr-isti-vclab/meshlab/tree/d876376e3cc4f92d257e248023d82cbac5b03c7d/src/external) identify additional downloads, including CGAL, lib3mf, Nexus/Corto, U3D, Xerces and libE57Format. Recursive Git checkout alone is not the full source set.
- The [CGAL 5.6 GMP/MPFR auxiliary archive](https://github.com/CGAL/cgal/releases/download/v5.6/CGAL-5.6-win64-auxiliary-libraries-gmp-mpfr.zip) was inspected in memory: its 17 entries contain headers, binaries, README and license files, not complete source. Exact matching source, patches and build recipes remain unverified.
- The [Embree recipe](https://github.com/cnr-isti-vclab/meshlab/blob/d876376e3cc4f92d257e248023d82cbac5b03c7d/src/external/embree.cmake) copies the deployed TBB DLL from the Embree archive. Do not label it 2021.6 from the separately downloaded build dependency; the shipped DLL reports 2021.11.
- [Qt's software OpenGL archive](https://download.qt.io/development_releases/prebuilt/llvmpipe/windows/) lists a Mesa 12.0 rc2 binary package. Exact binary matching and its complete build/source and notice set were not established.

The complete payload-to-source/license inventory and appropriate access
arrangement remain open. The [upstream GPL text](https://github.com/cnr-isti-vclab/PyMeshLab/blob/1dc199f9b6c43e58b6db346ba4600866b950b8ae/LICENSE)
describes Corresponding Source in section 1 and object-code conveyance in
section 6. Generic upstream URLs are not a completed source delivery or a
written source offer. No GPL component is relicensed as MIT by this audit;
worker integration and any combined-work obligations also require review.

## Native Runtime Requirement

The actual wheel metadata requires `numpy` and `msvc-runtime`, matching
[Windows-specific setup.py](https://github.com/cnr-isti-vclab/PyMeshLab/blob/1dc199f9b6c43e58b6db346ba4600866b950b8ae/setup.py).
The two-wheel lock does not supply `msvc-runtime`. Static PE import and
delay-import inspection confirms dependencies on `MSVCP140.dll`,
`MSVCP140_1.dll`, `VCOMP140.dll`, `VCRUNTIME140.dll` and
`VCRUNTIME140_1.dll`. Embedded Python supplies only the latter two.

The current installer detects .NET 4.8 but not VC++ redist. The tested
machine has x64 redist 14.51.36231.0, including all five DLLs. This explains
local success, not clean-system completeness or a proven minimum version.

[Microsoft requires](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)
the Redistributable to match the target architecture and be no earlier
than the MSVC build tools used for the binaries. The exact minimum is not
established here. Select and record a supported x64 runtime, then verify
the complete payload on clean supported Windows. Linking users to the
official prerequisite avoids adding a runtime wheel or redistributing
system DLLs; it does not remove the need to test. Redistribution of
Microsoft binaries requires separate confirmation of applicable terms.

## Smallest Release Closure Plan

1. Keep the proven wheel unchanged. Assemble the pinned recursive source tree, external dependency sources, patches and build/install scripts, with a payload-to-source manifest and hashes. Resolve the outstanding binary source mappings with upstream rather than assuming version strings are sufficient.
2. Add the missing component license/notice materials. Publish the verified source materials with clear directions alongside the installer and maintain the required access. Do not present an incomplete source ZIP as complete Corresponding Source.
3. Document an explicit supported Microsoft x64 runtime prerequisite and pass clean-system import, reduction and worker protocol checks. Installer detection can be a separate change; introducing it requires a new tested installer.
4. Keep public installer publication blocked until those items are closed. A draft release or documentation deployment is not installer approval.

## Artifact Boundary

This audit and notice update affect source-worktree documents only. They
do not rebuild, replace, or modify the previously tested local installer,
its embedded notices, or its provenance. Documentation-only changes must
not be described as contents of that existing binary. Any subsequent
installer build needs its own source revision, hash and validation evidence.
