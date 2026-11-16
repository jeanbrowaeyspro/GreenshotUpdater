namespace GreenshotUpdater;

/// <summary>
/// Mise à jour silencieuse de GreenshotUpdater lui-même depuis ses releases GitHub
/// stables : le lanceur installé est remplacé sans droits administrateur. Le lanceur en
/// place est renommé en <c>.old</c> (un exécutable en cours d'exécution peut être
/// renommé, pas écrasé), le nouveau prend son nom, et l'ancien est supprimé au
/// lancement suivant. Au démarrage suivant, le nouveau lanceur extrait sa propre
/// application (empreinte différente). Toute erreur est seulement journalisée.
/// </summary>
public sealed class SelfUpdate(IReleaseSource releases, Version current, string installedLauncher)
{
    /// <summary>Ancien lanceur renommé, supprimé au lancement suivant.</summary>
    public string OldLauncher => installedLauncher + ".old";

    /// <summary>Téléchargement en cours, jamais exécuté sous ce nom.</summary>
    public string DownloadedLauncher => installedLauncher + ".new";

    /// <summary>Supprime l'ancien lanceur laissé par une mise à jour précédente.</summary>
    public void CleanUp()
    {
        if (File.Exists(OldLauncher) && Delete(OldLauncher))
            Log.Info($"Ancien lanceur supprimé : {OldLauncher}");
    }

    /// <summary>
    /// Remplace le lanceur installé par la dernière version stable si elle est plus
    /// récente. Renvoie vrai si le lanceur a été remplacé ; ne lève jamais d'exception.
    /// </summary>
    public async Task<bool> RunAsync(CancellationToken cancel = default)
    {
        try
        {
            if (!File.Exists(installedLauncher))
                return false;
            var latest = await releases.GetLatestAsync(cancel);
            var currentText = VersionText.Display(current);
            if (latest is null || latest.Version <= current)
            {
                Log.Info($"GreenshotUpdater {currentText} à jour");
                return false;
            }
            var latestText = VersionText.Display(latest.Version);
            if (latest.AssetUrl is null)
            {
                Log.Error($"GreenshotUpdater {latestText} publié sans {AppPaths.LauncherFileName}");
                return false;
            }
            Log.Info($"Mise à jour de GreenshotUpdater {currentText} vers {latestText} : {latest.AssetUrl}");
            await releases.DownloadAsync(latest.AssetUrl, DownloadedLauncher, cancel);
            if (!IsExecutable(DownloadedLauncher))
                throw new InvalidDataException("le fichier téléchargé n'est pas un exécutable");
            Replace();
            Log.Info($"GreenshotUpdater {latestText} installé, actif au prochain lancement");
            return true;
        }
        catch (Exception exc)
        {
            // Réseau, API, fichier verrouillé… : la vérification de Greenshot n'en dépend pas.
            Log.Error($"Mise à jour de GreenshotUpdater : {exc.Message}");
            Delete(DownloadedLauncher);
            return false;
        }
    }

    /// <summary>Vrai si le fichier commence par l'en-tête <c>MZ</c> d'un exécutable Windows.</summary>
    private static bool IsExecutable(string path)
    {
        using var file = File.OpenRead(path);
        return file.Length > 2 && file.ReadByte() == 'M' && file.ReadByte() == 'Z';
    }

    /// <summary>Renomme le lanceur en place puis met le nouveau à sa place (retour arrière en cas d'échec).</summary>
    private void Replace()
    {
        Delete(OldLauncher);
        File.Move(installedLauncher, OldLauncher);
        try
        {
            File.Move(DownloadedLauncher, installedLauncher);
        }
        catch
        {
            File.Move(OldLauncher, installedLauncher);
            throw;
        }
    }

    /// <summary>Supprime le fichier s'il existe ; renvoie faux (et journalise) si c'est impossible.</summary>
    private static bool Delete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Suppression impossible de {path} : {exc.Message}");
            return false;
        }
    }
}
