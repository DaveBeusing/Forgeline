# Action-bar notification evidence

These are original native PNG candidates, copied without resampling, cropping or
recompression. `images.json` pins their bytes and source revisions. No image is an
approved golden or evidence of physical-monitor/accessibility acceptance.

- [1080p collapsed dock, three cards and overflow](notifications-1080p.png).
- [720p, 192 DPI, scale 2: critical summary when no regular card fits](critical-overflow-720p.png).
- [4K, 192 DPI, scale 2: complete detail text and fitted Close control](details-4k.png).

The first two use rendering revision `7a421bef309118dea32726dcd292321975b76a75`.
The last uses final product revision `3b5c545e4396c56a40f8ac54cb82fa285370e4da`,
whose only subsequent code change fits the detail Close control to scaled text.
The current complete Presentation/Client projects and three detail recaptures
verify that change. `records.zip` retains original text/JSON diagnostics, native
manifests, independent pixel audits, tests, costs, package identity and earlier
failures. The archive index pins every record; full original capture directories
remain under `D:/Code/Forgeline/artifacts/notifications-*`.

See [qualification and resume ledger](../../NotificationStackQualification.md) for
source-by-source coverage, commands, hashes and open CI/gameplay/human gates.
