# Instance submission qualification

Status: implementation in progress; acceptance open.

Base: `47f4230f487e37f699b6572354e89f800476c513`, tracked version 0.1.420.
The remote had only master and no open PR at pre-check. Snapshot publication
and resource scheduling are integrated through PRs 258 and 260. The accepted
parallel ownership manifest supplies the required frozen boundaries.

Master CI 39421043011 fails before build at version validation: expected
0.1.421, received 0.1.420. This is a pre-existing integration issue. This
change will carry one isolated PATCH delta relative to its current base.

Only SimpleInstanceRenderer, InstanceVisibilityIndex and the assigned
RenderSubmission, build/rendering/instance-submission,
tests/ForgeLine.Presentation.Tests/RenderSubmission and this directory are
functional write scope. Shared contracts, resource-session ownership, graphics
layouts, workflows and existing tests remain frozen. Project references stay
acyclic. Native qualification and human image review remain required.
