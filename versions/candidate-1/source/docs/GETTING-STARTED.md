# Try the native v1 candidate

This development build targets Windows 11 x64 and Android 10+. It is for device validation. Windows is unsigned and Android uses a debug signing key; these are not the owner-signed public release packages.

1. Extract the Windows ZIP into a normal folder. Run `Install-Ebira.cmd` to copy it into your per-user Programs folder and add a Start-menu shortcut, or run `Ebira.exe` directly. Node.js and a separately installed .NET runtime are not required.
2. Install `Ebira-Android-development.apk` on your phone/tablet using Android's package installer. Enable installation for the local file manager only if needed. Public distribution signing is still pending.
3. Connect Windows and Android to the same private Wi-Fi, or manually join an existing hotspot. In Windows Firewall, allow Ebira on the private network when prompted. The app prefers port 8443, falling back to an available port if occupied. Do not expose it through a router port-forward.
4. Choose the Windows address belonging to that network. Copy its complete invitation to Android, paste it in Ebira, confirm it came from your Windows screen and tap Pair. If transferring the invitation manually is inconvenient, that is a known onboarding gap: QR/discovery is not implemented.
5. Approve the named device in Windows. Android can now send additional files under that same approval. Use Start / resume for the existing session; end an old session before pairing again. In Windows, check approved recipients and choose Add files to send to them.
6. Tap Start / resume transfers on Android to run its visible transfer service. Keep Windows awake. Select sequential or per-file round-robin in Windows when using more than one Android receiver.
7. To resume after interruption, reopen both apps, start the Android service and use Resume on any paused jobs. An expired/ended session needs new pairing and new file offers. Keep selected sources unchanged and available until all recipients finish.

Android photos/videos/audio publish to their respective Ebira media folders after checksum verification. Documents and other files appear under Download/Ebira. Windows receives under Downloads/Ebira by file type. Cancel transfers before cleaning incomplete files. Clearing history never deletes completed received files.

The app records local resume metadata and encrypted credentials. There are no accounts, cloud transfer or analytics. See V1-BASELINE.md for persistence, retention and platform limitations, and DEVICE-VALIDATION.md for the remaining release tests.

Updates are manual: stop Ebira and install the new Windows package into the same folder. Use an APK signed with the same owner-controlled key for Android upgrades; a development APK cannot be upgraded in place by a differently signed release. Back up completed files before uninstalling. User data is retained by the Windows uninstaller; Android uninstall removes private state but keeps published shared files.

Support/defects: use this repository's GitHub Issues. Include device/OS/build, the transfer direction, file size and the exact visible error. Do not post pairing invitations, tokens, private keys, local state or personal file contents.
