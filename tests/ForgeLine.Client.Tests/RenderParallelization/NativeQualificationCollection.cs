using Xunit;

namespace ForgeLine.Client.Tests.RenderParallelization;

// Native windows/codecs share desktop resources; allocation/timing probes must not
// overlap other test collections in this process. Assertions and deadlines remain unchanged.
[CollectionDefinition("Exclusive native and timing qualification", DisableParallelization = true)]
public sealed class NativeQualificationGroup;
