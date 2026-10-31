using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class OwnerTraceHandoffTests
{
    [Fact]
    public void AttachAndReleaseRequireActualRunPidAndOriginalReadinessHash()
    {
        var directory = Directory.CreateTempSubdirectory("forgeline-trace-handoff-");
        try
        {
            var handoff = new ReferenceOwnerTraceHandoff(directory.FullName, new string('a',64), "actual-run", Environment.ProcessId, new string('b',64), Stopwatch.GetTimestamp());
            Assert.False(handoff.Attached()); Assert.False(handoff.Released());
            string attach = Path.Combine(directory.FullName,"trace-attached.json");
            File.WriteAllText(attach,JsonSerializer.Serialize(new { sessionEstablished=true,runId="replayed-run",processId=Environment.ProcessId,readySha256=new string('b',64) }));
            Assert.Throws<InvalidDataException>(()=>handoff.Attached());
            File.WriteAllText(attach,JsonSerializer.Serialize(new { sessionEstablished=true,runId="actual-run",processId=Environment.ProcessId,readySha256=new string('b',64) }));
            Assert.True(handoff.Attached());
            string release = Path.Combine(directory.FullName,"trace-release.json");
            File.WriteAllText(release,JsonSerializer.Serialize(new { completed=true,runId="actual-run",processId=Environment.ProcessId,checkpointSha256=new string('a',64),traceSha256="not-a-trace-hash" }));
            Assert.Throws<InvalidDataException>(()=>handoff.Released());
            File.WriteAllText(release,JsonSerializer.Serialize(new { completed=true,runId="actual-run",processId=Environment.ProcessId,checkpointSha256=new string('a',64),traceSha256=new string('c',64) }));
            Assert.True(handoff.Released());
        }
        finally { directory.Delete(true); }
    }
    [Fact]
    public void ExpiredReadyDeadlineCannotBeExtendedByAnAcknowledgement()
    {
        var handoff = new ReferenceOwnerTraceHandoff(Path.GetTempPath(),new string('a',64),"run",Environment.ProcessId,new string('b',64),Stopwatch.GetTimestamp()-11*Stopwatch.Frequency);
        Assert.Throws<TimeoutException>(()=>handoff.Attached());
    }
    [Fact]
    public void WindowPublicationIsAtomicAndExistingDestinationFailureKeepsPendingEvidence()
    {
        var directory = Directory.CreateTempSubdirectory("forgeline-trace-publication-");
        try
        {
            var handoff = new ReferenceOwnerTraceHandoff(directory.FullName,new string('a',64),"run",Environment.ProcessId,new string('b',64),Stopwatch.GetTimestamp());
            handoff.PublishWindowComplete();
            using var data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory.FullName,"window-complete.json")));
            Assert.Equal("actual-owner-trace-window-complete",data.RootElement.GetProperty("kind").GetString());
            Assert.False(File.Exists(Path.Combine(directory.FullName,"window-complete.pending")));
            byte[] original = File.ReadAllBytes(Path.Combine(directory.FullName,"window-complete.json"));
            Assert.Throws<IOException>(()=>handoff.PublishWindowComplete());
            Assert.Equal(original,File.ReadAllBytes(Path.Combine(directory.FullName,"window-complete.json")));
            Assert.True(File.Exists(Path.Combine(directory.FullName,"window-complete.pending")));
        }
        finally { directory.Delete(true); }
    }
}
