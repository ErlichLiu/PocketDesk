# Experimental PocketDesk IddCx driver

**Unsupported source experiment. Not a working installer. Not part of the app build.**

The earlier development device failed to start with Code 31 (`CM_PROB_FAILED_ADD`). Renaming this source does not resolve that failure. Successful compilation does not establish loading, signing compatibility or production suitability.

This subtree adapts Microsoft's [Windows-driver-samples / IndirectDisplay](https://github.com/microsoft/Windows-driver-samples/tree/main/video/IndirectDisplay) and is distributed under the **Microsoft Public License (MS-PL)**, not the repository's root MIT license. See [LICENSE](LICENSE) and the retained source notices.

## Layout

```text
idd/                 Driver source, project, INF and generated EDID header
scripts/gen-edid.py  Deterministic EDID generator (no device or system operations)
```

Development requires VS C++ Build Tools, WDK/SDK and compatible UMDF/IddCx versions. The current project targets WDK 10.0.26100, UMDF 2.25 and IddCx 1.2. From the repository root:

```powershell
python drivers/windows/scripts/gen-edid.py
msbuild drivers/windows/idd/PocketDeskIdd.vcxproj /p:Configuration=Release /p:Platform=x64
```

No installation/signing script is provided: previous local experiments changed certificate stores and UMDF registry state and are intentionally excluded from public tooling. Do not assume that a self-signed certificate is sufficient for every Windows policy or driver-signing configuration. Do not enable test signing or weaken system protections on the basis of this repository.

Do not install, uninstall or restart display drivers while depending on a live remote session. The regular app should use an independently installed working virtual display. Existing hardware IDs and device instances are not rewritten by renaming these source files.
