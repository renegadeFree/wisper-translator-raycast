using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;

namespace WisperTranslator.Core.Models;

public static class ModelStore
{
    private const string ManifestName = ".wisper-manifest.json";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (long Size, DateTime Modified)> Verified = new(StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyList<string> TranslationPairs { get; } = ["it-en", "en-it"];

    public static string RoleFolder(ModelRole role) => role switch
    {
        ModelRole.Asr => "asr", ModelRole.Vad => "vad", ModelRole.Diarization => "diar",
        ModelRole.Translation => "mt", _ => "runtime",
    };

    public static string PathFor(ModelCatalogEntry entry) => entry.ManagedByServer
        ? Translation.TranslationServer.DefaultModelDirectory
        : SafePath(Path.Combine(AppPaths.ModelsDirectory, RoleFolder(entry.Role)), entry.RelativePath);

    public static string PackDirectory(ModelCatalogEntry entry) => SafePath(
        entry.Role == ModelRole.Runtime ? Path.Combine(AppPaths.Root, "tools")
            : Path.Combine(AppPaths.ModelsDirectory, RoleFolder(entry.Role)), entry.Id);

    public static string AsrModelPath(AsrModelSpec spec) => PathFor(ModelCatalog.ById(spec.Id));
    public static Task<string> EnsureAsrModelAsync(AsrModelSpec spec, IProgress<long>? progress = null,
        CancellationToken cancellationToken = default) => EnsureAsync(ModelCatalog.ById(spec.Id), progress, cancellationToken);
    public static Task<string> EnsureVadModelAsync(IProgress<long>? progress = null,
        CancellationToken cancellationToken = default) => EnsureAsync(ModelCatalog.Vad, progress, cancellationToken);

    public static bool IsInstalled(ModelCatalogEntry entry) => entry.ManagedByServer
        ? DirectorySize(PathFor(entry)) > 0
        : entry.Packaging != ModelPackaging.SingleFile ? IsPackInstalled(entry)
        : File.Exists(PathFor(entry)) && new FileInfo(PathFor(entry)).Length == entry.ExpectedSizeBytes;

    public static long InstalledSize(ModelCatalogEntry entry) => entry.ManagedByServer
        ? DirectorySize(PathFor(entry)) : entry.Packaging != ModelPackaging.SingleFile
            ? DirectorySize(PackDirectory(entry)) : File.Exists(PathFor(entry)) ? new FileInfo(PathFor(entry)).Length : 0;

    public static long DirectorySize(string directory) => Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length) : 0;

    public static string? FindFile(ModelCatalogEntry entry, string fileName) => Directory.Exists(PackDirectory(entry))
        ? Directory.EnumerateFiles(PackDirectory(entry), fileName, SearchOption.AllDirectories).FirstOrDefault() : null;

    public static bool IsPackInstalled(ModelCatalogEntry entry) =>
        File.Exists(Path.Combine(PackDirectory(entry), ManifestName))
        && (entry.RequiredFile is null || FindFile(entry, entry.RequiredFile) is not null);

    public static async Task<string> EnsureAsync(ModelCatalogEntry entry, IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (entry.ManagedByServer) throw new InvalidOperationException("Usa MTranServer per scaricare i modelli di traduzione.");
        if (entry.Packaging != ModelPackaging.SingleFile)
            return await EnsurePackAsync(entry, progress is null ? null : new InlineProgress<double>(
                fraction => progress.Report((long)(fraction * entry.ExpectedSizeBytes))), cancellationToken).ConfigureAwait(false);
        var path = PathFor(entry);
        var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                var stamp = (info.Length, info.LastWriteTimeUtc);
                if (Verified.TryGetValue(path, out var previous) && previous == stamp) return path;
                var (ok, _) = await CheckFileAsync(path, entry, cancellationToken).ConfigureAwait(false);
                if (ok) { Verified[path] = stamp; return path; }
            }
            await HttpDownload.DownloadVerifiedAsync(entry.Url, path, entry.ExpectedSizeBytes, entry.Sha256,
                progress, cancellationToken).ConfigureAwait(false);
            var installed = new FileInfo(path);
            Verified[path] = (installed.Length, installed.LastWriteTimeUtc);
            return path;
        }
        finally { gate.Release(); }
    }

    public static async Task<(bool Ok, string Message)> CheckFileAsync(string path, ModelCatalogEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return (false, "Non installato");
        var size = new FileInfo(path).Length;
        if (size < 1024) return (false, "File troppo piccolo");
        if (entry.ExpectedSizeBytes > 0 && size != entry.ExpectedSizeBytes) return (false, "Dimensione diversa dal catalogo");
        var hash = await HttpDownload.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        return hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase) ? (true, "Integro") : (false, "Hash diverso dal catalogo");
    }

    public static async Task<(bool Ok, string Message)> VerifyAsync(ModelCatalogEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (entry.ManagedByServer) return IsInstalled(entry)
            ? (true, "Presente; integrità gestita da MTranServer") : (false, "Non installato");
        if (entry.Packaging == ModelPackaging.SingleFile) return await CheckFileAsync(PathFor(entry), entry, cancellationToken).ConfigureAwait(false);
        return await VerifyPackAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(bool Ok, string Message)> VerifyPackAsync(ModelCatalogEntry entry, CancellationToken cancellationToken)
    {
        if (!IsPackInstalled(entry)) return (false, "Pacchetto non installato o incompleto");
        try
        {
            var directory = PackDirectory(entry);
            var manifest = JsonSerializer.Deserialize<PackManifest>(await File.ReadAllTextAsync(
                Path.Combine(directory, ManifestName), cancellationToken).ConfigureAwait(false));
            if (manifest is null || manifest.ArchiveSha256 != entry.Sha256 || manifest.Files.Count == 0)
                return (false, "Manifest del pacchetto non valido");
            foreach (var (relative, expected) in manifest.Files)
            {
                var file = SafePath(directory, relative);
                if (!File.Exists(file) || !expected.Equals(await HttpDownload.ComputeSha256Async(file, cancellationToken)
                    .ConfigureAwait(false), StringComparison.OrdinalIgnoreCase)) return (false, $"File del pacchetto diverso: {relative}");
            }
            return (true, "Integro");
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException)
        { return (false, $"Pacchetto non leggibile: {exception.Message}"); }
    }

    public static async Task<string> EnsurePackAsync(ModelCatalogEntry entry, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var directory = PackDirectory(entry);
        var gate = Gates.GetOrAdd(directory, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsPackInstalled(entry)) return directory;
            var archive = PathFor(entry);
            await HttpDownload.DownloadVerifiedAsync(entry.Url, archive, entry.ExpectedSizeBytes, entry.Sha256,
                progress is null ? null : new InlineProgress<long>(bytes => progress.Report(bytes / (double)entry.ExpectedSizeBytes)),
                cancellationToken).ConfigureAwait(false);
            var temporary = directory + ".extract-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(temporary);
            try
            {
                await Task.Run(() =>
                {
                    using var zip = ZipFile.OpenRead(archive);
                    if (zip.Entries.Sum(item => item.Length) > 2L * 1024 * 1024 * 1024)
                        throw new InvalidOperationException("Pacchetto troppo grande dopo l'estrazione.");
                    foreach (var item in zip.Entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var path = SafePath(temporary, item.FullName);
                        if (item.FullName.EndsWith('/')) Directory.CreateDirectory(path);
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                            item.ExtractToFile(path);
                        }
                    }
                }, cancellationToken).ConfigureAwait(false);
                var subdirectories = Directory.GetDirectories(temporary);
                var extracted = subdirectories.Length == 1 && Directory.GetFiles(temporary).Length == 0
                    ? subdirectories[0] : temporary;
                if (entry.RequiredFile is { } required && !Directory.EnumerateFiles(extracted, required, SearchOption.AllDirectories).Any())
                    throw new InvalidOperationException($"Il pacchetto non contiene {required}.");
                var files = new Dictionary<string, string>();
                foreach (var file in Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories))
                    files[Path.GetRelativePath(extracted, file)] = await HttpDownload.ComputeSha256Async(file, cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(extracted, ManifestName),
                    JsonSerializer.Serialize(new PackManifest(entry.Sha256, files)), cancellationToken).ConfigureAwait(false);
                Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
                cancellationToken.ThrowIfCancellationRequested();
                var backup = directory + ".backup-" + Guid.NewGuid().ToString("N");
                if (Directory.Exists(directory)) Directory.Move(directory, backup);
                try { Directory.Move(extracted, directory); }
                catch
                {
                    if (Directory.Exists(backup)) Directory.Move(backup, directory);
                    throw;
                }
                if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
                return directory;
            }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
        }
        finally { gate.Release(); }
    }

    public static async Task<string> ImportAsync(string source, ModelCatalogEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.ManagedByServer || entry.Packaging != ModelPackaging.SingleFile)
            throw new InvalidOperationException("Importazione disponibile solo per modelli a file singolo.");
        var destination = PathFor(entry);
        var gate = Gates.GetOrAdd(destination, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = destination + ".import-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            var (ok, message) = await CheckFileAsync(temporary, entry, cancellationToken).ConfigureAwait(false);
            if (!ok) throw new InvalidOperationException(message);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            Verified.TryRemove(destination, out _);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            gate.Release();
        }
    }

    public static void Delete(ModelCatalogEntry entry)
    {
        if (entry.Packaging != ModelPackaging.SingleFile && !entry.ManagedByServer) { DeletePack(entry); return; }
        var path = PathFor(entry);
        var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(0)) throw new InvalidOperationException("Attendi il completamento del download o dell'importazione.");
        try
        {
            if (entry.ManagedByServer) { if (Directory.Exists(path)) Directory.Delete(path, true); }
            else
            {
                Verified.TryRemove(path, out _);
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".part")) File.Delete(path + ".part");
            }
        }
        finally { gate.Release(); }
    }

    public static void DeletePack(ModelCatalogEntry entry)
    {
        var directory = PackDirectory(entry);
        var gate = Gates.GetOrAdd(directory, _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(0)) throw new InvalidOperationException("Attendi il completamento del download o dell'estrazione.");
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            foreach (var path in new[] { PathFor(entry), PathFor(entry) + ".part" })
                if (File.Exists(path)) File.Delete(path);
        }
        finally { gate.Release(); }
    }

    private static string SafePath(string directory, string relative)
    {
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || relative.Contains(':'))
            throw new ArgumentException("Percorso del pacchetto non valido.");
        return path;
    }

    private sealed record PackManifest(string ArchiveSha256, Dictionary<string, string> Files);
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
