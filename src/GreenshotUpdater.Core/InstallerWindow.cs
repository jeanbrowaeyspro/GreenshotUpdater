using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GreenshotUpdater;

/// <summary>Fenêtre de premier niveau visible.</summary>
/// <param name="ProcessName">
/// Nom du processus propriétaire, sans extension ".exe" ; lu seulement pour les titres
/// contenant "Greenshot" (lecture coûteuse), vide sinon ou si le processus est terminé.
/// </param>
public sealed record TopWindow(nint Handle, string ClassName, string Title, int ProcessId, string ProcessName);

/// <summary>Fenêtres de premier niveau visibles du bureau, remplacées par un faux dans les tests.</summary>
public interface IWindowWatcher
{
    IReadOnlyList<TopWindow> VisibleWindows();
}

/// <summary>Issue de l'attente de l'assistant d'installation de Greenshot.</summary>
public enum InstallerWaitEnd
{
    /// <summary>L'assistant (ou le choix de la langue) est affiché.</summary>
    InstallerShown,

    /// <summary>winget ou l'installeur s'est terminé avant (échec, annulation, refus de l'UAC).</summary>
    InstallFinished,

    /// <summary>Rien n'est apparu dans le délai <see cref="InstallerWindowWait.Timeout"/>.</summary>
    TimedOut,
}

/// <summary>
/// Attend l'apparition de l'assistant d'installation de Greenshot, pour fermer la
/// fenêtre d'attente à ce moment-là.
/// </summary>
/// <remarks>
/// L'installeur de Greenshot est un Inno Setup : l'exécutable téléchargé extrait puis
/// lance un processus <c>is-XXXXX.tmp\&lt;installeur&gt;.tmp</c> qui porte les fenêtres.
/// Ce sont des fiches Delphi VCL, dont la classe de fenêtre Windows est le nom de la
/// classe Delphi (ni <c>TSetupForm.CreateParams</c> ni ses dérivées ne la changent,
/// source issrc, unités <c>Setup.SetupForm</c>, <c>Setup.WizardForm</c>,
/// <c>Setup.SelectLanguageForm</c>) : <c>TWizardForm</c> pour l'assistant et
/// <c>TSelectLanguageForm</c> pour le choix de la langue, affiché avant l'assistant
/// (<c>ShowLanguageDialog</c> vaut <c>yes</c> par défaut et le script de Greenshot ne
/// le change pas). Repli : fenêtre dont le titre contient "Greenshot", d'un processus
/// autre que Greenshot et que GreenshotUpdater. Les fenêtres déjà présentes au début
/// de l'attente sont ignorées (page web ou dossier "Greenshot" ouverts…).
/// </remarks>
public sealed class InstallerWindowWait(IWindowWatcher watcher)
{
    /// <summary>Classes de fenêtre des fiches Inno Setup affichées au lancement de l'installeur.</summary>
    public static readonly string[] InnoSetupClasses = ["TWizardForm", "TSelectLanguageForm"];

    /// <summary>Intervalle entre deux examens des fenêtres.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Délai maximal : au-delà, la fenêtre d'attente se ferme même si rien n'est apparu
    /// (téléchargement par winget très lent, UAC laissée sans réponse…).
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Vrai si le titre mentionne Greenshot (repli de la détection).</summary>
    public static bool MentionsGreenshot(string title) => title.Contains("Greenshot", StringComparison.OrdinalIgnoreCase);

    /// <summary>Vrai si la fenêtre appartient à l'installeur de Greenshot.</summary>
    public static bool IsInstaller(TopWindow window) =>
        InnoSetupClasses.Contains(window.ClassName, StringComparer.Ordinal)
        || (MentionsGreenshot(window.Title)
            && window.ProcessId != Environment.ProcessId
            && !string.Equals(window.ProcessName, Path.GetFileNameWithoutExtension(InstalledGreenshot.ExeName),
                              StringComparison.OrdinalIgnoreCase));

    private IEnumerable<nint> Installers() => watcher.VisibleWindows().Where(IsInstaller).Select(w => w.Handle);

    /// <summary>
    /// Attend qu'une nouvelle fenêtre de l'installeur soit visible, que
    /// <paramref name="install"/> se termine, ou <see cref="Timeout"/>.
    /// </summary>
    public async Task<InstallerWaitEnd> WaitAsync(Task install)
    {
        var known = Installers().ToHashSet();
        var deadline = Task.Delay(Timeout);
        while (true)
        {
            if (install.IsCompleted)
                return Ended(InstallerWaitEnd.InstallFinished);
            if (Installers().Any(handle => !known.Contains(handle)))
                return Ended(InstallerWaitEnd.InstallerShown);
            if (await Task.WhenAny(install, deadline, Task.Delay(Interval)) == deadline)
                return Ended(InstallerWaitEnd.TimedOut);
        }
    }

    private static InstallerWaitEnd Ended(InstallerWaitEnd end)
    {
        Log.Info(end switch
        {
            InstallerWaitEnd.InstallerShown => "Assistant d'installation de Greenshot affiché : fin de l'attente",
            InstallerWaitEnd.InstallFinished => "Installation terminée avant l'apparition de l'assistant",
            _ => "Assistant d'installation non détecté dans le délai : fin de l'attente",
        });
        return end;
    }
}

/// <summary>Fenêtres réelles du bureau (EnumWindows).</summary>
public sealed class DesktopWindows : IWindowWatcher
{
    public IReadOnlyList<TopWindow> VisibleWindows()
    {
        var windows = new List<TopWindow>();
        EnumWindows((handle, _) =>
        {
            if (IsWindowVisible(handle))
                windows.Add(Describe(handle));
            return true;
        }, 0);
        return windows;
    }

    private static TopWindow Describe(nint handle)
    {
        var text = new StringBuilder(256);
        GetClassName(handle, text, text.Capacity);
        var className = text.ToString();
        text.Clear();
        GetWindowText(handle, text, text.Capacity);
        var title = text.ToString();
        GetWindowThreadProcessId(handle, out var processId);
        var processName = InstallerWindowWait.MentionsGreenshot(title) ? ProcessName((int)processId) : "";
        return new TopWindow(handle, className, title, (int)processId, processName);
    }

    /// <summary>Nom du processus ; vide s'il s'est terminé entre-temps.</summary>
    private static string ProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception exc) when (exc is ArgumentException or InvalidOperationException)
        {
            return "";
        }
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint handle, StringBuilder name, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
}
