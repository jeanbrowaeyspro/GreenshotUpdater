using System.Diagnostics;

namespace GreenshotUpdater;

/// <summary>Résultat d'une vérification : version installée et dernière version stable.</summary>
/// <param name="Error">Message si GitHub n'a pas répondu.</param>
public sealed record UpdateCheck(InstalledGreenshot? Installed, ReleaseInfo? Latest, string? Error)
{
    public bool UpdateAvailable => Installed is not null && Latest is not null && Latest.Version > Installed.Version;

    /// <summary>Question posée à l'utilisateur quand une mise à jour existe.</summary>
    public string OfferText => UpdateAvailable
        ? $"Greenshot {VersionText.Display(Latest!.Version)} est disponible (version installée : {VersionText.Display(Installed!.Version)}). Installer maintenant ?"
        : "";
}

/// <summary>Issue d'une mise à jour, affichée telle quelle.</summary>
public sealed record UpdateOutcome(bool Success, string Message);

/// <summary>Vérification et installation des mises à jour de Greenshot.</summary>
public sealed class UpdateService(
    IRegistryStore registry,
    IReleaseSource releases,
    IGreenshotProcess greenshot,
    IProcessRunner runner,
    Func<string?> findWinget,
    string tempDir)
{
    /// <summary>
    /// Attentes entre les tentatives au démarrage de session (environ une minute en
    /// tout) : le réseau n'est pas toujours prêt à l'ouverture de session.
    /// </summary>
    public static readonly TimeSpan[] StartupRetryDelays =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)];

    /// <summary>Attente entre deux tentatives ; remplacée dans les tests.</summary>
    public Func<TimeSpan, CancellationToken, Task> Wait { get; init; } = Task.Delay;

    /// <summary>
    /// Lit la version installée et interroge GitHub, en réessayant après chacune des
    /// attentes de <paramref name="retryDelays"/> si le réseau ne répond pas.
    /// </summary>
    public async Task<UpdateCheck> CheckAsync(IReadOnlyList<TimeSpan> retryDelays, CancellationToken cancel = default)
    {
        var installed = InstalledGreenshot.Find(registry);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var latest = await releases.GetLatestAsync(cancel);
                Log.Info($"Greenshot installé : {Describe(installed?.Version)}, dernière version stable : {Describe(latest?.Version)}");
                return new UpdateCheck(installed, latest, null);
            }
            catch (Exception exc) when (exc is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
            {
                Log.Error($"GitHub, tentative {attempt + 1} : {exc.Message}");
                if (attempt >= retryDelays.Count)
                    return new UpdateCheck(installed, null, $"GitHub ne répond pas : {exc.Message}");
                await Wait(retryDelays[attempt], cancel);
            }
        }
    }

    private static string Describe(Version? version) => version is null ? "aucune" : VersionText.Display(version);

    /// <summary>Vrai si winget est présent : sinon, l'installeur GitHub est proposé.</summary>
    public bool WingetAvailable => findWinget() is not null;

    /// <summary>Mise à jour par winget (assistant interactif de Greenshot).</summary>
    public Task<UpdateOutcome> InstallWithWingetAsync(CancellationToken cancel = default) =>
        InstallAsync(async () => await runner.RunAsync(
            Winget.UpgradeCommand(findWinget() ?? throw new InvalidOperationException("winget introuvable")), cancel));

    /// <summary>Téléchargement de l'installeur de la release GitHub, puis lancement.</summary>
    public Task<UpdateOutcome> InstallFromGitHubAsync(ReleaseInfo release, CancellationToken cancel = default) =>
        InstallAsync(async () =>
        {
            if (release.InstallerUrl is null || release.InstallerName is null)
                throw new InvalidOperationException("La release GitHub ne contient pas d'installeur.");
            var installer = Path.Combine(tempDir, release.InstallerName);
            Log.Info($"Téléchargement de {release.InstallerUrl}");
            await releases.DownloadAsync(release.InstallerUrl, installer, cancel);
            // ShellExecute : l'installeur demande lui-même l'élévation (UAC).
            return await runner.RunAsync(new ProcessStartInfo(installer) { UseShellExecute = true }, cancel);
        });

    /// <summary>
    /// Ferme Greenshot, exécute l'installation, relance Greenshot depuis son dossier
    /// d'installation (relu dans le registre) et formule le résultat.
    /// </summary>
    private async Task<UpdateOutcome> InstallAsync(Func<Task<int>> install)
    {
        var closedPath = greenshot.Close();
        UpdateOutcome outcome;
        try
        {
            var code = await install();
            Log.Info($"Installation terminée, code {Winget.ExitCodeText(code)}");
            var version = InstalledGreenshot.Find(registry)?.Version;
            outcome = code == 0
                ? new UpdateOutcome(true, $"Greenshot est à jour (version {Describe(version)}).")
                : new UpdateOutcome(false, $"La mise à jour a échoué (code {Winget.ExitCodeText(code)}).");
        }
        catch (Exception exc) when (exc is not OperationCanceledException)
        {
            Log.Error($"Installation : {exc}");
            outcome = new UpdateOutcome(false, $"La mise à jour a échoué : {exc.Message}");
        }

        var exe = InstalledGreenshot.Find(registry)?.ExePath ?? closedPath;
        if (exe is not null && File.Exists(exe))
            greenshot.Start(exe);
        return outcome;
    }
}
