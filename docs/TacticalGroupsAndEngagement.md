# Tactical groups and engagement rules

Select units or recall a numbered group, open Combat in the existing action ring, then page to HOLD FIRE, RETURN FIRE or FIRE AT WILL. HOLD remains the existing zero-pursuit Hold Position order; ATTACK MOVE remains an explicit destination command. Fire rules and movement intent are independent, so changing a rule does not make a held group advance or replace a retreat, queue or individual order.

Only owned, controllable, living direct-weapon units accept policy edits. Cargo/Supply vehicles and artillery without a direct weapon retain their own behavior; partial selection results use the existing accepted/rejected feedback. Return Fire waits for real damage-derived retaliation and legitimate current intelligence. It cannot reveal or chase a hidden enemy. Empty ammunition remains empty; no readiness, supply, fuel or range is granted. Explicit artillery missions retain their own setup, ammunition and cancellation controls.

The selected-group status shows ENGAGEMENT and OBJECTIVE, with MIXED when participating units disagree or only some have tactical orders. Existing health/readiness/fuel/ammunition values retain real contributor coverage. Missing components never imply full readiness. Facts come from detached owned completed-tick snapshots, so render rate cannot drive gameplay policy.

Ctrl+0..9 assigns the current owned selection; 0..9 recalls it; Ctrl+Shift+0..9 clears a slot. Slots can overlap. Filtering or individually commanding one member does not rewrite a saved selection. Destruction prunes the exact full-generation identity, and a replacement unit never inherits membership. Labels, surviving members and active slot now survive normal Save/Load. A new game still starts with empty slots. At most ten slots and 1,024 members per slot are retained; labels are limited to 24 printable characters. Loading intersects the saved membership with the restored local-player live snapshot.

Save/replay payload is 12; authoritative schema stays 18 and envelope stays 1. Existing compatible older payloads without the new operations/metadata remain valid. Invalid metadata or new policy operations disguised as older payloads fail before reconstruction. Group metadata is selection organization, without simulation authority or influence on authoritative hashes.

## Qualification

Run the normal Release solution build and the EngagementPolicyTests, TacticalCombatSystemTests, MatchPersistenceTests, PlayerCommandBoundaryTests, EngagementGroupTests, existing CombatGroup/SelectedCombatGroup tests, ControlGroupSaveTests and existing client group/ring/session tests. CI retains all original gates and includes the new classes.

The existing native capture host supports:

    ForgeLine.Client.exe --engagement-policy-capture artifacts/engagement-native assets/runtime

Its three native D3D12 HUD fixtures use the production controller to page the existing ring, render owned group status, and retain repeated image/text diagnostics. This is authored UI evidence, separate from gameplay authority tests and human visual acceptance. See qualification/tactical-groups for executed-source evidence and any remaining blockers.

See [the architectural decision](adr/ExplicitEngagementPoliciesAndSavedControlGroups.md).

Compact ring labels are Hold fire, Retaliate (Return Fire) and Free fire (Fire At Will); full action titles retain the policy names. The ring reports the actual eligible direct-weapon count for policy actions, and selections above 1,024 are refused without scheduling.

The objective also recognizes owned ordinary movement and recurring patrol. Completed/cancelled combat orders do not remain active objectives; differing active intent or only some moving members reports MIXED.
