# Mesh runtime source-material collection

Date: 2026-09-13. Scope: the existing PyMeshLab
`2025.7.post1-cp311-cp311-win_amd64` wheel. This review adds concrete source
and notice materials to the 2026-09-11 audit. It is an engineering evidence
report, not a legal compliance verdict or a publication approval.

## Material delivered locally

The directory `.codex-build/source-materials` contains 28 original source
archives totaling **1,210,207,385 bytes**, their download URLs, SHA256 and
recipe MD5 checks, the 97-file native payload inventory, original license
and notice files, and acquisition/inspection scripts.

- `source-archive-manifest.json`: all 28 source archives, URL, SHA256,
  size, and matching pinned CMake recipe where an upstream hash exists.
- `native-payload-manifest.json`: every DLL/PYD path and SHA256 in the
  actual wheel, with a source-family mapping.
- `prebuilt-matches.json`: original archive/member identity and hashes
  for the five prebuilt dependency DLLs compared below.
- `notice-manifest.json` and `notices/`: collected original notice and
  license text, preserving archive/member identities. Qt extraction covers
  the shipped qtbase, qtsvg and qtimageformats modules; the complete Qt
  source archive retains every module's notices.
- `README.md`: archive layout, source reconstruction instructions,
  version distinctions and remaining provenance limits.

The source archive is
`sw2urdf-mesh-source-materials-20260913.zip`, **1,214,417,657 bytes**, SHA256
`f9d98e4cfa19048c821db8363104d263e2ad6d1ee8b00469a862404684b7156e`.
Its checksum is also recorded in `bundle-sha256.txt`. The bundle contains
28 source archives and 384 original notice/attribution files. The materials are intentionally
named source materials, rather than certified complete Corresponding Source.
The wheel and original binary dependency archives are retained locally
as evidence and are excluded from the source bundle.

## Closed collection gaps

The following exact source identities are now downloaded, rather than
represented only by links:

| Component | Source identity |
| --- | --- |
| PyMeshLab | 1dc199f9b6c43e58b6db346ba4600866b950b8ae |
| MeshLab | d876376e3cc4f92d257e248023d82cbac5b03c7d |
| pybind11 | a2e59f0e7065404b44dfe92a28aca47ba1378dc4 |
| VCGLib | c94ef4e12e9ea3ae986d9af91005be8328d13719 |
| Qt | 5.15.2 complete source distribution |
| Boost / CGAL | 1.87.0 / 5.6 |
| libE57Format / Xerces-C | 3.1.1 / 3.2.4 |
| Levmar / lib3ds / lib3mf | 2.6.1 / 1.3.0 / 2.4.1 with submodules |
| libigl / muparser | 2.6.0 / 2.3.5 |
| Nexus / Corto | 2025.05 / 2025.07 |
| OpenCTM / Qhull / StructureSynth | 1.0.3 / 2020.2 / 1.5.1 |
| tinygltf / U3D | 2.6.3 / 1.5.2 |
| Embree | 4.3.3 |
| TBB | 2021.11.0 deployed binary; 2021.6.0 separate build dependency |
| GMP / MPFR base releases | 5.0.1 / 3.0.0 |
| Mesa / LLVM base releases | 12.0.0-rc2 / 3.6.2 |

The [Qt complete source archive](https://download.qt.io/archive/qt/5.15/5.15.2/single/qt-everywhere-src-5.15.2.tar.xz)
is 586,690,220 bytes and matches the [official SHA256](https://download.qt.io/archive/qt/5.15/5.15.2/single/qt-everywhere-src-5.15.2.tar.xz.sha256):
`3a530d1b243b5dec00bc54937455471aaa3e56849d2593edb8ded07228202240`.
It includes ANGLE and Qt's other vendored dependencies.

All downloaded external source dependencies with a recorded recipe MD5
have a matching archive in the collected materials. In particular, the live tinygltf GitHub tag ZIP
did **not** match the pinned recipe. The recipe's
[MeshLab mirror](https://www.meshlab.net/data/libs/tinygltf-2.6.3.zip)
does match `f63ab0fb59e5de059d9719cc14057dc8` and is the included archive.
This avoids silently substituting a different source archive.

MeshLab's source archive also includes EasyExif 1.0 and GLEW 2.2.0;
VCGLib contains Newuoa and other bundled implementations. The full
original source trees retain build scripts and inline notices. The
[pinned external recipes](https://github.com/cnr-isti-vclab/meshlab/tree/d876376e3cc4f92d257e248023d82cbac5b03c7d/src/external)
are copied into the bundle as build evidence.

## Exact binary-origin comparisons

These comparisons read the actual wheel and original upstream archives;
all five hashes are equal. They establish original binary origins, not
bit-for-bit source reproducibility.

| Wheel file | Matching original archive | SHA256 |
| --- | --- | --- |
| libgmp-10.dll | CGAL 5.6 win64 auxiliary libraries | fec43cb18ce0ec8a5dd6ad1db745747167310ccb92b5acff0d445a8b3013f009 |
| libmpfr-4.dll | CGAL 5.6 win64 auxiliary libraries | 09b3b868b96433991fc15c9c5ae6f9a44c62d2e21194110442607917391ed927 |
| embree4.dll | Embree 4.3.3 x64 Windows | 967ea8c40c64f060bf697942a4ed44b8c19a9cab24585b0cf1ed6e690ff7c175 |
| tbb12.dll | Embree 4.3.3 x64 Windows | 68c07bc7c2161637f37f11ebf4af5be2f940138cbcaa717bb71282fd55a827ef |
| opengl32sw.dll | Qt opengl32sw-64-mesa_12_0_rc2.7z | 963641a718f9cae2705d5299eae9b7444e84e72ab3bef96a691510dd05fa1da4 |

Original sources:
[CGAL auxiliary archive](https://github.com/CGAL/cgal/releases/download/v5.6/CGAL-5.6-win64-auxiliary-libraries-gmp-mpfr.zip),
[Embree archive](https://github.com/embree/embree/releases/download/v4.3.3/embree-4.3.3.x64.windows.zip),
[Qt Mesa archive](https://download.qt.io/development_releases/prebuilt/llvmpipe/windows/opengl32sw-64-mesa_12_0_rc2.7z).

Mesa additionally contains `llvm-mc (based on LLVM 3.6.2)` in its binary
strings. The [original 2016 Qt build instructions](https://wiki.qt.io/index.php?title=MesaLlvmpipe&oldid=28759)
specify LLVM 3.6.2, Visual Studio 2015, static LLVM CRT, SCons, and the
`libgl-gdi` target. The published base
[Mesa 12.0.0-rc2 source](https://archive.mesa3d.org/older-versions/12.x/12.0.0/mesa-12.0.0-rc2.tar.xz)
and [LLVM 3.6.2 source](https://releases.llvm.org/3.6.2/llvm-3.6.2.src.tar.xz)
are now downloaded with their notices.

## Follow-up: source correspondence and historical build limits

The CGAL auxiliary archive has no source or per-build patch manifest. This
absence is not evidence that a source modification is missing. The
GMP/MPFR release sources and their build/install scripts are now present.
The DLLs contain GNU C 4.4.5 / MinGW-w64 strings, so the 2024 CGAL Wiki's
modern MSVC/vcpkg build instructions are not evidence for these old DLLs.
A historical MinGW-w64 GMP 5.0.1 source ZIP was also inspected: every common
source file matches the GNU release after newline normalization and it has
explicit MinGW build instructions. That candidate is not proven to be the
CGAL build input and is kept locally as research, outside the bundle.

Static PE inspection of the original CGAL DLLs provides more specific
evidence: `__gmp_version` points to `5.0.1`; `mpfr_get_version` returns
`3.0.0`; and `mpfr_get_patches` returns the empty string. The collected
MPFR 3.0.0 source likewise has an empty `PATCHES` file and returns the
empty string from `get_patches.c`. The [MPFR 3.0.0 release documentation](https://www.mpfr.org/mpfr-3.0.0/)
explicitly describes `mpfr_get_patches` as the interface for identifying
applied release patches. This is positive evidence consistent with the
unpatched release. It does not exclude an undocumented private change
that deliberately or accidentally bypassed that mechanism. No specific
missing GMP or MPFR modification was found.

The [Qt Wiki revision history](https://wiki.qt.io/index.php?title=MesaLlvmpipe&action=history)
dates the first revision to 2016-11-11, after the Qt DLL archive's
2016-06-14 listing date. The [original instructions, revision 28759](https://wiki.qt.io/index.php?title=MesaLlvmpipe&oldid=28759)
mention additional patches generically, but contain no named patch set,
patch URLs, patch application commands, or commit IDs for that DLL.
They specify Mesa 11.2.2 or newer, LLVM 3.6.2, Visual Studio 2015,
and a SCons `libgl-gdi` build. This later, generic sentence does not
establish an outstanding patch against the 12.0.0-rc2 source.

The official Mesa repository's history at `mesa-12.0.0-rc2` confirms that
relevant Windows support and fixes were already upstream:

| Date | Upstream change | Evidence in the collected 12.0.0-rc2 source |
| --- | --- | --- |
| 2014-05-23 | [Windows MCJIT support, 2c02f34f](https://gitlab.freedesktop.org/mesa/mesa/-/commit/2c02f34fccee563e22db70cbfb03fb2cc7da30f9) | Windows ELF target handling in `src/gallium/auxiliary/gallivm/lp_bld_misc.cpp`; line 532 uses `x86_64-pc-win32-elf` |
| 2015-04-27 | [LLVM 3.5/3.6 Windows support, b94a4e84](https://gitlab.freedesktop.org/mesa/mesa/-/commit/b94a4e84987f6bdefabf45527ec434d8510da2b8) | LLVM 3.6 branch and libraries in `scons/llvm.py`, starting at line 121 |
| 2016-05-30 | [Native target initialization fix, cee459d8](https://gitlab.freedesktop.org/mesa/mesa/-/commit/cee459d84de7533d0e0a74a37f7fc4c0f2b77bcf) | `ONCE_FLAG_INIT` in `lp_bld_misc.cpp`, line 118 |

These are verified incorporated changes, not an assertion that they are
the unnamed Qt patches. It remains impossible to enumerate that generic
phrase from the published recipe alone. No concrete omitted Mesa or LLVM
patch was identified. The matching Qt binary origin, embedded LLVM
3.6.2 identity, original source releases, and published build recipe are
available together.

Therefore this work closes the missing archives, original notice
collection, external recipe identity and five binary-origin mappings.
The follow-up found no specific missing source modification for GMP,
MPFR, Mesa, or LLVM; it does not prove that every historical private
change can be excluded. Exact historical binary reproduction was not
performed and is not the source-material acceptance criterion. Missing
per-build manifests or compiler caches alone do not establish missing
necessary source or justify a mandatory rebuild. On this engineering
evidence, no new rebuild requirement follows from this investigation.
No upstream party was contacted, and no legal conclusion is made.

The already prepared ZIP and its checksum remain unchanged. The separate
`.codex-build/source-materials/provenance-followup.json` records the new
PE evidence and official commit records for inclusion with release
evidence. This section refines the historical-provenance caveats in the
unchanged ZIP README; it does not replace or alter any source archive.

## Smallest alternative if full-bundle provenance remains unacceptable

The current worker uses three PyMeshLab operations: QEM decimation, face
selection by expression, and point-to-reference-mesh distance. A private
runtime containing only the needed MeshLab plugins and verified shared
dependencies can preserve these operations while removing unrelated
CGAL/Embree/import plugins and the software-OpenGL fallback. This requires
an explicit native dependency closure, import and worker regression tests,
and truthful packaging records for the modified wheel payload. No
production files were changed by this investigation.

A second option is user-side installation of the pinned official wheel,
which preserves reduction but adds a first-use dependency-install step.
It changes the product experience more than trimming unused native
components. Neither proposal is a legal conclusion about integration.

Public delivery also requires actual source-asset availability with clear
directions, the installer's exact SW2URDF sourceCommit, and the separately
reviewed Windows native runtime prerequisites. Creating files locally is
not equivalent to publishing them.
