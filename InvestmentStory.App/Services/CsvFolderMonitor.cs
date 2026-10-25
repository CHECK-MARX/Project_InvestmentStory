using System.IO;

namespace InvestmentStory.App.Services;

/// <summary>Detects newly stable CSV files. It never reads their content or imports them.</summary>
public sealed class CsvFolderMonitor : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, (long Length, DateTime LastWriteUtc)> _reported =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public CsvFolderMonitor(string folder)
    {
        Folder = Path.GetFullPath(folder);
        if (!Directory.Exists(Folder)) throw new DirectoryNotFoundException(Folder);
        foreach (var path in Directory.EnumerateFiles(Folder, "*.csv", SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(path);
            _reported[info.FullName] = (info.Length, info.LastWriteTimeUtc);
        }
        _watcher = new FileSystemWatcher(Folder)
        {
            Filter = "*.csv",
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _watcher.Created += OnChanged;
        _watcher.Changed += OnChanged;
        _watcher.Renamed += OnRenamed;
    }

    public string Folder { get; }
    public event Action<string>? NewCsvDetected;

    private void OnChanged(object sender, FileSystemEventArgs e) => _ = CheckStableAsync(e.FullPath);
    private void OnRenamed(object sender, RenamedEventArgs e) => _ = CheckStableAsync(e.FullPath);

    private async Task CheckStableAsync(string path)
    {
        try
        {
            if (!string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Folder, StringComparison.OrdinalIgnoreCase)) return;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                await Task.Delay(500, _stop.Token);
                if (!File.Exists(path)) continue;
                var first = new FileInfo(path);
                var signature = (first.Length, first.LastWriteTimeUtc);
                if (signature.Length == 0) continue;
                await Task.Delay(500, _stop.Token);
                if (!File.Exists(path)) continue;
                var second = new FileInfo(path);
                if (signature != (second.Length, second.LastWriteTimeUtc)) continue;
                // A file copied from a broker may still be locked after its size stabilizes.
                try { using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) { } }
                catch (IOException) { continue; }
                lock (_sync)
                {
                    if (_reported.TryGetValue(path, out var old) && old == signature) return;
                    _reported[path] = signature;
                }
                NewCsvDetected?.Invoke(path);
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _watcher.Dispose();
        _stop.Dispose();
    }
}
