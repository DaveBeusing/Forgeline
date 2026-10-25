using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

internal enum NavigationDiagnosticExportFailure : byte
{
    None,
    ObservationUnavailable,
    LocalDirectoryUnavailable,
    WriteFailed
}

internal readonly record struct NavigationDiagnosticExportResult(
    string? Path,
    NavigationDiagnosticExportFailure Failure)
{
    internal bool Exported => Path is not null && Failure == NavigationDiagnosticExportFailure.None;
}

/// <summary>
/// Bounded single-report output for an explicitly authorized developer action.
/// The caller must authorize each explicit export action and supply the latest
/// copied snapshot, current selection and matching session metadata. Overlay
/// visibility is not authorization. This helper cannot inspect live simulation.
/// </summary>
internal static class NavigationDiagnosticReportExporter
{
    internal static NavigationDiagnosticExportResult TryWrite(
        PresentationSnapshot snapshot,
        SimulationSessionId expectedSession,
        SimulationTick expectedTick,
        PlayerId viewer,
        EntityId selectedEntity,
        string mapId,
        ulong seed)
    {
        if (!NavigationDiagnosticReportEncoder.TryEncode(
                snapshot, expectedSession, expectedTick, viewer, selectedEntity,
                mapId, seed, out byte[] report))
        {
            return new(null, NavigationDiagnosticExportFailure.ObservationUnavailable);
        }

        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData) || !System.IO.Path.IsPathFullyQualified(localData))
        {
            return new(null, NavigationDiagnosticExportFailure.LocalDirectoryUnavailable);
        }

        string directory = System.IO.Path.Combine(localData, "FORGELINE", "Diagnostics");
        return WriteReport(directory, report);
    }

    internal static NavigationDiagnosticExportResult WriteReport(string directory, byte[] report, Guid? identity = null)
    {
        if (report.Length == 0 || report.Length > NavigationDiagnosticReportEncoder.MaximumReportBytes)
            return new(null, NavigationDiagnosticExportFailure.ObservationUnavailable);
        string fileId = (identity ?? Guid.NewGuid()).ToString("N");
        string temporaryPath = System.IO.Path.Combine(directory, $".navigation-{fileId}.tmp");
        string reportPath = System.IO.Path.Combine(directory, $"navigation-{fileId}.json");
        bool ownsTemporaryFile = false;

        try
        {
            Directory.CreateDirectory(directory);
            using (var output = new FileStream(
                       temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporaryFile = true;
                output.Write(report);
                output.Flush(flushToDisk: true);
            }

            // Both paths are in the same directory; never expose a partial JSON
            // report and never overwrite a previous manual export.
            File.Move(temporaryPath, reportPath, overwrite: false);
            ownsTemporaryFile = false;
            return new(reportPath, NavigationDiagnosticExportFailure.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new(null, NavigationDiagnosticExportFailure.WriteFailed);
        }
        finally
        {
            if (ownsTemporaryFile)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    // A failed cleanup must not hide the original write failure.
                    // The uncommitted .tmp file is not a completed report.
                }
            }
        }
    }
}
