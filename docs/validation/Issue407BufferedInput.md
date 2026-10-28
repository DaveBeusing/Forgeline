# Policy-template buffered input (#407)

The panel consumes a physical press sequence plus the retained WasKeyPressed edge. A released tap remains available exactly once; held-key autorepeat cannot produce a second action. Session changes synchronize key and mouse sequences before the new panel can act.

Local qualification passed 15 cases with zero skips: nine keyboard/session/gate cases, three selection identity/boundary cases, and three actual Win32 input-message integration cases. The latter use WindowsPlatform/IWindow, deliver WM_KEYDOWN and WM_KEYUP before the panel update, then drain native events through InputState into the actual panel controller with an eligible owned-smelter stock snapshot.

The native cases exercise navigation, Copy, Save, Preview and Apply; default and remapped bindings; an unrelated key; and focus loss/return. Save and Preview remain request-free. Apply emits exactly one expected stock-policy request, and a further press while awaiting its result emits none. No renderer or economy state is owned by the test window.

Countercheck: restoring only the original IsKeyDown criterion made all three native cases fail at the first released navigation tap (expected focus 2, actual 1). Restoring the final retained-edge fix returned the complete 15-case run to green. This qualifies the Win32 message path, not a physical-human keyboard or GPU visual review.
