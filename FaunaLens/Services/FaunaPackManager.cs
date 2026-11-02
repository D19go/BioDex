using System.Text.Json;
using FaunaLens.Models;

namespace FaunaLens.Services;

public sealed record InstalledFaunaPack(
    string PackId,
    string PackName,
    int SpeciesCount,
    int ReferencesPerSpecies,
    string DirectoryPath);

public sealed class FaunaPackManager
{
    const string PreferencesName = "faunalens_packs";
    const string ActivePackKey = "active_pack_id";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    readonly FaunaPackReader reader = new();

    public async Task<FaunaPackData> InstallAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(FileSystem.PackDirectory);
        var pack = await reader.InstallAsync(
            sourcePath,
            FileSystem.PackDirectory,
            cancellationToken);

        SetActivePackId(pack.Manifest.PackId);
        return pack;
    }

    public async Task<IReadOnlyList<InstalledFaunaPack>> ListAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(FileSystem.PackDirectory);
        var directories = Directory.GetDirectories(FileSystem.PackDirectory);
        var output = new List<InstalledFaunaPack>();

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                await using var stream = File.OpenRead(manifestPath);
                var manifest = await JsonSerializer.DeserializeAsync<FaunaPackManifest>(
                    stream,
                    JsonOptions,
                    cancellationToken);

                if (manifest is null || string.IsNullOrWhiteSpace(manifest.PackId))
                    continue;

                output.Add(new InstalledFaunaPack(
                    manifest.PackId,
                    manifest.PackName,
                    manifest.SpeciesCount,
                    manifest.ReferencesPerSpecies,
                    directory));
            }
            catch
            {
                // Um pacote inválido não impede os outros de aparecerem.
            }
        }

        return output
            .OrderBy(x => x.PackName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<FaunaPackData> LoadAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var pack = await reader.LoadInstalledAsync(directoryPath, cancellationToken);
        SetActivePackId(pack.Manifest.PackId);
        return pack;
    }

    public string? GetActivePackId()
    {
        var preferences = Android.App.Application.Context.GetSharedPreferences(
            PreferencesName,
            Android.Content.FileCreationMode.Private);

        return preferences?.GetString(ActivePackKey, null);
    }

    public void SetActivePackId(string packId)
    {
        var preferences = Android.App.Application.Context.GetSharedPreferences(
            PreferencesName,
            Android.Content.FileCreationMode.Private);

        preferences?.Edit()?.PutString(ActivePackKey, packId)?.Apply();
    }

    public void Remove(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return;

        var fullDirectory = Path.GetFullPath(directoryPath);
        var root = Path.GetFullPath(FileSystem.PackDirectory) + Path.DirectorySeparatorChar;

        if (!fullDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O pacote está fora da pasta de instalação do FaunaLens.");

        Directory.Delete(fullDirectory, true);

        var active = GetActivePackId();
        if (!string.IsNullOrWhiteSpace(active))
        {
            var activeDirectory = Path.Combine(FileSystem.PackDirectory, active);
            if (!Directory.Exists(activeDirectory))
            {
                var preferences = Android.App.Application.Context.GetSharedPreferences(
                    PreferencesName,
                    Android.Content.FileCreationMode.Private);
                preferences?.Edit()?.Remove(ActivePackKey)?.Apply();
            }
        }
    }
}
