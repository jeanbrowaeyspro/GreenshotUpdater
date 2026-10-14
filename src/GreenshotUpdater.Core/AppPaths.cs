namespace GreenshotUpdater;

/// <summary>
/// Emplacements utilisés par l'application. Les tests en construisent un jeu complet
/// dans un dossier temporaire : rien n'est lu ni écrit dans les vrais dossiers.
/// </summary>
public sealed record AppPaths(
    string InstallDir,
    string SettingsFile,
    string LogFile,
    string GreenshotIni,
    string DesktopDir,
    string StartMenuDir,
    string TempDir)
{
    public const string AppName = "GreenshotUpdater";
    public const string LauncherFileName = AppName + ".exe";
    public const string AppFileName = AppName + ".App.exe";
    public const string ShortcutFileName = AppName + ".lnk";

    public string InstalledLauncher => Path.Combine(InstallDir, LauncherFileName);
    public string InstalledApp => Path.Combine(InstallDir, AppFileName);
    public string DesktopShortcut => Path.Combine(DesktopDir, ShortcutFileName);
    public string StartMenuShortcut => Path.Combine(StartMenuDir, ShortcutFileName);

    /// <summary>Emplacements réels de l'utilisateur courant.</summary>
    public static AppPaths FromEnvironment()
    {
        var local = Folder("LOCALAPPDATA", Environment.SpecialFolder.LocalApplicationData);
        var roaming = Folder("APPDATA", Environment.SpecialFolder.ApplicationData);
        return new AppPaths(
            InstallDir: Path.Combine(local, "Programs", AppName),
            SettingsFile: Path.Combine(roaming, AppName, "settings.json"),
            LogFile: Path.Combine(local, AppName, "journal.log"),
            GreenshotIni: Path.Combine(roaming, "Greenshot", "Greenshot.ini"),
            DesktopDir: Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            StartMenuDir: Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            TempDir: Path.GetTempPath());
    }

    private static string Folder(string variable, Environment.SpecialFolder fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrEmpty(value) ? Environment.GetFolderPath(fallback) : value;
    }

    /// <summary>Vrai si le fichier donné se trouve dans le dossier d'installation.</summary>
    public bool IsInInstallDir(string? file) =>
        file is not null
        && string.Equals(Path.GetFullPath(Path.GetDirectoryName(file) ?? "").TrimEnd('\\'),
                         Path.GetFullPath(InstallDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
