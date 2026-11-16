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

/// <summary>Issue d'une mise à jour.</summary>
/// <param name="Cancelled">Élévation (UAC) refusée ou installation annulée par l'utilisateur : rien à afficher.</param>
/// <param name="Message">Confirmation (succès) ou erreur, affichée sauf en cas d'annulation.</param>
public sealed record UpdateOutcome(bool Success, bool Cancelled, string Message);

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

    /// <summary>
    /// Installation silencieuse de <paramref name="release"/> : par winget s'il est
    /// présent, sinon par l'installeur de la release GitHub, téléchargé puis lancé.
    /// </summary>
    public Task<UpdateOutcome> InstallAsync(ReleaseInfo release, CancellationToken cancel = default) =>
        InstallAsync(release.Version, findWinget() is { } winget
            ? () => runner.RunAsync(Winget.UpgradeCommand(winget), cancel)
            : async () =>
            {
                if (release.AssetUrl is null || release.AssetName is null)
                    throw new InvalidOperationException("La release GitHub ne contient pas d'installeur.");
                var installer = Path.Combine(tempDir, release.AssetName);
                Log.Info($"Téléchargement de {release.AssetUrl}");
                await releases.DownloadAsync(release.AssetUrl, installer, cancel);
                return await runner.RunAsync(InnoSetup.SilentCommand(installer), cancel);
            });

    /// <summary>Code Windows ERROR_CANCELLED : élévation (UAC) refusée.</summary>
    public const int ErrorCancelled = 1223;

    /// <summary>
    /// Codes de sortie d'une annulation par l'utilisateur : winget
    /// (APPINSTALLER_CLI_ERROR_INSTALL_CANCELLED_BY_USER), ERROR_CANCELLED seul ou en
    /// HRESULT, installeur Inno Setup de Greenshot (2 : annulé avant l'installation,
    /// 5 : annulé pendant).
    /// </summary>
    public static readonly int[] CancelCodes = [unchecked((int)0x8A15010C), ErrorCancelled, unchecked((int)0x800704C7), 2, 5];

    /// <summary>
    /// Ferme Greenshot, exécute l'installation et attend sa fin. Succès : relance
    /// Greenshot s'il ne tourne pas, puis relit la version installée et la compare à
    /// <paramref name="expected"/>. Échec ou annulation : relance Greenshot s'il
    /// tournait avant. Le résultat est noté dans le journal.
    /// </summary>
    private async Task<UpdateOutcome> InstallAsync(Version expected, Func<Task<int>> install)
    {
        var closedPath = greenshot.Close();
        UpdateOutcome outcome;
        try
        {
            var code = await install();
            var text = Winget.ExitCodeText(code);
            if (code == 0)
            {
                Restart(closedPath);
                outcome = new UpdateOutcome(true, false, InstalledText(expected, InstalledGreenshot.Find(registry)?.Version));
            }
            else
                outcome = new UpdateOutcome(false, CancelCodes.Contains(code), $"La mise à jour a échoué (code {text}).");
            Log.Info($"Installation terminée, code {text} : {(outcome.Cancelled ? "annulée par l'utilisateur" : outcome.Message)}");
        }
        catch (Exception exc) when (exc is not OperationCanceledException)
        {
            Log.Error($"Installation : {exc}");
            var cancelled = exc is System.ComponentModel.Win32Exception { NativeErrorCode: ErrorCancelled };
            outcome = new UpdateOutcome(false, cancelled, $"La mise à jour a échoué : {exc.Message}");
        }

        if (!outcome.Success && closedPath is not null)
            Restart(closedPath);
        return outcome;
    }

    /// <summary>Confirmation de fin, avec l'écart si la version relue n'est pas celle attendue.</summary>
    public static string InstalledText(Version expected, Version? installed) => installed == expected
        ? $"Greenshot {VersionText.Display(expected)} est installé."
        : $"L'installation de Greenshot {VersionText.Display(expected)} s'est terminée, "
          + $"mais la version installée relue est {Describe(installed)}.";

    /// <summary>
    /// Relance Greenshot depuis son dossier d'installation (relu dans le registre),
    /// sinon depuis son chemin avant fermeture, s'il ne tourne pas déjà.
    /// </summary>
    private void Restart(string? closedPath)
    {
        var exe = InstalledGreenshot.Find(registry)?.ExePath ?? closedPath;
        if (greenshot.IsRunning)
            Log.Info("Greenshot tourne déjà : pas de relance");
        else if (exe is not null && File.Exists(exe))
            greenshot.Start(exe);
    }
}
