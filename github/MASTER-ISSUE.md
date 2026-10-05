# Ebira v1 master board

Prepared master tracker for Ebira; GitHub publication is blocked by integration write permissions. Confirmed release direction is recorded below; unresolved technical acceptance criteria remain open. Planned features are not claims of implemented functionality.

## Confirmed product direction

- Free initial consumer early access: installed Windows and Android phone/tablet apps, primarily for India, targeting 7 November 2026.
- Paid Apple-compatible consumer release aimed at the US: desired within November 2026, conditional on testing, implementation and distribution readiness. This is a target, not a committed delivery date.
- Local file sharing with native file access.
- Mandatory 1 Gbps file-transfer performance on tested, certified configurations. Hotspot configurations may qualify after testing; this is not a universal speed promise.
- Sequential recipient delivery and selectable round-robin scheduling, with one active recipient at a time.
- Session-based approval, type-based receive folders and recovery after interruptions.
- Linux is outside v1.

Bluetooth cannot provide 1 Gbps file throughput. Its possible discovery/pairing or lower-speed role remains undecided. Round-robin switching granularity and the supported hardware/OS matrix are also open decisions.

## Available test hardware

- Lenovo Slim 5i, Core Ultra 5 125H.
- CMF Phone 1.
- Samsung Tab S9.
- MacBook Air M4, 15-inch.
- iPad Air: exact generation and OS version pending.
- No iPhone currently available; physical iPhone testing requires another device/tester.

Mac/iPad OS versions, exact iPad model and regular access will be supplied later. No generation is inferred from screen size.

## Remaining specification decisions

- Minimum OS versions and required transfer directions.
- Whether the requested 60 GB limit is per file or per batch; GB versus GiB.
- Round-robin scheduling quantum and switching granularity.
- Exact offline/direct-connection behaviours and supported fallbacks.
- Metadata retention/location and persistent resume/key requirements.
- Screen-lock/restart behaviour and the hardware-specific certification matrix.
- Final measurable reliability/security acceptance criteria.
- Paid follow-on pricing and distribution readiness.

## Task checklist

Unchecked means incomplete. Feasibility and specification decisions precede dependent implementation work.

- [ ] E-001 — Finalise and freeze the v1 acceptance criteria.
- [ ] E-002 — Establish this single GitHub master issue.
- [ ] E-003 — Finalise data-handling requirements.
- [ ] E-004 — Finalise security requirements.
- [ ] E-005 — Establish the supported device and OS test matrix.
- [ ] E-006 — Validate connection options across platform pairs.
- [ ] E-007 — Establish certified performance configurations.
- [ ] E-008 — Validate screen-lock and restart behaviour.
- [ ] E-009 — Specify the shared transfer protocol and engine.
- [ ] E-010 — Implement and verify connection/session security.
- [ ] E-011 — Implement the native Windows client.
- [ ] E-012 — Implement the native Android phone/tablet client.
- [ ] E-013 — Follow-on: implement the native macOS client.
- [ ] E-014 — Follow-on: implement the native iOS/iPadOS client.
- [ ] E-015 — Implement and test large-file integrity and recovery.
- [ ] E-016 — Implement sequential and round-robin recipient scheduling.
- [ ] E-017 — Deferred B2B: business identity/device workflow.
- [ ] E-018 — Deferred B2B: transfer history/audit export requirements.
- [ ] E-019 — Implement consumer installation/update requirements; business deployment is deferred.
- [ ] E-020 — Pass the approved end-to-end performance tests.
- [ ] E-021 — Pass the approved reliability tests.
- [ ] E-022 — Complete release security validation.
- [ ] E-023 — Record free initial pricing and define follow-on paid-release pricing/purchase requirements.
- [ ] E-024 — Validate initial direct-download distribution; obtain store approvals when relevant to subsequent channels.
- [ ] E-025 — Complete consumer onboarding/support readiness; business-pilot acceptance belongs to the later B2B edition.
- [ ] E-026 — Review evidence and approve the public release.

## Dependencies and release gate

Apple and B2B follow-on tasks do not block the Windows/Android early-access milestone. Exact consumer release gates must still be approved; calendar dates cannot waive integrity or security requirements.

E-001 and the feasibility tasks E-003–E-008 establish the implementation baseline. Client and transfer implementation depends on that baseline and the shared protocol. Performance, reliability, distribution and customer acceptance must be evidenced before E-026 can close.

Ready to sell means the approved acceptance criteria have passed and required distribution approvals are complete. It is not equivalent to a successful demonstration.

## Scope control

Feature additions are closed as of 5 October 2026. Outstanding baseline decisions are resolved in E-001. New ideas go into the later backlog below; they do not silently enter v1. Defect fixes must map to an approved requirement.

## Later backlog

- Linux support.
- Office-scale hierarchy and network-management expansion.
- Custom receive-folder selection.

Detailed internal planning is maintained separately; this public issue contains no credentials, local filesystem paths, budget figures or internal security decisions.



