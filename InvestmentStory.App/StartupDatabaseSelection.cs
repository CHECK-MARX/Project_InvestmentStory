using System;
using System.IO;

namespace InvestmentStory.App;

public static class StartupDatabaseSelection
{
    public static string? ResolveIsolatedPath(string[] args, string defaultDatabasePath)
    {
        if (args.Length == 0 || !string.Equals(args[0], "--isolated-db", StringComparison.Ordinal))
            return null;
        if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
            throw new ArgumentException("隔離DBの既存ファイルを指定してください。");
        var path = Path.GetFullPath(args[1]);
        if (string.Equals(path, Path.GetFullPath(defaultDatabasePath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("通常のDBを隔離DBとして指定できません。");
        if (!File.Exists(path))
            throw new FileNotFoundException("隔離DBが見つかりません。", path);
        return path;
    }
}
