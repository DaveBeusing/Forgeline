using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ForgeLine.Client;

internal static class ClientDiagnostics
{
    internal static string? WriteCrashReport(
        Exception exception,
        string? diagnosticsRoot = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            string root =
                string.IsNullOrWhiteSpace(diagnosticsRoot)
                    ? Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "FORGELINE",
                        "Diagnostics")
                    : diagnosticsRoot;

            Directory.CreateDirectory(root);

            string path =
                Path.Combine(
                    root,
                    $"crash-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}.log");

            var builder =
                new StringBuilder();
            Assembly assembly =
                typeof(ClientDiagnostics).Assembly;
            Version? version =
                assembly.GetName().Version;

            builder.AppendLine("FORGELINE client failure report");
            builder.Append("TimestampUtc: ");
            builder.AppendLine(
                DateTimeOffset.UtcNow.ToString("O"));
            builder.Append("Version: ");
            builder.AppendLine(
                version?.ToString() ??
                "unknown");
            builder.Append("OS: ");
            builder.AppendLine(
                RuntimeInformation.OSDescription);
            builder.Append("ProcessArchitecture: ");
            builder.AppendLine(
                RuntimeInformation.ProcessArchitecture.ToString());
            builder.Append(".NET: ");
            builder.AppendLine(
                RuntimeInformation.FrameworkDescription);
            builder.AppendLine();
            builder.AppendLine(
                exception.ToString());

            File.WriteAllText(
                path,
                builder.ToString());

            return path;
        }
        catch (Exception writeException)
            when (writeException is IOException or
                  UnauthorizedAccessException or
                  NotSupportedException)
        {
            return null;
        }
    }
}
