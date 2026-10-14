namespace GreenshotUpdater;

/// <summary>
/// Arguments de ligne de commande échangés entre le lanceur et l'application.
/// Fichier lié aux deux projets : interne à chacun.
/// </summary>
internal static class Switches
{
    /// <summary>Démarrage de session Windows : vérification silencieuse.</summary>
    public const string Startup = "/demarrage";
    /// <summary>Suivi du chemin complet du lanceur (fichier copié à l'installation).</summary>
    public const string Launcher = "/lanceur";
}
