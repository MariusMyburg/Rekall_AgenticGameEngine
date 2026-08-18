# Playable Package Trust and Archive Security

Rekall AGE playable packages are portable, integrity-checked deployment
artifacts. They are not currently signed publisher identities or security
sandboxes.

## Trust boundary

`rekall.package.json` schema 2 records relative package roles and a SHA-256
inventory. Inspection verifies that declared regular files exist beneath the
package root, that their sizes and hashes match, and that no undeclared files
or reparse-backed paths cross the package boundary. This proves package
consistency with the manifest; it does not prove who produced the package.

Packaged C# modules remain in-process, full-trust code. Module build receipts
and output hashes detect stale or modified build outputs, but receipts are not
publisher signatures. Only run packages and modules from sources you trust.
Restricted module hosting and signed release/package provenance are future,
separate capabilities.

Use the shipped read-only inspector before execution:

```text
Rekall.Age.Cli.exe game inspect-package <package-directory|manifest|archive.zip>
```

The same inspection contract is available to agents as
`rekall.workflow.inspect_playable_package` through MCP.

## ZIP preflight limits

Before manifest deserialization, inventory allocation, hashing, extraction, or
execution, ZIP archives must pass one shared metadata-only preflight:

- at most 100,000 entries;
- at most 8 GiB uncompressed per regular file;
- at most 32 GiB total declared uncompressed content;
- exactly one regular root `rekall.package.json`, at most 4 MiB;
- normalized relative `/`-separated paths, at most 1,024 characters total and
  255 characters per segment;
- no traversal, empty segments, backslashes, control characters, drive/colon
  syntax, Windows wildcard characters, trailing dots/spaces, or reserved
  Windows device names;
- no case-insensitive target collisions or file/descendant conflicts; and
- only regular files and directories—never symbolic links, junctions,
  reparse-point entries, or other special-file modes.

Exceeded limits and unsafe metadata fail closed with stable
`REKALL_PACKAGE_ARCHIVE_*` diagnostics. Compressed byte counts are recorded,
but no compression-ratio heuristic is used: declared uncompressed byte ceilings
are the deterministic resource boundary.

## Extraction guarantees

Extraction independently repeats preflight against the archive it opens. It
refuses existing destinations and destination paths crossing reparse points,
streams only entries in the immutable preflight plan, and requires every stream
to end at its exact declared uncompressed length. Files are written into a
unique sibling staging directory. The completed package is published with one
directory move; failures remove staging and cannot publish a partial package at
the requested destination.

These controls address archive ambiguity, traversal, link redirection,
unbounded declared expansion, and changed/corrupt inputs. They do not replace
malware review, OS process isolation, publisher signatures, or a restricted
module runtime.
