using System.Diagnostics;

namespace GreenshotUpdater;

/// <summary>
/// Installation dans <c>%LOCALAPPDATA%\Programs\GreenshotUpdater</c>, application des
/// options et désinstallation. Aucun droit administrateur n'est nécessaire.
/// </summary>
public sealed class Setup(AppPaths paths, IRegistryStore registry, IShortcutWriter shortcuts, GreenshotConfig greenshot)
{
    public const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValue = AppPaths.AppName;
    public const string ShortcutDescription = "Réglages de GreenshotUpdater";

    public AppPaths Paths => paths;

    /// <summary>Vrai si le lanceur est présent dans le dossier d'installation.</summary>
    public bool IsInstalled => File.Exists(paths.InstalledLauncher);

    /// <summary>Commande inscrite dans Run : lanceur installé, vérification silencieuse.</summary>
    public string RunCommand => $"\"{paths.InstalledLauncher}\" {Switches.Startup}";

    /// <summary>
    /// Copie le lanceur et l'application dans le dossier d'installation, puis applique
    /// les options. Renvoie les avertissements à afficher.
    /// </summary>
    public IReadOnlyList<string> Install(string launcherSource, string appSource, AppSettings settings)
    {
        Directory.CreateDirectory(paths.InstallDir);
        Copy(launcherSource, paths.InstalledLauncher);
        Copy(appSource, paths.InstalledApp);
        Log.Info($"Installé dans {paths.InstallDir}");
        return Apply(settings);
    }

    private static void Copy(string source, string destination)
    {
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            File.Copy(source, destination, overwrite: true);
    }

    /// <summary>Applique les options et les mémorise. Renvoie les avertissements à afficher.</summary>
    public IReadOnlyList<string> Apply(AppSettings settings)
    {
        if (settings.RunAtStartup)
            registry.SetString(RegistryRoot.CurrentUser, RunPath, RunValue, RunCommand);
        else
            registry.DeleteValue(RegistryRoot.CurrentUser, RunPath, RunValue);
        var warning = greenshot.SetUpdateCheck(settings.DisableGreenshotUpdateCheck);
        SetShortcut(paths.DesktopShortcut, settings.DesktopShortcut);
        SetShortcut(paths.StartMenuShortcut, settings.StartMenuShortcut);
        settings.Save(paths.SettingsFile);
        Log.Info($"Options appliquées : {settings.ToJson().ReplaceLineEndings(" ")}");
        return warning is null ? [] : [warning];
    }

    private void SetShortcut(string path, bool wanted)
    {
        if (wanted)
            shortcuts.Create(path, paths.InstalledLauncher, ShortcutDescription);
        else
            File.Delete(path);
    }

    /// <summary>
    /// Retire l'entrée Run, remet la vérification de Greenshot, supprime raccourcis et
    /// réglages. Le dossier d'installation, qui contient l'exécutable en cours, est
    /// supprimé après la fermeture par la commande renvoyée (null s'il n'existe pas).
    /// </summary>
    public ProcessStartInfo? Uninstall()
    {
        registry.DeleteValue(RegistryRoot.CurrentUser, RunPath, RunValue);
        greenshot.SetUpdateCheck(disable: false);
        File.Delete(paths.DesktopShortcut);
        File.Delete(paths.StartMenuShortcut);
        File.Delete(paths.SettingsFile);
        var settingsDir = Path.GetDirectoryName(paths.SettingsFile)!;
        if (Directory.Exists(settingsDir) && !Directory.EnumerateFileSystemEntries(settingsDir).Any())
            Directory.Delete(settingsDir);
        Log.Info("Désinstallation");
        return Directory.Exists(paths.InstallDir) ? DeferredDeletion(paths.InstallDir) : null;
    }

    /// <summary>
    /// Commande cachée qui attend quelques secondes (fermeture de l'application) puis
    /// supprime le dossier.
    /// </summary>
    public static ProcessStartInfo DeferredDeletion(string folder) =>
        new("cmd.exe", $"/d /c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{folder}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath(),
        };
}
