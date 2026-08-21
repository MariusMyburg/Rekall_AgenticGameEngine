# Remote Asset Acquisition Design

## Problem

The first Studio-only `Rain Glass Reverie` acceptance run ended at the 36-turn
limit after `rekall.asset.import` received an HTTPS URL and reported `Asset
source file was not found.` The asset tool exposes only local-file import, so an
agent cannot honestly satisfy an ordinary request to use a licensed image from
the internet without leaving Studio or inventing a placeholder.

## Decision

Add a generic `rekall.asset.import_remote` command. It acquires one public HTTPS
resource under explicit network and size bounds, verifies its optional expected
SHA-256 digest, passes the downloaded bytes through the existing asset importer,
and records durable source and license provenance in the asset catalog.

This remains an authoring primitive: AGE fetches the exact resource selected by
the agent, but does not search for, select, generate, or license content on the
agent's behalf.

After the first installed Studio rerun, the direct Wikimedia host returned HTTP
429 and a user review exposed a more important authoring-contract flaw: the
Studio task field had been filled with internal tool, URL, provenance, testing,
and packaging instructions. Those details must not be user-authored.

The complete decision therefore also adds:

- `rekall.asset.search_remote_images`, a bounded Openverse catalog adapter that
  returns agent-selectable open-license candidates with direct URL, landing
  page, creator, attribution, and license metadata. AGE exposes evidence; the
  agent chooses and verifies a result, so the engine still does not author or
  select content.
- a lower-level agent task envelope and embedded contract that preserve the
  user's short ordinary-language request as authoritative intent and inject the
  generic tool discovery, rights/provenance, validation, runtime evidence,
  revision, packaging, and audit discipline beneath Studio;
- standards-compliant remote-host behavior: optional operator contact in the
  User-Agent, bounded `Retry-After` handling, and a stable rate-limit diagnostic
  that tells the agent to wait or select another licensed source rather than
  circumventing the host.

## Command contract

`ImportRemoteAssetRequest` contains:

- `ProjectRoot`, `SourceUrl`, `Kind`, and optional `DisplayName`;
- optional `ExpectedSha256` for caller-supplied integrity verification;
- optional `Attribution`, `License`, and `LicenseUrl` provenance fields.

`ImportRemoteAssetResult` returns the ordinary `RekallAgeAssetDocument` plus
the final URL, media type, byte count, and SHA-256 digest. The tool description
must contain the terms remote, HTTPS, URL, download, attribution, license, and
provenance so progressive tool discovery can find it from natural authoring
requests.

## Security and resource policy

- Accept absolute HTTPS URLs only. Reject credentials, fragments, non-default
  ports, and hosts that resolve only to loopback, unspecified, multicast,
  link-local, carrier-grade NAT, documentation, benchmarking, private IPv4, or
  unique-local/private IPv6 addresses.
- Disable automatic redirects. Follow at most five redirects, resolving and
  validating every destination before requesting it.
- Use a 30-second total cancellation deadline and allow at most 32 MiB of body
  data. Enforce both declared `Content-Length` and streamed-byte limits.
- Use a production `SocketsHttpHandler.ConnectCallback` that connects only to an
  address returned by the validated resolver for that hop. This closes the DNS
  rebinding gap between policy validation and socket connection while retaining
  normal TLS hostname verification.
- Send no project files, credentials, cookies, referrers, or authorization
  headers. Send only a stable Rekall AGE user agent and ordinary HTTP metadata.
- Stage the body in a project-confined temporary directory, import it through
  `RekallAgeAssetImporter`, and remove the staging file in `finally`. Reparse
  points are not permitted in the staging path.
- Return stable `REKALL_ASSET_REMOTE_*` error codes for invalid URL, blocked
  address, redirect policy, timeout, excessive size, HTTP failure, unsupported
  filename, and digest mismatch.

## Provenance

Extend `RekallAgeAssetDocument` with an optional `Provenance` value containing:

- original and final URL;
- retrieval time in UTC;
- media type and byte count;
- SHA-256 digest;
- caller-supplied attribution, license identifier/name, and license URL.

For remote assets, `SourcePath` is the original URL rather than an ephemeral
staging filename. Existing local assets deserialize with `Provenance = null`,
preserving catalog compatibility.

## Test and acceptance strategy

Unit tests use injected HTTP and DNS seams; they never depend on the public
internet. They prove successful import/provenance, URL and private-address
rejection, redirect revalidation, size enforcement, digest mismatch, cleanup,
catalog persistence, transaction resources, and command discovery/registration.
The existing asset suite proves local import compatibility.

After the locked build and affected suites pass, rebuild the installed Windows
distribution and repeat the same `Rain Glass Reverie` prompt through the Studio
UI with Qwen. Success requires a real remotely acquired CC0 image, authored
scene/shader/module, validation, playable runtime evidence from at least two
distinct frames, package inspection/audit, and provenance visible in the asset
catalog. Any new failure is treated as evidence for the next generic contract
repair rather than hidden with manual game authoring.

The final Studio rerun must put only a normal request in the game-authoring
field: “Create a game that uses a suitable openly licensed nature image from
the internet as a full-window background, with moving raindrops on glass over
it.” AGE and the embedded LLM contract—not the user—must expand that into the
required internal workflow. The Studio field starts empty and displays a
watermark explaining this responsibility split.
