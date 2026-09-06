using System.Security.Cryptography;
using System.Text.Json;

namespace Trophy.Catalogue.Services;

// JSON archive files and the operational ledger share one writer process.
public sealed class DataDirectoryLease : IDisposable
{
    private readonly FileStream stream;
    public DataDirectoryLease(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        try { stream = new FileStream(Path.Combine(dataRoot, ".instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception) { throw new InvalidOperationException("This DATA_PATH is already open. Run one archive instance per data directory; stop it before backup or restore.", exception); }
    }
    public void Dispose() => stream.Dispose();
}

public static class DataMaintenance
{
    private sealed record BackupManifest(int Version, DateTimeOffset CreatedAt, Dictionary<string, string> Files);
    public static bool TryRun(string[] args)
    {
        if (!args.Contains("--backup-data") && !args.Contains("--restore-data")) return false;
        string Required(string option)
        {
            var index = Array.IndexOf(args, option);
            if (index < 0 || index + 1 >= args.Length || args[index + 1].StartsWith("--")) throw new ArgumentException($"Supply {option} with an absolute directory.");
            if (!Path.IsPathFullyQualified(args[index + 1])) throw new ArgumentException($"{option} requires an absolute directory.");
            return Path.GetFullPath(args[index + 1]).TrimEnd(Path.DirectorySeparatorChar);
        }
        if (args.Contains("--backup-data") && args.Contains("--restore-data")) throw new ArgumentException("Choose backup or restore, not both.");
        if (args.Contains("--backup-data")) Backup(Required("--data-path"), Required("--backup-data"));
        else Restore(Required("--restore-data"), Required("--destination-data"));
        return true;
    }
    public static void Backup(string source, string destination)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("The source DATA_PATH does not exist.");
        EnsureSeparate(source, destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new backup directory; existing backups are never overwritten.");
        using var lease = new DataDirectoryLease(source);
        var files = EnumerateFiles(source).Where(path => Path.GetFileName(path) != ".instance.lock").ToList();
        Directory.CreateDirectory(destination);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in files)
        {
            var relative = Path.GetRelativePath(source, path).Replace('\\', '/');
            var target = ContainedPath(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, overwrite: false);
            var hash = Hash(path);
            if (Hash(target) != hash) throw new IOException("The source changed during backup. Stop all older application versions and take a fresh backup.");
            hashes.Add(relative, hash);
        }
        File.WriteAllText(Path.Combine(destination, "backup-manifest.json"), JsonSerializer.Serialize(new BackupManifest(1, DateTimeOffset.UtcNow, hashes), new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Backup verified: {hashes.Count} files in {destination}. Protect this backup like the live archive: it includes account credentials and encryption keys.");
    }
    public static void Restore(string source, string destination)
    {
        EnsureSeparate(source, destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Restore requires a new destination directory. It never replaces an existing archive.");
        var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(Path.Combine(source, "backup-manifest.json"))) ?? throw new IOException("Invalid backup manifest.");
        if (manifest.Version != 1 || manifest.Files.Count == 0) throw new IOException("Unsupported or empty backup manifest.");
        foreach (var entry in manifest.Files)
        {
            var path = ContainedPath(source, entry.Key);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || Hash(path) != entry.Value) throw new IOException("Backup verification failed; nothing was restored.");
            _ = ContainedPath(destination, entry.Key);
        }
        Directory.CreateDirectory(destination);
        using var lease = new DataDirectoryLease(destination);
        foreach (var entry in manifest.Files)
        {
            var target = ContainedPath(destination, entry.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(ContainedPath(source, entry.Key), target, overwrite: false);
            if (Hash(target) != entry.Value) throw new IOException("Restored file failed verification. Do not start the application against this directory.");
        }
        Console.WriteLine($"Restore verified: {manifest.Files.Count} files in {destination}. Set DATA_PATH explicitly to this new directory after review.");
    }
    private static IEnumerable<string> EnumerateFiles(string root)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked files or directories are not supported in archive backups.");
            if ((attributes & FileAttributes.Directory) != 0) foreach (var child in EnumerateFiles(path)) yield return child;
            else yield return path;
        }
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static string ContainedPath(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/', '\\').Any(part => part is ".." or "." or "")) throw new IOException("Unsafe backup path.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Backup path leaves its directory.");
        return path;
    }
    private static void EnsureSeparate(string source, string destination)
    {
        var a = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var b = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase)) throw new IOException("Use separate source and destination directories.");
    }
}
