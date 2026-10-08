using System.Text.Json;
using ForgeLine.Benchmarks;
using Xunit;

namespace ForgeLine.Simulation.Tests;

public sealed class ScalabilityMeasurementTests
{
    [Fact]
    public void NearestRankPercentilesPreserveRawOrderAndCountBudgetMisses()
    {
        double[] raw = [200, 1, 10, 2, 9, 3, 8, 4, 7, 5, 6, 11, 12, 13, 14, 15];
        using var report = JsonDocument.Parse(JsonSerializer.Serialize(ScalabilityMeasurement.Summarize(raw, 10)));
        Assert.Equal(8, report.RootElement.GetProperty("P50Milliseconds").GetDouble());
        Assert.Equal(200, report.RootElement.GetProperty("P95Milliseconds").GetDouble());
        Assert.Equal(200, report.RootElement.GetProperty("P99Milliseconds").GetDouble());
        Assert.Equal(6, report.RootElement.GetProperty("OverBudget").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("HitchesOver100Milliseconds").GetInt32());
        Assert.Equal(200, raw[0]);
        Assert.Throws<ArgumentException>(() => ScalabilityMeasurement.Summarize([]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(65537)]
    public void InvalidSampleCountFailsBeforeExecutingWork(int samples)
    {
        int calls = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => ScalabilityMeasurement.Measure("test", "test", () => ++calls, samples));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void RawSamplesAndMemoryWindowsKeepOperationCount()
    {
        int calls = 0;
        using var report = JsonDocument.Parse(JsonSerializer.Serialize(
            ScalabilityMeasurement.Measure("test", "one operation", () => ++calls, 16)));
        Assert.Equal(ScalabilityMeasurement.Warmup + 16, calls);
        Assert.Equal(16, report.RootElement.GetProperty("RawMilliseconds").GetArrayLength());
        Assert.Equal(5, report.RootElement.GetProperty("Memory").GetArrayLength());
        Assert.Equal(calls, report.RootElement.GetProperty("LastResult").GetInt32());
    }
}
