# Third-party components

TaskbarMonitor's original source code is MIT licensed. This license does not replace any dependency license.

- **LibreHardwareMonitor 0.9.6**, unmodified binary: MPL-2.0. Source: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6 . The complete upstream license and third-party notices are included in `licenses/` and the installation directory.
- **PawnIO.Modules**, embedded by LibreHardwareMonitor: LGPL-2.1. License is reproduced in the upstream notices. Source: https://github.com/namazso/PawnIO.Modules . The official LHM source archive and linked module repository describe how to rebuild the library. Libraries remain separate DLLs and may be replaced with compatible rebuilt versions.
- **PawnIO 2.2.0 official installer** is redistributed unmodified as an optional dependency. Its author permits redistribution of the official installer: https://github.com/namazso/PawnIO.Modules/wiki/Using-PawnIO-Modules#licensing-considerations . This is not the unsigned unrestricted edition. Installation is opt-in, and the TaskbarMonitor uninstaller does not remove this shared driver.
- The official LHM release includes supporting assemblies (HidSharp, DiskInfoToolkit, RAMSPDToolkit, BlackSharp and Microsoft .NET support libraries). See the original project source, its package references and the bundled license notices for attribution and build details.

The icon was generated with OpenAI ImageGen and is included as part of this project. TrafficMonitor and LiteMonitor are architectural references only; their source code is not bundled or linked. See `REFERENCES.md`.
