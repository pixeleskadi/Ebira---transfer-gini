# Ebira — native v1 candidate

Local file transfer for Windows 11 x64 and Android 10+ phones/tablets. The repository now contains native clients and a shared transfer engine. The original browser/Node prototype is retained separately for reference.

**Status: development candidate, not a certified public release.** Windows native TLS/key-profile behavior and physical Android storage, screen-lock/restart, 60 GB and 1 Gbps validation remain open. A successful compile does not close those gates.

Implemented: Windows → multiple approved Android devices and Android → Windows; direct source reads without outgoing staging; TLS and exact Android certificate pinning; sender/receiver SHA-256 verification; 8 MiB checksummed chunks; durable resume offsets; 24-hour approved sessions; sequential and per-file round-robin outgoing scheduling; collision-safe type folders; pause/resume/cancel and explicit partial cleanup.

The selected baseline is **60,000,000,000 bytes per file**. Both devices join an existing Wi-Fi LAN or hotspot. This build does not create Wi-Fi Direct/hotspot connections or implement QR/discovery. Actual hotspot compatibility and speed are still to be measured. There is no cloud service, account or analytics.

- [Build the native clients](BUILD.md)
- [Install, pair and transfer](docs/GETTING-STARTED.md)
- [Baseline and local data/security choices](docs/V1-BASELINE.md)
- [Protocol](docs/PROTOCOL.md)
- [Automated evidence](docs/VALIDATION.md)
- [Outstanding physical release tests](docs/DEVICE-VALIDATION.md)
- [Original scope](V1-SCOPE.md) and [master board](MASTER-ISSUE.md)
- [Legacy PoC documentation](docs/POC.md); use `Start-PoC.cmd` to start it.

`Start-Ebira.cmd` starts the native build under `dist/windows` when present. It does not fall back to the browser PoC. Apple clients, B2B, Linux, custom receive folders and later changes remain outside this candidate.
