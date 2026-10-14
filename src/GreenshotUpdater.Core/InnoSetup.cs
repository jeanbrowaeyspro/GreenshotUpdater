using System.Diagnostics;

namespace GreenshotUpdater;

/// <summary>Installeur Inno Setup de Greenshot, lancé sans winget.</summary>
public static class InnoSetup
{
    /// <summary>
    /// Installation sans aucune fenêtre de l'installeur (ni assistant, ni progression,
    /// ni message, ni demande de redémarrage), comme <c>winget --silent</c> : ce sont
    /// les switches Inno par défaut de winget pour le mode Silent. Les choix de
    /// l'installation précédente (dossier, composants, portée) sont repris par Inno.
    /// </summary>
    public const string SilentArguments = "/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";

    /// <summary>
    /// Lancement par ShellExecute : l'installeur demande lui-même l'élévation (UAC)
    /// si l'installation existante est pour tous les utilisateurs.
    /// </summary>
    public static ProcessStartInfo SilentCommand(string installerPath) => new(installerPath, SilentArguments)
    {
        UseShellExecute = true,
    };
}
