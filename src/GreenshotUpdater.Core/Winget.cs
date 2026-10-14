using System.Diagnostics;

namespace GreenshotUpdater;

/// <summary>Mise à jour de Greenshot par winget.</summary>
public static class Winget
{
    public const string ExeName = "winget.exe";
    public const string PackageId = "Greenshot.Greenshot";

    /// <summary>
    /// Arguments de la mise à jour : l'assistant de Greenshot reste affiché
    /// (<c>--interactive</c>), les accords sont acceptés pour ne rien demander en console.
    /// </summary>
    public const string UpgradeArguments =
        $"upgrade --id {PackageId} --exact --interactive --accept-source-agreements --accept-package-agreements";

    /// <summary>
    /// winget est une console : elle est cachée, seuls l'UAC et l'assistant de
    /// l'installeur apparaissent.
    /// </summary>
    public static ProcessStartInfo UpgradeCommand(string wingetPath) => new(wingetPath, UpgradeArguments)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    /// <summary>
    /// Dossiers où chercher winget : alias d'exécution de l'utilisateur
    /// (<c>%LOCALAPPDATA%\Microsoft\WindowsApps</c>), puis le PATH.
    /// </summary>
    public static IEnumerable<string> CandidateDirs()
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(local))
            yield return Path.Combine(local, "Microsoft", "WindowsApps");
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            yield return dir.Trim();
    }

    /// <summary>Premier winget.exe trouvé ; null si absent.</summary>
    public static string? Find(IEnumerable<string> dirs, Func<string, bool> exists) =>
        dirs.Select(dir => Path.Combine(dir, ExeName)).FirstOrDefault(exists);

    /// <summary>Code de sortie lisible : les codes de winget sont des HRESULT.</summary>
    public static string ExitCodeText(int code) => code < 0 || code > 0xFFFF ? $"0x{code:X8}" : code.ToString();
}
