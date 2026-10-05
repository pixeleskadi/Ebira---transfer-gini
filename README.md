# Ebira — bare-bones PoC

This working prototype hosts a local encrypted transfer server on Windows. Android phones/tablets use their browser. It has no cloud service or external package dependencies.

## Start

Requirements: Windows with its built-in Windows PowerShell 5.1 and Node.js 20 or newer. PowerShell 7 (`pwsh`) is not required. The launcher also checks the standard Node.js installation folder if Node is missing from PATH.

1. Double-click `Start-Ebira.cmd` and keep the window open.
2. Open the **Windows control** URL printed in that window on the Windows machine. Treat this URL as private: it grants host control.
3. Connect your phone/tablet to the same Wi-Fi. Open the printed phone/tablet URL in Chrome.
4. The PoC generates a self-signed HTTPS certificate, so browsers show a certificate warning. For personal testing on your trusted LAN, inspect the certificate and compare its SHA-256 fingerprint with the terminal before proceeding. The connection is encrypted, but certificate identity is not publicly trusted. Do not use this PoC on an untrusted network.
5. Enter a device name and the pairing code from the Windows terminal. Approve that session in the Windows page.
6. Select multiple files and press **Send selected files**. On Windows, choose the approved device in **Send to** first.

Ebira prefers port 8443. If that port is occupied, it automatically selects an available port and prints the correct URLs. Always use the addresses from the current launcher window.

Windows Firewall may need a private-network rule allowing Node.js or the TCP port printed in the launcher. Do not open this port on your router. If a guest Wi-Fi network blocks communication between devices, use a normal shared network or manually join an existing hotspot.

## What works

- HTTPS/TLS encryption, browser-to-Windows and Windows-to-browser transfers.
- Approval once per device session, including subsequently added files.
- Multiple files sent sequentially; individual file limit 10 GiB.
- 4 MiB upload chunks, bounded server memory, progress and measured upload rate.
- Stop/pause after the current chunk; resume by pressing Send while the same page/server remain open.
- Received Windows files saved under `Downloads/Ebira/Pictures`, `Videos`, `Music`, `Documents`, or `Other`.
- Collision-safe receive filenames. No overwrite of existing received files.
- Receiver-side SHA-256 computation after upload; HTTPS authenticates transport records. This is not yet an independent sender/receiver checksum comparison.
- Either side can end a session. Subsequent file requests require a new approved session.

## PoC limitations

- No native APK or standalone Windows installer yet. Phone/tablet browser downloads use the normal Downloads destination and require tapping Save per file. Android gallery albums and automatic type-based folders require the native app.
- Windows stages outgoing files on disk before Android downloads them, using extra storage and an additional transfer step. This is not the final high-speed architecture.
- Upload resumes only within the active page/server lifetime. Refreshing the sender page, restarting Windows, or restarting the server does not restore upload state.
- No automatic nearby discovery, QR scan, Wi-Fi Direct management, or Maximum speed route selection yet.
- No background-transfer guarantee: keep the browser foreground and screen awake.
- TLS certificates need manual verification in this PoC; native certificate pinning and stronger pairing will replace this flow.
- No malware scanning, disk-space preflight, download resume, persistent history, idle expiry, or automatic staging-file cleanup yet. Ending a session prevents new requests; a download already in progress can finish.
- Outgoing and incomplete files remain in `data` until manually removed while the app is stopped. Windows completed incoming files remain in Downloads/Ebira. Private key material is also in `data`; do not share that directory.
- The automatic checks exercise real HTTPS transfers with small binary files. Browser testing also passed a 16 MiB chunked upload and a reverse download using two separate browser sessions. The 10 GiB metadata limit is tested, but actual 5–10 GB transfers and device performance have not yet been validated on your hardware.

## Tests

Run `node --test test.js` from this folder after the first launch creates the local certificate.

The tests cover session approval/revocation, two-way byte-identical transfer, chunk offsets/resume, session isolation, file-size limits, zero-byte files, and file categorization.
