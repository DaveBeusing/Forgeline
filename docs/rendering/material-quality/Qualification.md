# Material and shadow qualification

Status: implementation in progress; no quality or performance acceptance claimed.

## Baseline

Current master: `bfc16a953d731a844321a0848a2ebadc71c5875f`, version 0.1.423.
The accepted render foundation and its reviewed ownership release are integrated.
No existing material/shadow implementation branch or open PR was found.
The independent local `.gitignore` modification is excluded.

Master CI [39445686642](https://github.com/DaveBeusing/Forgeline/actions/runs/39445686642)
failed in the full test step: five existing Game failures and
`RenderSubmission.SubmissionSafetyTests.BatchEscapeBandUsesBoundedReusablePayloadLeases`
(expected zero allocation; observed 8,486,912 bytes on the hosted runner).
Later CI gates were skipped. Movement qualification passed. These failures
are baseline observations, not a waiver or a request to change other subsystems.

## Scope and contracts

Only material/shadow shader and projection ownership, local tests, measurement
helpers and this documentation may change. Root constants, shader resource
slots, vertex layouts, formats, resource lifetime, frame phases and simulation
remain fixed. The version increment is finalized once against current master.

The current material shader already implements GGX, cofactor normal transforms,
normal derivative specular filtering, hemisphere ambient, four weighted shadow
taps and snapped shadow projection. New evidence must distinguish existing
capabilities from the remaining numerical and quality delta.

## Next verification

Freeze an unchanged Release baseline, run the existing focused classes, create
controlled material/normal/shadow sequences, and compile all production variants.
Then qualify the minimal numerical and visual changes using unchanged assets,
lighting, camera sequences and quality profiles. Retain original references.
Human material and shadow-contact approval is separate from automated tests.
