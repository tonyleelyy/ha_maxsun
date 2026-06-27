namespace HaMaxsun.Service;

internal sealed class BridgeLogger : IDisposable
{
    private readonly object _lock = new();
    private readonly StreamWriter _writer;

    public BridgeLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, $"bridge-{DateTime.Now:yyyyMMdd}.log");
        _writer = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);
    public void Error(Exception ex, string message) => Write("ERROR", $"{message}: {ex}");

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.Dispose();
        }
    }

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        lock (_lock)
        {
            Console.WriteLine(line);
            _writer.WriteLine(line);
        }
    }
}

