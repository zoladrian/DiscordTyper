namespace TyperBot.Infrastructure.Data;

/// <summary>
/// Normalizes SQLite connection strings so relative <c>Data Source</c> paths resolve to the
/// application base directory (typically next to the published DLL), not the process working directory.
/// </summary>
public static class SqliteConnectionHelper
{
    public static string WithAbsoluteDataSource(string connectionString, string applicationBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(applicationBaseDirectory))
            return connectionString;

        var trimmed = connectionString.Trim();
        foreach (var prefix in new[] { "Data Source=", "DataSource=" })
        {
            var idx = trimmed.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;

            var afterPrefix = trimmed[(idx + prefix.Length)..].Trim();
            var semicolon = afterPrefix.IndexOf(';');
            var pathToken = semicolon >= 0 ? afterPrefix[..semicolon].Trim() : afterPrefix;
            var rest = semicolon >= 0 ? afterPrefix[semicolon..] : "";

            if (pathToken.Length >= 2 && pathToken[0] == '"' && pathToken[^1] == '"')
                pathToken = pathToken[1..^1];

            if (pathToken.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
                return connectionString;

            if (pathToken.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return connectionString;

            if (Path.IsPathRooted(pathToken))
                return connectionString;

            var fullPath = Path.GetFullPath(Path.Combine(applicationBaseDirectory, pathToken));
            return $"{prefix}{fullPath}{rest}";
        }

        return connectionString;
    }
}
