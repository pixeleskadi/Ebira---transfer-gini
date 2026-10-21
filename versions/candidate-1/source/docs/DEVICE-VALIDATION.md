# Native candidate: physical validation sheet

No rows below are certified by compilation or the automated checks. Use the actual Windows and Android apps, not the legacy browser PoC. Record OS/build, app hashes, device model, storage space, router/hotspot owner, band/channel, negotiated link rate, wall-clock elapsed time, byte count and SHA-256 for each run.

| Configuration / test | Status | Evidence |
| --- | --- | --- |
| Lenovo Windows 11 x64: installation, DPAPI identity, Schannel TLS, restart | Not run | Windows native key/profile access unavailable in sandbox. |
| CMF Phone 1: install, pairing approval/rejection, folders, both directions | Not run | Physical device required. |
| Samsung Tab S9: install, pairing, layout, folders, both directions | Not run | Physical device required. |
| Windows → phone + tablet, sequential and per-file round-robin | Not run | Check only one outgoing recipient active; pause one and verify next proceeds. |
| Same LAN with internet disabled | Not run | Explicit Android Wi-Fi routing implemented; validate device behavior. |
| Existing Windows hotspot → both Android devices | Not run | No automatic hotspot management. |
| Existing phone/tablet hotspot → Windows | Not run | Manufacturer routing varies. |
| Wi-Fi disconnect/reconnect during each direction | Not run | Check durable offset, final bytes and no overwrite. |
| Android screen locked, battery saver, background, 15 idle minutes | Not run | Record Android/OEM foreground-service restrictions. |
| Windows app restart; Android process kill/restart; phone reboot | Not run | Reopen apps and resume while session remains valid. |
| Zero-byte, pictures, video, audio, documents, archives, Unicode/reserved names | Not run | Verify correct destination, gallery visibility and collisions. |
| Actual 5 GB, 10 GB, and 60,000,000,000-byte file, both directions | Not run | Boundary metadata tests are insufficient evidence. |
| Disk full, revoked source permission, deleted/changed source | Not run | Must report an actionable state and preserve completed files. |
| Session revocation mid-chunk, expiration, foreign-session requests, wrong pin | Partial automated | Native TLS and actual Android Keystore/OS still need physical verification. |
| Certified ≥1,000,000,000 payload bits/sec | Not run | Time from first payload request through verified receiver completion, record hashing preparation separately. No Wi-Fi link-rate substitution. |
| Owner-signed installers/APK, upgrade and uninstall | Not run | Current Windows binary is unsigned; APK uses a development key. |

For each direction, compare hashes from independently read source and completed destination. Attempt interrupted transfer recovery at the first chunk, middle, last chunk, after local flush but before acknowledgement, and during verification/publication. Keep completed received files while clearing history and cleaning cancelled partials.

Release approval requires the outstanding scope/security/reliability gates to be explicitly resolved and the applicable rows to have reproducible evidence. Do not publish this candidate as a certified 1 Gbps or 60 GB-tested product.
