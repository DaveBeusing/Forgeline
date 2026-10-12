# Precompiled runtime asset integrity

## Status

Accepted for authoring builds, CI and the Windows player.

## Context

The client already consumes runtime artifacts without starting a compiler. The
incremental compiler previously trusted a matching input key and file existence;
a damaged cached payload could therefore be retained. Publishing also depended
on a previously populated local runtime directory.

## Decision

Keep stable asset IDs, manifest format 1, FLAS header 1 and aggregate runtime
version 4. Add integrity contract 1: each record carries full-container size and
SHA-256; the manifest carries the compiler version and a deterministic SHA-256
content signature. The signature hashes the normal serialized manifest with its
signature field empty. It detects inconsistency, not publisher authenticity.
The authoritative hashes belong to the manifest. Container metadata is written
before its enclosing file can be hashed and therefore has empty integrity fields.

Compiler 1.9.0 invalidates old caches once. Keys continue to include source and
asset-definition bytes, referenced external glTF buffers, compiler/runtime
versions and transitive dependency build hashes. Reuse additionally requires
compatible record versions, unchanged paths and verified output bytes. Corrupt
outputs are rebuilt individually; an invalid or legacy manifest is rebuilt as a
whole. Changes that retain identical dependency build hashes do not force
unnecessary dependent reimports.

A kernel-held exclusive sibling `<runtimeRoot>.compile.lock` serializes writers,
including clean builds and the caller's complete build qualification. It remains outside the directory that clean removes.
Waits are bounded to five minutes and reported as cache-lock progress. A crashed
process automatically releases the handle. The empty lock file remains in place
to avoid unlink/recreation races. Output and manifest writes use unique temporary
files followed by atomic replacement. A generation is not a multi-file
transaction: interruption may leave an old manifest and some replaced files.
Integrity checks reject that inconsistent deployment, and the next compiler run
rebuilds missing or mismatched records. Orphan temporary files are ignored and
never packaged; a clean rebuild removes them.

Development and build modes compile then qualify all output. Explicit validation
mode never accesses authoring inputs or changes output. The player rejects
missing/incompatible integrity contracts, checks the manifest and dependencies
before menu readiness, and verifies each container on its existing file handle
when gameplay requests it. This avoids hashing gameplay content before the menu
or reopening a verified path. Legacy manifests remain available only to the
inspection API, which is not a deployment gate. Neither player configuration nor
an environment override enables legacy acceptance or source compilation.

Default publishing builds a cache owned by the output directory. CI supplies the
exact built/tested root to validation mode. Only manifest-listed outputs are
copied; the manifest checksum and every copied size/hash must match the qualified
snapshot. Package provenance includes the content signature/compiler version.
Repository notices are retained without shipping raw authoring files, temporary
files, the compiler or its encoder dependency. Missing or changed input fails the
build rather than falling back to another directory.

## Consequences and verification

Old development output must be rebuilt once; old player installations must be
replaced as complete packages. No runtime payload format or simulation dependency
changes. SHA-256 adds sequential I/O to verified incremental builds and requested
payload loads. Native bootstrap, menu-first initialization, cancellable gameplay
loading and render-owner GPU uploads retain their existing boundaries.

Asset tests cover unchanged/changed inputs, damaged files, deployment contracts,
version/settings invalidation, concurrent writers and interrupted-generation
recovery. `build/Test-RuntimeAssetPipeline.ps1` additionally exercises separate
processes, killing a live lock owner, recovery, source-less validation and
validation that refuses to repair corrupt output. Publish reproducibility and
installed-player startup are separate required checks. See
[Asset Pipeline](../AssetPipeline.md) for operational commands.
