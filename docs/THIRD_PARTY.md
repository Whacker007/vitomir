# Third-party components

Vitomir itself is under the Vitomir Licence 1.0 (`LICENSE`); our own typefaces (`assets/fonts/`, and the atlases
built from them) under the SIL Open Font License 1.1 (`assets/fonts/OFL.txt`). Nothing below is stored in this repository:
packages are restored by NuGet at build time, and the ECJ jar is downloaded by the build into `lib/` (which git ignores).
This page records what Vitomir depends on and under which licence, so the combination stays compatible.

| Component | Used for | Licence | How it arrives |
|---|---|---|---|
| IKVM 8.16 | runs the game's Java scripts inside .NET | IKVM: zlib-style; `IKVM.Java` (OpenJDK class library): GPLv2 with the Classpath Exception | NuGet |
| ECJ 4.6.1 (Eclipse Compiler for Java) | compiles the player's own `.java` scripts at load time | EPL-1.0 | downloaded from Maven Central by the build |
| xUnit v3, xunit.runner.visualstudio | tests | Apache-2.0 | NuGet |
| Microsoft.NET.Test.Sdk | tests | MIT | NuGet |
| SDL3 | window, input, GPU (SDL_GPU), audio output | zlib | native library inside the ppy.SDL3-CS package |
| ppy.SDL3-CS | C# bindings for SDL3 | MIT | NuGet |
| NLayer 3.0.0 | decodes the game's MP3 music, ambience and dialogue (fully managed, no native code) | MIT | NuGet |
| DotRecast.Recast, DotRecast.Detour 2026.3.1 (and DotRecast.Core, which they bring) | navigation meshes: Recast builds a cooked level's mesh, Detour's tile format carries it; a C# port of Recast & Detour, fully managed | zlib | NuGet |
| BCnEncoder.Net 2.3.0 (and CommunityToolkit.HighPerformance 8.4.0, Microsoft.Bcl.Numerics, which it brings) | a cooked level's material maps, compressed to BC7 and BC5; fully managed | MIT or Unlicense (CommunityToolkit: MIT) | NuGet |
| Microsoft.Extensions.Logging.Abstractions | the logging API (`ILogger`, `LogLevel`, the `LoggerMessage` source generator); the factory, the sinks and the settings are ours | MIT | NuGet |
| DXC (DirectX Shader Compiler) | build tool: compiles our HLSL to SPIR-V and DXIL; nothing of it ships | MIT and LLVM licences | NuGet `Microsoft.Direct3D.DXC` on Windows; a DXC release elsewhere (`DXC_PATH`) |

**What the game's zip carries for them** (`tools/package/licences/`, the zip's `licences\` folder): each component's licence in
full with its copyright lines and where its source is; `THIRD_PARTY.txt` beside it names each file. **IKVM.Java's source** (GPLv2
with the Classpath Exception, which asks for the source with the binary) is in this repository: `third-party/ikvm-8.16.0/`, IKVM's
source at its tag and the OpenJDK files it compiles into `IKVM.Java.dll` (the rest of OpenJDK left out to save space; its
README says what and where the whole is), made by `tools/third-party/ikvm_source.py`.

## What the player supplies

| Component | Used for | Licence | How it arrives |
|---|---|---|---|
| FFmpeg | decodes the game's Bink films | LGPL 2.1 in a default build | **not shipped and not required**: Vitomir looks for one the player already has, and plays no film when there is none |

Nothing about FFmpeg is distributed here — no binary, no source, no bindings — so none of its terms attach to a Vitomir
release. It is run as a program, the way a shell would run it. If that ever changes and a build is shipped alongside the
engine, the obligations are a notice, the LGPL text, the corresponding source of that exact build, and dynamic linking so a
player can replace it — and the build must be made with neither `--enable-gpl` nor `--enable-nonfree`.

The game itself (archives, levels, models, scripts, the NOD SDK) is never redistributed; Vitomir reads the player's own copy
(see `ORIGINAL_CONTENT_POLICY.md`). Community tools and mods are reference points only unless their licence is compatible
and the borrowing is listed here.
