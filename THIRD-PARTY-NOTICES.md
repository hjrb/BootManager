# Third-party notices and open source compliance

BootManager itself is licensed under the Apache License, Version 2.0; the full text is in the
[LICENSE](LICENSE) file that ships with every build.

This document lists the third-party components that BootManager uses and redistributes, together
with their licence terms. It is deployed next to the executable, and the About screen in the
application links to it.

**None of these licences restrict private or commercial use of BootManager.** They are all
permissive licences that require attribution only, which is what this document provides.

Package versions refer to the versions declared in `BootManager.csproj` and resolved in
`obj/project.assets.json`. To re-check the list after a dependency change, run:

```
dotnet list package --include-transitive
```

## 1. Direct dependencies

| Package | Version | Licence | Author | Project |
| --- | --- | --- | --- | --- |
| Avalonia | 12.1.1 | MIT | Avalonia Team | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Desktop | 12.1.1 | MIT | Avalonia Team | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Themes.Fluent | 12.1.1 | MIT | Avalonia Team | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Fonts.Inter | 12.1.1 | MIT (font: SIL OFL 1.1, see section 4) | Avalonia Team | https://github.com/AvaloniaUI/Avalonia |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | Microsoft | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Extensions.Configuration | 10.0.11 | MIT | Microsoft | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Configuration.Binder | 10.0.11 | MIT | Microsoft | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Configuration.CommandLine | 10.0.11 | MIT | Microsoft | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.11 | MIT | Microsoft | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Configuration.Json | 10.0.11 | MIT | Microsoft | https://github.com/dotnet/runtime |
| Serilog | 4.4.0 | Apache-2.0 | Serilog Contributors | https://github.com/serilog/serilog |
| Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 | Serilog Contributors | https://github.com/serilog/serilog-extensions-logging |
| Serilog.Settings.Configuration | 10.0.1 | Apache-2.0 | Serilog Contributors | https://github.com/serilog/serilog-settings-configuration |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | Serilog Contributors | https://github.com/serilog/serilog-sinks-file |

## 2. Transitive dependencies

These are pulled in by the packages above and end up in the published output.

| Package | Version | Licence | Author |
| --- | --- | --- | --- |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | BSD-3-Clause (ANGLE) | Avalonia Team / The ANGLE Project Authors |
| Avalonia.FreeDesktop | 12.1.1 | MIT | Avalonia Team |
| Avalonia.FreeDesktop.AtSpi | 12.1.1 | MIT | Avalonia Team |
| Avalonia.HarfBuzz | 12.1.1 | MIT | Avalonia Team |
| Avalonia.Native | 12.1.1 | MIT | Avalonia Team |
| Avalonia.Remote.Protocol | 12.1.1 | MIT | Avalonia Team |
| Avalonia.Skia | 12.1.1 | MIT | Avalonia Team |
| Avalonia.Win32 | 12.1.1 | MIT | Avalonia Team |
| Avalonia.X11 | 12.1.1 | MIT | Avalonia Team |
| HarfBuzzSharp | 8.3.1.3 | MIT (native library: "Old MIT", see section 4) | Microsoft |
| HarfBuzzSharp.NativeAssets.Linux | 8.3.1.3 | MIT | Microsoft |
| HarfBuzzSharp.NativeAssets.macOS | 8.3.1.3 | MIT | Microsoft |
| HarfBuzzSharp.NativeAssets.WebAssembly | 8.3.1.3 | MIT | Microsoft |
| HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.3 | MIT | Microsoft |
| MicroCom.Runtime | 0.11.6 | MIT | Nikita Tsukanov |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.11 | MIT | Microsoft |
| Microsoft.Extensions.Configuration.FileExtensions | 10.0.11 | MIT | Microsoft |
| Microsoft.Extensions.DependencyInjection | 10.0.0 | MIT | Microsoft |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.0 | MIT | Microsoft |
| Microsoft.Extensions.DependencyModel | 10.0.0 | MIT | Microsoft |
| Microsoft.Extensions.FileProviders.Abstractions | 10.0.11 | MIT | Microsoft |
| Microsoft.Extensions.FileProviders.Physical | 10.0.11 | MIT | Microsoft |
| Microsoft.Extensions.FileSystemGlobbing | 10.0.11 | MIT | Microsoft |
| Microsoft.Extensions.Logging | 10.0.0 | MIT | Microsoft |
| Microsoft.Extensions.Logging.Abstractions | 10.0.0 | MIT | Microsoft |
| Microsoft.Extensions.Options | 10.0.0 | MIT | Microsoft |
| Microsoft.Extensions.Primitives | 10.0.11 | MIT | Microsoft |
| Microsoft.IO.RecyclableMemoryStream | 3.0.1 | MIT | Microsoft |
| SkiaSharp | 3.119.4 | MIT (native library: BSD-3-Clause, see section 4) | Microsoft |
| SkiaSharp.NativeAssets.Linux | 3.119.4 | MIT | Microsoft |
| SkiaSharp.NativeAssets.macOS | 3.119.4 | MIT | Microsoft |
| SkiaSharp.NativeAssets.WebAssembly | 3.119.4 | MIT | Microsoft |
| SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT | Microsoft |
| Tmds.DBus.Protocol | 0.94.1 | MIT | Tom Deseyn |

## 3. Components used at build time only

These are not part of any published build, so their terms do not travel with the application. They
are listed for completeness.

| Package | Version | Licence | Note |
| --- | --- | --- | --- |
| Avalonia.BuildServices | 11.3.2 | MIT | MSBuild helper used during compilation. |
| AvaloniaUI.DiagnosticsSupport | 2.2.3 | Not declared in the package; see https://avaloniaui.net/ | Referenced for `Debug` builds only; excluded from every `Release` build by `BootManager.csproj`. |

## 4. Native libraries embedded in the above packages

The managed packages bundle pre-built native libraries whose upstream projects have their own
licences:

- **Skia** (inside `SkiaSharp.NativeAssets.*`) - BSD-3-Clause, Copyright (c) 2011 Google Inc.
  https://github.com/google/skia. The Skia builds statically link further open source components,
  among them FreeType (FreeType Licence), libpng, libwebp, zlib and Expat; the authoritative list
  ships with SkiaSharp at https://github.com/mono/SkiaSharp.
- **HarfBuzz** (inside `HarfBuzzSharp.NativeAssets.*`) - "Old MIT" licence, Copyright © 2010-2023
  Google, Inc., Copyright © 2018-2020 Ebrahim Byagowi and other contributors.
  https://github.com/harfbuzz/harfbuzz.
- **ANGLE** (inside `Avalonia.Angle.Windows.Natives`) - BSD-3-Clause, Copyright 2018 The ANGLE
  Project Authors. https://github.com/google/angle. The full text is reproduced in section 6.
- **Inter typeface** (inside `Avalonia.Fonts.Inter`) - SIL Open Font Licence 1.1, Copyright (c) 2016
  The Inter Project Authors. https://github.com/rsms/inter. The OFL permits bundling and
  redistribution of the font with software; the licence text is available at
  https://openfontlicense.org.

## 5. .NET runtime

The self-contained builds (`win-x64`, `linux-x64`, `osx-arm64`) bundle the .NET 10 runtime, which is
licensed under the MIT licence, Copyright (c) .NET Foundation and Contributors,
https://github.com/dotnet/runtime. The framework-dependent and portable builds do not contain the
runtime; it must already be installed on the target machine.

## 6. Licence texts

### MIT Licence

Applies to the packages marked "MIT" above. Copyright is held by the respective authors named in the
tables (among them the .NET Foundation and Contributors, Microsoft Corporation, the Avalonia Team,
Nikita Tsukanov and Tom Deseyn).

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### BSD 3-Clause Licence

Applies to Skia (Copyright (c) 2011 Google Inc.) and to ANGLE (Copyright 2018 The ANGLE Project
Authors. All rights reserved.).

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:

    Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.

    Redistributions in binary form must reproduce the above
    copyright notice, this list of conditions and the following
    disclaimer in the documentation and/or other materials provided
    with the distribution.

    Neither the name of the copyright holder nor the names of its
    contributors may be used to endorse or promote products derived
    from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
```

### Apache Licence 2.0

Applies to the Serilog packages, Copyright Serilog Contributors. It is the same licence BootManager
itself uses, so the complete text is in the [LICENSE](LICENSE) file next to this document and at
https://www.apache.org/licenses/LICENSE-2.0.

## 7. Application icon

The application icon (`Assets/bootmanager.svg` and the files generated from it) is original artwork
created for this project and is covered by BootManager's own licence. It draws the standardised
power/standby sign (IEC 60417-5010), a public standard symbol used by Windows, macOS and Linux
alike; no third-party icon set, logo or font was copied.
