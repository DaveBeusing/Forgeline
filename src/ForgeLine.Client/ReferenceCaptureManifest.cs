namespace ForgeLine.Client;

/// <summary>Publishes a complete progress snapshot without truncating an observer's open file.</summary>
internal static class ReferenceCaptureManifest
{
    internal static void Write(string path, string contents)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, contents);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(temporary, path, destinationBackupFileName: null);
                    else File.Move(temporary, path);
                    break;
                }
                catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 && attempt < 20)
                { Thread.Sleep(10); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
