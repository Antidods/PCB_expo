using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcbExpo.Core;

public static class LocalStorage
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcbExpo");
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class ProjectPersistenceService
{
    public void Save(ProjectModel project, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(project, LocalStorage.JsonOptions));
    }

    public ProjectModel Load(string path) => JsonSerializer.Deserialize<ProjectModel>(
        File.ReadAllText(path), LocalStorage.JsonOptions)
        ?? throw new InvalidDataException("Не удалось прочитать проект PCB Expo.");
}

public sealed class BlankProfileService
{
    private string FilePath => Path.Combine(LocalStorage.DirectoryPath, "blank-profiles.json");

    public IReadOnlyList<BlankProfile> Load()
    {
        if (!File.Exists(FilePath)) return [];
        return JsonSerializer.Deserialize<List<BlankProfile>>(File.ReadAllText(FilePath), LocalStorage.JsonOptions) ?? [];
    }

    public void Save(BlankProfile profile)
    {
        var profiles = Load().Where(x => x.Name != profile.Name).ToList();
        profiles.Add(profile);
        Directory.CreateDirectory(LocalStorage.DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(profiles, LocalStorage.JsonOptions));
    }
}

public sealed class ExposureProfileService
{
    private string FilePath => Path.Combine(LocalStorage.DirectoryPath, "exposure-profiles.json");

    public ExposureSettings Load()
    {
        if (!File.Exists(FilePath)) return new ExposureSettings();
        return JsonSerializer.Deserialize<ExposureSettings>(File.ReadAllText(FilePath), LocalStorage.JsonOptions)
            ?? new ExposureSettings();
    }

    public void Save(ExposureSettings exposure)
    {
        Directory.CreateDirectory(LocalStorage.DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(exposure, LocalStorage.JsonOptions));
    }
}

public sealed class ApplicationLog
{
    private readonly string _path = System.IO.Path.Combine(LocalStorage.DirectoryPath, "app.log");
    public string LogPath => _path;
    public void Write(string message)
    {
        Directory.CreateDirectory(LocalStorage.DirectoryPath);
        File.AppendAllText(_path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
    }
    public void Error(Exception error) => Write($"ERROR {error}");
}
