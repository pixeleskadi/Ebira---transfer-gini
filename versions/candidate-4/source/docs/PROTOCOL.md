# Ebira native protocol 1

All network endpoints require TLS 1.2 or newer and exact certificate pinning by the Android client. HTTP is available only through an internal loopback test helper; the Windows product entry point always requires TLS. Metadata is UTF-8 JSON with explicit Content-Length and at most 64 KiB per request. Binary chunks have explicit Content-Length, at most 8 MiB. Sizes and offsets are signed 64-bit decimal byte counts. File limit: 60,000,000,000 bytes. One server I/O gate bounds simultaneous payload buffers.

## Pairing

The trusted Windows screen provides `ebira://pair?host=<encoded HTTPS IPv4 origin>&pin=<64 hex SHA-256 leaf-certificate digest>&code=<12 hex code>`. Android checks the pinned leaf certificate and its validity period on the TLS handshake, and checks the same pin for hostname verification. It refuses DNS names, public IP addresses, cleartext, redirects and malformed pins. The invitation is the out-of-band identity bootstrap; there is no trust-on-first-use fallback.

`POST /v1/join {code,name}` returns `{id,token,state,expires}`. Pairing is limited to five attempts per IP/minute and 20 active/pending sessions. A random 256-bit bearer token identifies the session. Only `GET /v1/session` and `POST /v1/end` accept pending tokens. Approval is performed in the Windows process, not through a remotely accessible admin endpoint.

## Local discovery (offline candidate 3)

Android broadcasts a UTF-8 UDP probe on private IPv4 interfaces to port 8444: `{magic:"ebira-discover-v1",nonce:"32 lowercase hex characters"}`. Windows accepts at most 512 bytes, requires a local same-subnet source and valid nonce, and limits processing to 20 probes/second. It replies to the probe's source port with `{magic:"ebira-host-v1",nonce,name,host,pin,code}`. Host is the HTTPS origin on the matching local interface. Neither bearer tokens nor file records are disclosed.

Android accepts at most 4096 bytes, requires the current nonce, exact protocol marker, a private IPv4 source matching the advertised host, a valid explicit HTTPS origin/port, a 64-hex pin and a 12-hex code. Discovery runs for five seconds only after a user action. These packets are unauthenticated: the user must independently compare both code and the displayed 12-hex certificate ending on the Windows screen before connecting. The complete 256-bit pin is then enforced for TLS; discovery never approves a session.

If the same certificate identity is discovered at a different DHCP address and a saved token still authenticates, Android updates only the host origin and retains the approved session/resume records. An expired/revoked token requires normal new pairing/approval.

## Transfers

Authenticated requests carry `Authorization: Bearer <token>`.

| Request | Behavior |
| --- | --- |
| `GET /v1/info` | Public protocol/version/size limits, host name. |
| `GET /v1/transfers` | Only this session's public transfer records; never source paths. |
| `POST /v1/transfers {id,name,size,sha256}` | Android offers a file. Random 32-hex ID, sender's full-file SHA-256, idempotent registration if metadata/session match. |
| `GET /v1/transfers/{id}` | Receiver's durable upload offset and transfer state. |
| `PUT /v1/transfers/{id}/chunk?offset=N` | Android uploads exactly the next chunk with `X-Ebira-SHA256`. Server validates it, writes and flushes to disk, then durably saves the next offset before acknowledging. |
| `POST /v1/transfers/{id}/finish {}` | Server hashes the complete receive file, compares to sender's manifest, journals the destination and atomically moves it to a collision-safe type folder. Idempotent completion. |
| `GET /v1/transfers/{id}/chunk?offset=N` | Android reads at most 8 MiB directly from the authorized Windows source, only while this outgoing job is active. Response includes `X-Ebira-SHA256`. |
| `POST /v1/transfers/{id}/ack {offset,sha256?}` | Android acknowledges a flushed and locally journaled chunk. Full-file digest at the exact file size marks verified completion and releases the scheduler slot. Repeating the final acknowledgement is safe. |
| `POST /v1/transfers/{id}/control {action}` | `pause`, `resume`, `cancel` for this session's transfer. |
| `POST /v1/end {}` | Revoke this session. |

Android reconciles a lost acknowledgement before downloading another chunk. A receiver never trusts a sender's progress over its own durable offset. On Windows restart, unacknowledged trailing partial bytes are truncated. All unfinished jobs pause. The finish journal handles a crash between receive-file rename and metadata completion. A checksum mismatch prevents publication; cancel and offer the source again.

Source changes cannot produce verified completion unless the complete bytes still match the original manifest. Windows also rejects a changed source length. Sources are not locked for the entire queue lifetime. There is no protocol compression or executable-file processing.

States: `queued`, `active`, `paused`, `verifying`, `complete`, `failed`, `cancelled`. Direction is from Windows's perspective: `send` or `receive`. Offsets are bytes durably accepted by the receiver, not network bytes written. Throughput displays are acknowledged-payload samples in decimal Mbps; final certification must time the entire delivery and verification separately from those samples.

Errors use JSON `{error}`: 400 invalid request, 401 unapproved/expired/ended session, 404 cross-session or unknown transfer, 409 state/offset conflict, 422 integrity failure, 429 pairing rate limit, 507 storage/source unavailable. Unexpected server exception details should be treated as defects; filesystem errors are mapped to a generic peer message.
