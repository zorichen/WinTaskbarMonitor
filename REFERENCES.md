# Implementation references

Inspected on 2026-09-09. Counts are snapshots, not a claim of a global ranking.

| Project | GitHub stars observed | Source checked | Used for |
| --- | ---: | --- | --- |
| [TrafficMonitor](https://github.com/zhongyang219/TrafficMonitor) | 46101 | `188b8773b959733bfc0e4f506c762d9a255883d4` | Taskbar child-window strategy, weather clearance, polling and memory APIs |
| [LiteMonitor](https://github.com/Diorser/LiteMonitor) | 6313 | `169e90a2ccaa68ca29d0b549d10c6c59634c1b49` | Two-row geometry, resource lifetime, current sensor dependency |
| [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | 9022 | official v0.9.6 release | CPU package and storage temperature library |

GitHub repository searches used `windows taskbar monitor`, `TrafficMonitor`, and `LibreHardwareMonitor`, with `sort=stars&order=desc`. TrafficMonitor was the highest-star taskbar monitoring application among the relevant results inspected.

Local clones: `../references/TrafficMonitor`, `../references/LiteMonitor`. Our small UI and sampler are written independently; no source files from those UI applications are included in the app build.

Relevant source locations:

- TrafficMonitor `TrafficMonitor/TaskBarDlg.cpp`: `SetParent`, geometry, two-row layout.
- TrafficMonitor `TrafficMonitor/Win11TaskbarDlg.cpp`: left-side widget spacing and offsets.
- TrafficMonitor `TrafficMonitor/TrafficMonitorDlg.cpp`: `GlobalMemoryStatusEx` and sensor sampling.
- TrafficMonitor `TrafficMonitor/CommonData.h`: default `monitor_time_span` 1000 ms.
- LiteMonitor `src/UI/Helpers/TaskbarStrategyWin11.cs`: child window lifecycle and coordinate conversion.
- LiteMonitor `src/UI/TaskbarRenderer.cs`: two-row text rendering and cached fonts.

Binary dependencies (official sources):

- LibreHardwareMonitor v0.9.6: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.6
- Download: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.zip
- SHA256: `086D9F1B5A99E643EDC2CFAAAC16051685B551E4C5AC0B32A57C58C0E529C001`
- PawnIO 2.2.0: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0
- Download: https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe
- SHA256: `1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`
- Authenticode verification returned `Valid`, signer `CN=namazso.eu, O=namazso`.

LibreHardwareMonitor is MPL-2.0, with additional dependency notices in its upstream repository. Dependency assemblies are unmodified. TrafficMonitor's Anti-996 license stays with the reference clone. The LiteMonitor clone has no root license file in the inspected revision; it is a reference only and none of its code is redistributed in this implementation.
