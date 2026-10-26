# Developer presets and reproduction

## Preset document

Developer builds can save, import, export and validate a bounded JSON start
profile. Schema version 1 is explicit and rejects unknown or duplicate fields.
Documents are limited to 64 KiB and 64 typed parameter overrides. They contain
the registered scenario key and full map/content identity, Gameplay or Validation
profile, deterministic seed, two canonical participants, and initial parameter
values. They contain no absolute paths, executable content, runtime capability,
or permission to enable Developer tools.

This is a small schema-1 example using the registered
`conveyor.throughput_multiplier` parameter. `1.25` is applied only before the
next match starts; it does not mutate a running match.

```json
{
  "schemaVersion": 1,
  "key": "conveyor-example",
  "displayName": "Conveyor example",
  "buildRevision": "0.1.485.0",
  "exportedAtUtc": "2026-10-26T00:00:00.0000000+00:00",
  "baseScenarioKey": "central-divide.directorate.v1",
  "baseScenarioIdentity": {
    "mapKey": "prototype.vertical_slice",
    "mapRevision": 1,
    "mapSha256": "2f2b21f21697c665a8ab3ac607e7cebc6f82d1b9cf06f2838a02444f179f1f1f",
    "terrainVersion": 1,
    "contentKey": "directorate.v1",
    "contentSha256": "205d0887e816d8922c43996a6a8ad85d177cbbf168e551a87ce76d9cc81efd63",
    "startRule": "directorate.starting-base.v1",
    "objectiveRule": "command-core-elimination.v1"
  },
  "baseProfile": "Gameplay",
  "seed": 481,
  "participants": [
    { "playerId": 1, "factionId": 1, "startIndex": 0, "computerControlled": false },
    { "playerId": 2, "factionId": 2, "startIndex": 1, "computerControlled": true }
  ],
  "initialOverrides": [
    { "key": "conveyor.throughput_multiplier", "kind": "RealNumber", "scope": "global", "playerId": null, "value": 1.25 }
  ]
}
```

The codec resolves the scenario through `ScenarioRegistry` and checks its exact
identity before accepting the profile. It validates every parameter against the
registered type, scope, participant, range and paired domain constraints. A
missing or changed scenario, unsupported schema or invalid value makes the
preset unavailable with a validation error; the source document is left intact.
The default client store is
`%LOCALAPPDATA%\\FORGELINE\\Developer\\Presets`, holds at most 64 files, and
uses validated stable keys as filenames. Writes use a same-directory temporary
file and atomic move/replace. Imports never overwrite an existing key. Exports
validate the replacement before changing the existing file.

Exporting the original profile captures the values used to start the match.
Saving current values creates a new preset from effective values, including
pending next-match edits, without mutating the running match or its audit history.
Restarting a preset or returning to the baseline creates a normal new owned
session request and uses the client's transactional loading path. It does not
rewind a match or remove applied history. Session capability is still compiled
into the Developer build and bound by the normal session owner.

## Headless reproduction

Developer Headless accepts `--preset-input <path>`. Optional
`--reproduction-id <id>` and `--reproduction-output <path>` write a small identity
manifest after the run. The ID is 1–64 lowercase ASCII letters, digits or hyphens.
An explicitly supplied scenario, profile or seed must agree with the preset;
conflicts fail before the match starts. Recovery inputs cannot be combined with
a preset, and this first implementation requires one match. Public Headless
does not recognize these options as Developer functionality and rejects them.

The manifest records the bounded ID, preset key and build revision, base scenario
identity, profile, seed, canonical participants and typed initial overrides. It
does not copy absolute input/output paths or machine-specific locations. Save and
replay output continue through the existing codecs. Existing public recovery
paths remain responsible for rejecting Developer-only payloads in Public builds.

## Client integration handoff

`ClientDeveloperPresetSessionAdapter` owns the client-side store and produces
normal `ClientSessionRequest` values for preset restart and baseline restart. The
`ClientApplication` menu backend factory remains owned by the Menu and Diagnostics
integration. The concrete hook is `ClientApplication.DeveloperProviderFactory`
for registering the adapter as `IDeveloperPresetService`, paired with
`ClientApplication.DeveloperMenuBackendFactory` for menu intents. That owner must
compose the adapter with its session/action backend, surface store and validation
errors to the menu, and submit returned requests via the existing restart callback.
Until that composition commit is made, the preset codec, store and headless
workflow are available in Developer builds, while native menu actions for
import/export/restart are an explicit integration boundary.

## Acceptance boundary

This package has targeted product builds and focused preset acceptance coverage.
The full public/Developer package matrix, native menu interaction, full Save/Replay
compatibility matrix and combined scenario/content qualification remain open for
the combined qualification effort.
