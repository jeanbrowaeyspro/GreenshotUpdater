using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using GreenshotUpdater.ViewModels;

// Les tests partagent des variables d'environnement et le fil STA de WPF : un par un.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace GreenshotUpdater.Tests;

/// <summary>Dossiers temporaires et faux services communs aux tests.</summary>
public static class Fixtures
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "GreenshotUpdaterTests");

    /// <summary>Les vrais dossiers de Jean ne sont jamais lus ni écrits par les tests.</summary>
    [ModuleInitializer]
    internal static void IsolateEnvironment()
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;   // schéma pack:// connu sans Application
        IsolateFolders();
    }

    private static void IsolateFolders()
    {
        foreach (var variable in new[] { "APPDATA", "LOCALAPPDATA" })
        {
            var folder = Path.Combine(Root, "env", variable.ToLowerInvariant());
            Directory.CreateDirectory(folder);
            Environment.SetEnvironmentVariable(variable, folder);
        }
    }

    /// <summary>Nouveau dossier vide propre à un test.</summary>
    public static string NewFolder([CallerMemberName] string name = "")
    {
        var folder = Path.Combine(Root, $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Jeu complet d'emplacements dans un dossier temporaire.</summary>
    public static AppPaths NewPaths([CallerMemberName] string name = "")
    {
        var root = NewFolder(name);
        return new AppPaths(
            InstallDir: Path.Combine(root, "local", "Programs", AppPaths.AppName),
            SettingsFile: Path.Combine(root, "roaming", AppPaths.AppName, "settings.json"),
            DataDir: Path.Combine(root, "local", AppPaths.AppName),
            GreenshotIni: Path.Combine(root, "roaming", "Greenshot", "Greenshot.ini"),
            DesktopDir: Path.Combine(root, "Bureau"),
            StartMenuDir: Path.Combine(root, "Programmes"),
            TempDir: Path.Combine(root, "temp"));
    }

    /// <summary>Extrait de Greenshot.ini tel que Greenshot l'écrit (UTF-8 avec BOM, CRLF).</summary>
    public const string IniSample =
        "; Greenshot core configuration\r\n"
        + "[Core]\r\n"
        + "; The language in IETF format (e.g. en-US)\r\n"
        + "Language=fr-FR\r\n"
        + "; How many days between every update check? (0=no checks)\r\n"
        + "UpdateCheckInterval=14\r\n"
        + "LastUpdateCheck=09/30/2026 06:33:23\r\n"
        + "[Editor]\r\n"
        + "UpdateCheckInterval=99\r\n";

    public const string ReleaseJson = """
        {
          "tag_name": "v1.3.323",
          "name": "Greenshot 1.3.323",
          "draft": false,
          "prerelease": false,
          "assets": [
            { "name": "Greenshot-INSTALLER-1.3.323-RELEASE.exe",
              "browser_download_url": "https://github.com/greenshot/greenshot/releases/download/v1.3.323/Greenshot-INSTALLER-1.3.323-RELEASE.exe" },
            { "name": "Greenshot-PORTABLE-1.3.323-RELEASE.zip",
              "browser_download_url": "https://github.com/greenshot/greenshot/releases/download/v1.3.323/Greenshot-PORTABLE-1.3.323-RELEASE.zip" }
          ]
        }
        """;

    /// <summary>Exécute une action sur un fil STA (exigé par WPF) et propage ses exceptions.</summary>
    public static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exc) { error = exc; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw new AggregateException(error);
    }
}

/// <summary>Registre en mémoire.</summary>
public sealed class FakeRegistry : IRegistryStore
{
    private readonly Dictionary<(RegistryRoot, string), Dictionary<string, string>> _keys = [];

    private static (RegistryRoot, string) Id(RegistryRoot root, string path) => (root, path.ToLowerInvariant());

    public IReadOnlyList<string> SubKeyNames(RegistryRoot root, string path)
    {
        var prefix = path.ToLowerInvariant() + @"\";
        return _keys.Keys.Where(k => k.Item1 == root && k.Item2.StartsWith(prefix))
                    .Select(k => k.Item2[prefix.Length..].Split('\\')[0]).Distinct().ToList();
    }

    public string? GetString(RegistryRoot root, string path, string name) =>
        _keys.TryGetValue(Id(root, path), out var values) && values.TryGetValue(name, out var value) ? value : null;

    public void SetString(RegistryRoot root, string path, string name, string value)
    {
        if (!_keys.TryGetValue(Id(root, path), out var values))
            _keys[Id(root, path)] = values = [];
        values[name] = value;
    }

    public void DeleteValue(RegistryRoot root, string path, string name)
    {
        if (_keys.TryGetValue(Id(root, path), out var values))
            values.Remove(name);
    }

    /// <summary>Entrée de désinstallation de Greenshot.</summary>
    public FakeRegistry WithGreenshot(string version, string? location, RegistryRoot root = RegistryRoot.LocalMachine64,
                                      string key = "Greenshot_is1")
    {
        var path = $@"{InstalledGreenshot.UninstallPath}\{key}";
        SetString(root, path, "DisplayName", $"Greenshot {version}");
        SetString(root, path, "DisplayVersion", version);
        if (location is not null)
            SetString(root, path, "InstallLocation", location);
        return this;
    }
}

/// <summary>Raccourcis : un fichier texte contenant la cible.</summary>
public sealed class FakeShortcuts : IShortcutWriter
{
    public void Create(string shortcutPath, string target, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        File.WriteAllText(shortcutPath, target);
    }
}

/// <summary>Greenshot simulé : mémorise les fermetures et relances.</summary>
public sealed class FakeGreenshot(string? runningPath) : IGreenshotProcess
{
    public string? RunningPath { get; private set; } = runningPath;
    public List<string> Events { get; } = [];
    /// <summary>Action exécutée à la fermeture (Greenshot réécrit son .ini).</summary>
    public Action? OnClose { get; init; }

    public string? Close()
    {
        Events.Add("close");
        if (RunningPath is null)
            return null;
        OnClose?.Invoke();
        var path = RunningPath;
        RunningPath = null;
        return path;
    }

    public bool IsRunning => RunningPath is not null;

    public void Start(string exePath)
    {
        Events.Add($"start {exePath}");
        RunningPath = exePath;
    }

    /// <summary>Greenshot relancé hors de GreenshotUpdater (par son installeur).</summary>
    public void StartedElsewhere(string exePath) => RunningPath = exePath;
}

/// <summary>Programmes externes simulés : mémorise les lancements, renvoie un code donné.</summary>
public sealed class FakeRunner(int exitCode = 0) : IProcessRunner
{
    public List<ProcessStartInfo> Runs { get; } = [];
    /// <summary>Action exécutée pendant le "programme" (mise à jour du registre…).</summary>
    public Action? During { get; init; }

    /// <summary>Fin du "programme" : immédiate si null, sinon quand la tâche se termine.</summary>
    public Task? Until { get; init; }

    public async Task<int> RunAsync(ProcessStartInfo start, CancellationToken cancel = default)
    {
        Runs.Add(start);
        During?.Invoke();
        if (Until is not null)
            await Until;
        return exitCode;
    }

    public void Start(ProcessStartInfo start) => Runs.Add(start);
}

/// <summary>GitHub simulé : réponses successives (une exception = réseau absent).</summary>
public sealed class FakeReleases(params Func<ReleaseInfo?>[] answers) : IReleaseSource
{
    public int Calls { get; private set; }
    public List<(string Url, string Destination)> Downloads { get; } = [];

    public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancel = default) =>
        Task.FromResult(answers[Math.Min(Calls++, answers.Length - 1)]());

    public Task DownloadAsync(string url, string destination, CancellationToken cancel = default)
    {
        Downloads.Add((url, destination));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, "installeur");
        return Task.CompletedTask;
    }

    public static ReleaseInfo? Latest() => ReleaseInfo.Parse(Fixtures.ReleaseJson);

    public static ReleaseInfo? Offline() => throw new HttpRequestException("réseau absent");
}

/// <summary>Messages simulés : réponse fixée aux confirmations, textes mémorisés.</summary>
public sealed class FakeDialogs(bool answer = true) : IDialogs
{
    public List<string> Shown { get; } = [];

    public bool Confirm(string text, string yes = "Oui", string no = "Non")
    {
        Shown.Add(text);
        return answer;
    }

    public void Info(string text) => Shown.Add(text);

    public void Error(string text) => Shown.Add(text);
}
