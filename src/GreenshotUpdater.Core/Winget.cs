using System.Diagnostics;

namespace GreenshotUpdater;

/// <summary>Mise à jour de Greenshot par winget.</summary>
public static class Winget
{
    public const string ExeName = "winget.exe";
    public const string PackageId = "Greenshot.Greenshot";

    /// <summary>
    /// Arguments de la mise à jour silencieuse, les accords acceptés pour ne rien
    /// demander en console. Le manifeste winget de Greenshot.Greenshot déclare un
    /// installeur <c>inno</c> sans switches Silent ni SilentWithProgress propres :
    /// <c>--silent</c> donne donc les switches Inno par défaut de winget,
    /// <see cref="InnoSetup.SilentArguments"/> (plus <c>/ALLUSERS</c> ou
    /// <c>/CURRENTUSER</c> selon la portée de l'installation existante).
    /// </summary>
    public const string UpgradeArguments =
        $"upgrade --id {PackageId} --exact --silent --accept-source-agreements --accept-package-agreements";

    /// <summary>
    /// winget est une console : elle est cachée, seule l'UAC apparaît.
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
