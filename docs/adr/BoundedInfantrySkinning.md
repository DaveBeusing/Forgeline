# Bounded infantry skinning

## Decision

Extend the existing versioned animation channel evaluator and source/runtime asset boundary. A mesh optionally references one Skin asset; the skin references nine independently identified animation assets. Static meshes, rigid clip binaries, mechanical vehicles and distant infantry LODs remain compatible. No gameplay entity is added for the four cosmetic squad figures.

The shared rig has eleven joints per figure (pelvis, spine, head, upper/lower arms and thighs/shins), four figures per squad. Limit skins to 48 joints, 32768 vertices, 98304 indices, four normalized influences per vertex and 8MiB payload. Only rigid joint TR with unit scale, finite matching inverse binds, parent-first hierarchy, float weights and unsigned byte/short joint indices are accepted. The existing Animation payload layout remains readable; its bounded skeletal envelope is 64 nodes/128 channels, while the rigid source/binding subset retains 32 nodes/64 channels. No skeletal retargeting, morphs, IK or ragdolls.

Skin poses are evaluated on the CPU once per shared action/time bucket, then submitted through existing instanced draws. Upload vertices use the existing fence-owned frame-index pattern; shadow and color passes share stable pose slots. At most sixteen animated pose variants and 16MiB upload vertices per frame slot, at most four slots. Overflow explicitly counts a static fallback. No update or disposal touches an in-flight frame. Reduced LODs stay static; simulation snapshots own action/phase inputs and session/entity lifecycle resets.

All nine actions are authored (idle/walk/run/aim/fire/reload/hit/suppression/death). Only actual copied movement, weapon, reload, impact, suppression and wreck states select them. Wreck selection, authoritative hits/navigation and squad health remain unchanged. Clip motion cannot drive simulation. Weapon sockets are evaluated by the same skin palette at the completed-tick shot pose, separate from authoritative hit trajectories.

Production geometry, weights, clips, source-less loading and native cost must qualify before acceptance; fixed frames are not human visual approval. Existing baseline software-fence and inactive-path timing failures remain separate and are not grounds to relax gates.
