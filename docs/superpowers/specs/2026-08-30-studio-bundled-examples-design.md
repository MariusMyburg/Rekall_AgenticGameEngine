# Studio Bundled Examples Design

## Goal

Ship every authored project under the repository `Examples` directory with Studio and make those projects discoverable from a conventional `Examples` main menu.

## User workflow

Studio has a top menu containing `File` and `Examples`. `File` exposes the existing Create and Open workflows. `Examples` is populated from the bundled projects' `rekall.project.json` manifests and displays the manifest project names alphabetically.

Selecting an example never edits the installed copy. On first use, Studio copies the project to `Documents/Rekall AGE/Examples/<folder>` and opens that writable copy. If that destination already contains a project, Studio lets the user open it, create a fresh suffixed copy, or cancel. An unrelated collision is never overwritten and instead receives a fresh destination.

## Discovery and packaging

`RekallAgeStudioExampleCatalog` discovers immediate child directories containing valid `rekall.project.json` manifests. It accepts explicit search roots for tests and uses these default locations in order:

1. `Examples` beside the Studio executable for standalone publishes.
2. The distribution-level `examples` directory when Studio runs from `tools/studio`.
3. An ancestor repository `Examples` directory for developer builds.

The Studio publish target copies all repository examples into `Examples` beside Studio. It excludes only transient development state (`.git`, `.rekall`, `.vs`, `bin`, `obj`, and `TestResults`); authored scenes, modules, source assets, compiled game assets, captures, and proof material remain part of the examples.

## Copy safety

`RekallAgeStudioExampleLibrary` copies through a unique staging directory and atomically renames it into place. It rejects destinations inside the packaged source, skips reparse points and transient directories, does not overwrite an existing destination, and removes only its exact staging directory after a failed copy.

## Verification

Focused tests prove manifest discovery, ordering, duplicate precedence, writable copying, transient-directory exclusion, collision handling, and source immutability. A Studio build verifies the menu XAML/code compiles; evaluated publish items verify the example payload is attached to publish without performing a several-hundred-megabyte publish during ordinary feature development.
