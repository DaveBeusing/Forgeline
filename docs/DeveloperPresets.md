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
  "buildRevision": "0.1.486.0",
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
`%LOCALAPPDATA%\FORGELINE\Developer\Presets`, holds at most 64 files, and
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

## Client integration and operation

ClientApplication creates ClientDeveloperMenuBackend with a
ClientDeveloperPresetSessionAdapter by default. DeveloperProviderFactory and
DeveloperMenuBackendFactory remain optional override hooks. The backend routes
explicit import/export/save/restart intents to the real adapter and publishes
bounded compatibility or I/O feedback.

To import, place a schema-1 document in
`%LOCALAPPDATA%\FORGELINE\Developer\Presets\Import\<key>.json`.
The filename must equal the document's stable key. Open the Developer menu and
choose Presets, select the `Import: <display name>` entry, then activate Import.
Import validates the source again and creates a managed preset without replacing
an existing key. Incoming entries whose keys already exist are hidden; invalid
or incompatible incoming documents remain untouched and produce availability
feedback. Each directory is limited to 64 JSON files and each document to 64 KiB;
linked files/directories are rejected.

Export writes the original start profile as `original-profile.json` in the
managed preset directory, atomically replacing that key only after validation.
Save Current Values writes a new `preset-<guid>.json` containing effective values
and pending next-match edits. Neither operation changes the running match.
Select a stored compatible preset before Restart Selected. Restart Same
Configuration uses the original initial profile; Return to Baseline removes its
overrides while retaining the registered scenario identity, profile, seed and
participants. All restart intents return normal session requests whose settings
ClientApplication queues as ClientSessionRequest.Restart through the existing
transactional loader. Admission leaves the backend loading; it does not report a
completed new session before the loader constructs it.

## Acceptance boundary

The live client composition and store are integrated through PR #379. Historical
builds, native/package observations, Save/Replay evidence and remaining failures
are recorded in [Developer tools qualification](DeveloperToolsQualification.md).
Those results retain their original source identity; this documentation update
runs no tests and claims no new functional acceptance. Manual user approval
remains outstanding. No preset, save, replay or authoritative hash schema changes
are introduced here.
