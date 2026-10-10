# FORGELINE integration preview

Development snapshot from the identified master commit. Version 0.y.z remains
initial development and does not guarantee a stable public API.

Refer to the linked source commit and CI run for the integrated changes and
qualification evidence. This is not a release candidate or stable release.

## Compatibility and recovery

Save/replay, settings, map and runtime asset formats retain their own explicit
schema versions. Loading incompatible or corrupt data fails closed; a product
build number is not a schema migration identifier. Keep backups before testing
a preview. Roll back by installing a retained earlier package, not by replacing
release assets or moving tags. Cross-build save/replay compatibility must be
verified for the particular source changes.
