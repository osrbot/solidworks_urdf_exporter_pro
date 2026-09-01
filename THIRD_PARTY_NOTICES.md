# Third-party notices

SolidWorks URDF Exporter Pro is distributed under the MIT License in
`LICENSE`. The installer also contains the runtime components listed below.
Their license texts are shipped in `THIRD_PARTY_LICENSES/`.

## Apache log4net 3.4.0

Copyright 2004-2026 The Apache Software Foundation.

Licensed under Apache License 2.0. This product includes software developed
at The Apache Software Foundation. Apache and Apache log4net are trademarks
of The Apache Software Foundation.

Source commit recorded by the signed NuGet package:
<https://github.com/apache/logging-log4net/tree/71c038c1860b331ca944840702d72da53e4cb41f>

## CsvHelper 7.1.1

Copyright 2009-2017 Josh Close and contributors.

CsvHelper 7.1.1 is dual-licensed under the Microsoft Public License and
Apache License 2.0. This distribution uses the Apache License 2.0 option.

Source and versioned license:
<https://github.com/JoshClose/CsvHelper/tree/7.1.1>

## MathNet.Numerics.Signed 4.7.0

Copyright 2002-2018 Math.NET.

Licensed under the MIT License.

Source and versioned license:
<https://github.com/mathnet/mathnet-numerics/tree/v4.7.0>

## Newtonsoft.Json 13.0.3

Copyright 2007 James Newton-King.

Licensed under the MIT License.

Source: <https://github.com/JamesNK/Newtonsoft.Json/tree/13.0.3>

## System.Runtime.CompilerServices.Unsafe 4.5.0

Copyright Microsoft Corporation.

Licensed under the MIT License.

Package: <https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe/4.5.0>

## System.Threading.Tasks.Extensions 4.5.1

Copyright Microsoft Corporation.

Licensed under the MIT License.

Package: <https://www.nuget.org/packages/System.Threading.Tasks.Extensions/4.5.1>

## CPython 3.11.9 embedded runtime

The pinned Windows embedded runtime is used by the bundled OpenUSD tooling.

Copyright (c) 2001-2023 Python Software Foundation; All Rights Reserved.

CPython is licensed under the Python Software Foundation License Version 2.
CPython also incorporates components under additional license terms; the
complete versioned `LICENSE` file must be distributed with the runtime payload.

Official release, source, and license:

- <https://www.python.org/downloads/release/python-3119/>
- <https://github.com/python/cpython/tree/v3.11.9>
- <https://github.com/python/cpython/blob/v3.11.9/LICENSE>

## OpenUSD usd-core 26.8

The pinned `usd-core` 26.8 CPython 3.11 Windows wheel provides the local
OpenUSD conversion runtime.

OpenUSD is licensed under the Tomorrow Open Source Technology License 1.0.
This license differs from the Apache License 2.0 in its trademarks section.
OpenUSD includes third-party components under additional terms; the complete
versioned `LICENSE.txt` file must be distributed with the wheel payload.

Official package, source, and license:

- <https://pypi.org/project/usd-core/26.8/>
- <https://github.com/PixarAnimationStudios/OpenUSD/tree/v26.08>
- <https://github.com/PixarAnimationStudios/OpenUSD/blob/v26.08/LICENSE.txt>

## MuJoCo 3.12.0

The pinned official Windows x86-64 release provides the local MuJoCo runtime
and validation tools, including `mujoco.dll`, `compile.exe`, and
`testspeed.exe`.

Copyright 2021 DeepMind Technologies Limited.

MuJoCo source code and runtime are licensed under the Apache License 2.0. The
official release archive includes `LICENSE` and `THIRD_PARTY_NOTICES.txt`; both
files must be distributed with the runtime payload.

Official release, package, source, and license:

- <https://github.com/google-deepmind/mujoco/releases/tag/3.12.0>
- <https://pypi.org/project/mujoco/3.12.0/>
- <https://github.com/google-deepmind/mujoco/tree/3.12.0>
- <https://github.com/google-deepmind/mujoco/blob/3.12.0/LICENSE>

## SolidWorks runtime boundary

SolidWorks interop types are embedded at build time. The installer candidate
also copies `solidworkstools.dll` from the explicitly selected local
SolidWorks installation because the add-in requires it at runtime. That file
is proprietary Dassault Systèmes software and is not covered by this
repository's MIT License or by the open-source notices above. Building a
candidate does not grant redistribution rights. Before a public installer is
published, the release owner must confirm that redistribution is permitted by
the applicable SolidWorks SDK and product license; otherwise the runtime must
be resolved from the user's licensed SolidWorks installation instead.

SolidWorks, Isaac Sim, Isaac Lab, ROS, Gazebo, and OpenUSD remain subject to
their respective licenses. Isaac Sim, Isaac Lab, ROS, and Gazebo are not
bundled with this installer. The pinned `usd-core` wheel listed above is the
only bundled OpenUSD payload; no separate USD SDK or application is included.
