namespace DLSSUpdater.Core;

public static class Log
{
    private static readonly object Gate = new();
    public static event Action<string>? Line;

    public static void Info(string message) => Write(message);
    public static void Error(string message, Exception? ex = null) =>
        Write(ex is null ? $"ERROR {message}" : $"ERROR {message}: {ex.Message}", ex);

    private static void Write(string message, Exception? ex = null)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine + (ex is null ? "" : ex + Environment.NewLine));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Line?.Invoke(line);
    }

    public static void TrimFile(long maxBytes = 2_000_000)
    {
        try
        {
            var fi = new FileInfo(AppPaths.LogFile);
            if (fi.Exists && fi.Length > maxBytes) fi.Delete();
        }
        catch (IOException) { }
    }
}
