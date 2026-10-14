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
        Updates = new UpdateService(registry, new GitHubReleaseSource(), greenshot, Runner,
                                    () => Winget.Find(Winget.CandidateDirs(), File.Exists), paths.TempDir);
    }

    public AppPaths Paths { get; }

    public IDialogs Dialogs { get; }

    public ProcessRunner Runner { get; } = new();

    public Setup Setup { get; }

    public UpdateService Updates { get; }
}

/// <summary>Messages affichés par MessageBox, au-dessus de la fenêtre principale.</summary>
public sealed class WpfDialogs(Func<Window?> owner) : IDialogs
{
    private MessageBoxResult Show(string text, MessageBoxButton buttons, MessageBoxImage image) =>
        owner() is { IsVisible: true } window
            ? MessageBox.Show(window, text, AppInfo.Title, buttons, image)
            : MessageBox.Show(text, AppInfo.Title, buttons, image);

    public bool Confirm(string text) => Show(text, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Info(string text) => Show(text, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Error(string text) => Show(text, MessageBoxButton.OK, MessageBoxImage.Error);
}
