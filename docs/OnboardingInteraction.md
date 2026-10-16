# Onboarding interaction

The optional match guide is anchored at SafeArea.X/Y and remains left aligned.
Its compiled compact skin is tinted with alpha 0.84; text stays opaque. No blur,
new artwork, font or settings/save schema is introduced.

Click the hint to minimize, HINT > to restore, or use Shift+F12. The entire visible
hint is the toggle; no separate close target is required. Tab focuses it only when
transient HUD navigation and camera keys do not own Tab. A focus rail marks the
owner; Enter/Space toggles it. The accessible names are “Minimize onboarding hint”
and “Restore onboarding hint”. Unfocused gameplay shortcuts keep their owners.

Hidden means onboarding is disabled, invalid/absent copied content, or a blocked
presentation interval. No panel, tab or input geometry is published then. This
never completes tutorial milestones. Collapsed state persists through new
objectives, pauses and temporary suppression in the same session. A new session
starts expanded. There is no new saved preference.

The published version-1 OnboardingPresentation includes translated panel, safe
clip, visible panel intersection, tab and focus. The left safe boundary clips
skin, dynamic/fallback geometry and native glyph UVs. The tab remains at that
boundary and owns only its painted rectangle. Vacated areas are world space.
The central transient owner still owns outside-click and release provenance;
a moving hint cannot acquire a held world-origin drag or its release. Toggle
presses are suppressed through release, so they cannot select/place/move too.
World clicks and Escape do not minimize the guide.

Bodies retain a 14 physical-pixel floor. Below 260 physical pixels of width or
140 of height, a reduced objective/binding view replaces context/next-step rows;
all text uses actual atlas metrics and ellipsis. Safe bounds remain at the top;
no fullscreen modal or downward relocation is used. The tab is 56 design pixels
wide and 48 high at unit scale. Its physical scale is bounded between 1 and 1.5
before safe bounds clip it, keeping restore compact on large HUDs. The label has
its own padded rectangle and 14–24 px native type; HINT and a right chevron
identify restore.
Clients with no usable safe region publish no target.

One 220 ms presentation-time SmoothStep transition can reverse from its current
position without a jump. Resting endpoints are exact. Reduced Motion, actual
Low/fallback quality, stalls over 250 ms, rollback, focus loss, minimize and
geometry changes settle the logical target and discard stale gestures. Rendering
uses the copied state without extrapolating it on another thread.

[Qualification and pending acceptance](OnboardingQualification.md).
