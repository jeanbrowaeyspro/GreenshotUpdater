namespace GreenshotUpdater;

/// <summary>
/// Modification de la vérification intégrée de Greenshot (Greenshot.ini).
/// </summary>
/// <remarks>
/// Greenshot réécrit tout son fichier .ini à sa fermeture : modifié pendant qu'il
/// tourne, le réglage serait écrasé. Si la valeur doit changer et que Greenshot
/// tourne, il est donc fermé (il enregistre alors ses réglages), le fichier est
/// modifié, puis Greenshot est relancé. Rien n'est fait si la valeur est déjà la bonne.
/// </remarks>
public sealed class GreenshotConfig(string iniPath, IGreenshotProcess greenshot)
{
    /// <summary>Applique la valeur voulue ; renvoie un message si le fichier est introuvable.</summary>
    public string? SetUpdateCheck(bool disable)
    {
        var target = disable ? GreenshotIni.Disabled : GreenshotIni.Default;
        if (!File.Exists(iniPath))
        {
            Log.Info($"Greenshot.ini introuvable : {iniPath}");
            return "Greenshot.ini introuvable : la vérification intégrée de Greenshot n'a pas été modifiée.";
        }
        if (GreenshotIni.ReadFile(iniPath) == target)
            return null;

        var running = greenshot.Close();
        GreenshotIni.WriteFile(iniPath, target);
        Log.Info($"Greenshot.ini : {GreenshotIni.Key}={target}");
        if (running is not null)
            greenshot.Start(running);
        return null;
    }
}
