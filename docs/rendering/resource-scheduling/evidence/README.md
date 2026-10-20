# Resource scheduling qualification evidence

The index maps each original artifact to a content-addressed retained object, with original, retained and stored SHA-256 hashes. PNG bytes are unchanged. Text records are UTF-8 with private profile/runtime paths removed and a BOM normalized, then losslessly gzip-compressed. Path changes are explicit in each index entry; timing samples, pixels, exit codes and assertions are not edited. Failed tests and performance cohorts remain retained.

## Native image review

The six images below are the final changed-source second cohort (09eb021b). The before/after pairs in both complete protocols match exactly by native PNG checksum; human visual equality review was explicitly approved on 2026-10-20. This is presentation equality, not approval of new artwork.

| Scene / camera | Native PNG |
| --- | --- |
| MainBattleTankProduction-1920x1080-High-CloseTactical | [1920 x 1080](objects/c091b5b91419dd6808ba1358fc5a58068681fb64451311f79ba5618a99497320.png) |
| MainBattleTankProduction-1920x1080-High-NormalGameplay | [1920 x 1080](objects/f156028d99a9d5ca869306b7ab30b6aae4ef4a22a84639560a09d252d54d3ae0.png) |
| MainBattleTankProduction-1920x1080-High-Strategic | [1920 x 1080](objects/573ecfead47910b9aa0a13202505fdcc3f5338d02fa91008e304563613ce036a.png) |
| IndustrialBase-1920x1080-High-CloseTactical | [1920 x 1080](objects/555f1271216befa60296f390dcc123e3993117b8cfb4f8a6c92864a9a0cdb69f.png) |
| IndustrialBase-1920x1080-High-NormalGameplay | [1920 x 1080](objects/54930583f7e8adfb1e233249234e589e82ffece34b25b6be80ed2d6d639a262c.png) |
| IndustrialBase-1920x1080-High-Strategic | [1920 x 1080](objects/0488ee1d61fc565f9a2d83c8376fd6a3fe716a602078c24cffa0b975cbc47264.png) |

## Status

The current coordinated cohort passes the first frame-time pair and fails the second pair on IndustrialBase NormalGameplay CPU p95 and p99. Both fixed protocols and both rejected competing-workload attempts remain recorded. No replacement run, relaxed threshold or automatic golden approval is used.

The original full suite has four independently reproduced baseline UI contract failures. The serial correction and its complete verification are recorded separately. Opt-in DRED and actual debug-layer availability are separate from timing acceptance.

See [qualification](../Qualification.md) and [record index](index.json).
