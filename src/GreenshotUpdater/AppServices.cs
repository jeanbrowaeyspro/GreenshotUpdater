using System.IO;
using System.Windows;
using GreenshotUpdater.ViewModels;

namespace GreenshotUpdater;

/// <summary>Services réels de l'application, assemblés une fois au démarrage.</summary>
public sealed class AppServices
{
    public AppServices(AppPaths paths, IDialogs dialogs)
    {
        Paths = paths;
        Dialogs = dialogs;
        var registry = new WindowsRegistry();
        var greenshot = new GreenshotProcess(Runner);
        Setup = new Setup(paths, registry, new ShellLinkWriter(), new GreenshotConfig(paths.GreenshotIni, greenshot));
        Updates = new UpdateService(registry, new GitHubReleaseSource(ReleaseFeed.Greenshot), greenshot, Runner,
                                    () => Winget.Find(Winget.CandidateDirs(), File.Exists), paths.TempDir);
        SelfUpdate = new SelfUpdate(new GitHubReleaseSource(ReleaseFeed.Self),
                                    typeof(SelfUpdate).Assembly.GetName().Version!, paths.InstalledLauncher);
    }

    public AppPaths Paths { get; }

    public SelfUpdate SelfUpdate { get; }

    public IDialogs Dialogs { get; }

    public ProcessRunner Runner { get; } = new();

    public Setup Setup { get; }

    public UpdateService Updates { get; }
}

/// <summary>
/// Messages affichés dans une <see cref="DialogWindow"/> modale, centrée sur la fenêtre
/// principale si elle est visible, sinon à l'écran.
/// </summary>
public sealed class WpfDialogs(Func<Window?> owner) : IDialogs
{
    /// <summary>Affiche la boîte et renvoie le rang du bouton choisi (null : fermée autrement).</summary>
    private int? Show(DialogKind kind, string text, params string[] buttons)
    {
        var viewModel = new DialogViewModel(kind, text, buttons);
        DialogWindow.Create(viewModel, owner()).ShowDialog();
        return viewModel.Choice;
    }

    public bool Confirm(string text, string yes = "Oui", string no = "Non") => Show(DialogKind.Question, text, yes, no) == 0;

    public void Info(string text) => Show(DialogKind.Info, text, "OK");

    public void Error(string text) => Show(DialogKind.Error, text, "OK");
}
