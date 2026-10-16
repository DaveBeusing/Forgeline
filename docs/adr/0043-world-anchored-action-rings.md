# World anchored action rings

Status: implemented; qualification pending.

Single owned selections previously split immediate commands and advanced parameters between rectangular surfaces. They now use one bounded screen-space annulus anchored to a copied permitted world instance. Group controls retain their established behavior until their own migration.

The existing PlayerActionPanelController remains the only action dispatcher. Root categories, paged item identities and parameter controls map to its existing operations and validation. Each level has at most eight controls. Shared D3D overlay buffers, text and icon atlases render the ring without a new per-object resource owner.

Rendering resolves the world anchor from the interpolated instance and actual camera, fits it around persistent HUD surfaces, and publishes the presented geometry back to input. Press/release identity validation includes session, generation, selection/menu revision and stable item IDs. Outside-click and Escape handling remain central. Closed visuals own no input, including during alpha fade.

This adds a bounded render/input value exchange and may delay first interaction until a corresponding frame is presented. It avoids divergent hit geometry during camera motion. At insufficient readable screen space the ring closes; the global category bar can retry. Native qualification and human visual/motion review remain explicit gates.
