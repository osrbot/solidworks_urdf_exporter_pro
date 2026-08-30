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

SolidWorks, Isaac Sim, Isaac Lab, ROS, Gazebo, and USD remain subject to their
respective licenses. Isaac Sim, Isaac Lab, ROS, Gazebo, and USD are not bundled
with this installer.
