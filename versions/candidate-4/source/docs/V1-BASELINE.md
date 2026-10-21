# Native v1 implementation baseline

This implements the supplied v1 scope. The requested delivery deadline supersedes the older November planning date. Apple clients, B2B, Linux, custom receive locations and later feature changes are outside this implementation.

## Confirmed during implementation

- Windows 11, x64; Android 10/API 29 or newer, phones and tablets.
- 60,000,000,000 bytes per file (60 decimal GB), not a batch limit.
- Sequential recipient scheduling or round-robin after each complete file; only one outgoing recipient active at a time.

## Concrete implementation choices

Windows is the local transfer host. Supported directions are Android → Windows and Windows → one or more paired Android devices. Android → Android and Windows → Windows are not implemented. Offline candidate 3 adds an Android local-only hotspot, owned by a separate visible connected-device foreground service. Windows joins using OS Wi-Fi settings. Android selects the band; optional phone hotspot settings provide manufacturer-dependent band control. Existing LANs/hotspots remain supported. No mobile data/internet/cloud relay is required. Windows-created Wi-Fi Direct groups and QR scanning are not implemented. Physical routing/hotspot compatibility and speed remain unverified.

Android sends bounded, nonce-bearing local UDP discovery probes; Windows responds on UDP 8444 with its local origin, exact certificate pin, name and pairing code. The user must independently compare code and certificate ending with the Windows screen before connecting, then approve the device there. Discovery is unauthenticated metadata, never a grant of file access. Android validates the response source/origin, protocol, nonce and field formats. Windows responds only to local same-subnet sources and rate-limits discovery. No bearer tokens or file metadata are advertised.

Android selects an available station Wi-Fi Network only when its subnet matches the destination. While owning the local-only hotspot, it uses Android's local-network route rather than forcing an unrelated station Wi-Fi or cellular Network. Local connections bypass HTTP proxies. The TLS client is cached for the current identity/token/mode, and fully consumed requests permit connection reuse; integrity checks and durable flush/offset ordering are retained. Finding the same Windows identity at a new DHCP address reuses a valid session and preserves resume state.

Windows serves the selected source directly. Android opens explicitly selected documents through the system picker with persistent read permission. Seekable, locally available files with a known size are required; providers that only stream or stage cloud documents cannot provide reliable direct resume and should not be used. There is no cloud service or relay, and no outgoing staging copy.

Receiver partial files are endpoint storage, not server/relay storage. They are hidden from completed destinations until verification. Windows uses Downloads/Ebira by type; Android uses Pictures/Ebira, Movies/Ebira, Music/Ebira, and Download/Ebira/Documents or Other. MediaStore selects collision-safe Android destinations. Windows uses an exclusive atomic rename, never overwriting an existing completed file.

Pause takes effect at a chunk boundary. A disconnected active outgoing recipient keeps its slot until resumed, paused, cancelled, ended or expired. A pending queue does not silently skip a recipient. Resume after restart requires reopening both apps, starting the Android service, and resuming paused transfers. The session must still be approved and unexpired. A new session requires selecting/offering files again; it cannot take over old session files.

## Local persistence and lifecycle

- Windows identity and state: per-user LocalApplicationData/Ebira, protected with current-user DPAPI. Windows Schannel also uses a temporary named key container in the current user's OS cryptographic profile; it is released when the certificate is disposed.
- Android state: app-private AtomicFile, AES-GCM with a non-exportable Android Keystore key. Backup/device-transfer exclusions are configured; there are no analytics.
- State includes local source/destination references, sizes, names, hashes, offsets, timestamps, session IDs and credentials. It is needed for restart recovery and remains local. The protocol never returns Windows filesystem paths to peers.
- Pairing invitations contain an exact certificate SHA-256 pin and a random 48-bit pairing code. Copy from the trusted Windows screen or independently compare the discovered code AND certificate ending with that screen. The Windows user must approve the named device; approval covers subsequently added files.
- Credentials expire after 24 hours. Codes rotate on Windows restart or on request. New code generation invalidates pending requests. Either device can end its session. Revocation prevents new requests and cancels in-flight payload operations; a chunk already committed may remain partial.
- Incomplete files are retained for recovery until explicit cancellation/cleanup. Completed files survive history cleanup. Local history is bounded to 10,000 records; clear completed history when needed. There is no automatic age-based payload deletion.
- Android foreground service, CPU wake lock and Wi-Fi lock support screen-lock continuation. Waiting ends after 15 idle minutes. Android 15+ data-sync service limits and manufacturer battery policy still apply. Force-stop, reboot and application restart require reopening/resuming; automatic boot restart is not implemented. Windows sleep suspends network transfers.
- The independent offline-connection service retains its OS hotspot reservation across Activity recreation and transfer-service pauses. Its notification provides an explicit stop action. Hotspot credentials are shown locally, kept in memory and cleared on service destruction. OS restrictions or force-stop/reboot can end the hotspot; manual recreation is required.

## Release status

This is an installable development candidate, not a certified public v1 release. Windows native TLS/DPAPI/profile behavior, Android OS storage/lifecycle, actual 60 GB transfers, hotspot compatibility, performance certification, release signing and the physical reliability matrix remain release gates. API size-boundary tests do not prove a 60 GB file has transferred. Loopback throughput does not prove 1 Gbps on Wi-Fi.

The 1 Gbps actual-delivery requirement remains open until measured on a named hardware/network configuration. No universal speed claim is made. Final numerical reliability/security acceptance and distribution ownership still need explicit release approval.
