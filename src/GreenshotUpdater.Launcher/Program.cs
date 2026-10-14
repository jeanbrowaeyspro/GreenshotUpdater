using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace GreenshotUpdater.Launcher;

/// <summary>
/// Vérifie le runtime .NET Desktop 10 (propose de l'installer), extrait
/// GreenshotUpdater.App.exe à côté du lanceur si besoin, puis la démarre en lui
/// transmettant la ligne de commande et le chemin du lanceur.
/// </summary>
public static class Program
{
    public const string Title = "GreenshotUpdater";

    [STAThread]
    public static int Main()
    {
        try
        {
            if (!RuntimeCheck.IsInstalled(RuntimeCheck.CandidateRoots()) && !RuntimeSetup.Run())
                return 1;
            var launcher = Assembly.GetExecutingAssembly().Location;
            var app = Path.Combine(Path.GetDirectoryName(launcher)!, EmbeddedApp.ResourceName);
            if (!PrepareApp(app))
                return 1;
            Process.Start(new ProcessStartInfo(app, CommandLine.ForApp(launcher, Environment.CommandLine))
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory,
            })?.Dispose();
            return 0;
        }
        catch (Exception exc)
        {
            Error($"Démarrage impossible :\n\n{exc.Message}");
            return 1;
        }
    }

    /// <summary>Extrait l'application incorporée si elle manque ou diffère de celle en place.</summary>
    private static bool PrepareApp(string app)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedApp.ResourceName);
        if (stream is null)
        {
            // Lanceur de développement, compilé sans l'application.
            if (File.Exists(app))
                return true;
            Error($"Application introuvable :\n{app}");
            return false;
        }
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        try
        {
            EmbeddedApp.Extract(buffer.ToArray(), app);
            return true;
        }
        catch (IOException) when (File.Exists(app))
        {
            // Application en cours d'exécution : celle en place est démarrée, elle refusera
            // une deuxième instance.
            return true;
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            Error($"Impossible d'écrire {EmbeddedApp.ResourceName} dans le dossier :\n"
                  + $"{Path.GetDirectoryName(app)}\n\n{exc.Message}\n\n"
                  + "Copiez GreenshotUpdater.exe dans un dossier où vous pouvez écrire (Téléchargements, Bureau…).");
            return false;
        }
    }

    public static void Error(string text) =>
        MessageBox.Show(text, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
