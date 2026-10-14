namespace GreenshotUpdater;

/// <summary>Greenshot tel qu'inscrit dans les programmes installés.</summary>
public sealed record InstalledGreenshot(Version Version, string? InstallLocation)
{
    public const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    public const string ExeName = "Greenshot.exe";

    private static readonly RegistryRoot[] Roots =
        [RegistryRoot.LocalMachine64, RegistryRoot.LocalMachine32, RegistryRoot.CurrentUser];

    /// <summary>Exécutable de Greenshot, si le dossier d'installation est connu.</summary>
    public string? ExePath =>
        string.IsNullOrEmpty(InstallLocation) ? null : Path.Combine(InstallLocation, ExeName);

    /// <summary>
    /// Entrée de désinstallation "Greenshot*" de plus haute version (HKLM 64 et
    /// 32 bits, HKCU) ; null si Greenshot n'est pas installé.
    /// </summary>
    public static InstalledGreenshot? Find(IRegistryStore registry)
    {
        InstalledGreenshot? best = null;
        foreach (var root in Roots)
        {
            foreach (var name in registry.SubKeyNames(root, UninstallPath))
            {
                var path = $@"{UninstallPath}\{name}";
                var displayName = registry.GetString(root, path, "DisplayName");
                if (displayName is null || !displayName.StartsWith("Greenshot", StringComparison.OrdinalIgnoreCase))
                    continue;
                var version = VersionText.Parse(registry.GetString(root, path, "DisplayVersion"));
                if (version is not null && (best is null || version > best.Version))
                    best = new InstalledGreenshot(version, registry.GetString(root, path, "InstallLocation"));
            }
        }
        return best;
    }
}
