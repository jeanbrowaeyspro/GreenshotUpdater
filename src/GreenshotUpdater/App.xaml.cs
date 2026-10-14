using System.Windows;
using System.Windows.Threading;
using GreenshotUpdater.Theme;
using GreenshotUpdater.ViewModels;

namespace GreenshotUpdater;

/// <summary>
/// Point d'entrée. Sans argument : fenêtre de réglages et d'installation. Avec
/// <c>/demarrage</c> : vérification silencieuse, fenêtre seulement si une mise à jour
/// de Greenshot est disponible.
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = AppPaths.FromEnvironment();
        Log.Configure(paths.LogFile);
        var args = AppArguments.Parse(e.Args);

        _singleInstance = new Mutex(true, @"Local\" + AppPaths.AppName, out var first);
        if (!first)
        {
            Log.Info("Une instance tourne déjà : sortie");
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }
        DispatcherUnhandledException += OnUnhandledException;
        ThemeManager.Apply(this);

        var services = new AppServices(paths, new WpfDialogs(() => MainWindow));
        if (args.Startup)
            await StartupCheckAsync(services);
        else
            ShowSettings(services, args);
    }

    /// <summary>Démarrage de session : rien n'est affiché s'il n'y a rien à proposer.</summary>
    private async Task StartupCheckAsync(AppServices services)
    {
        Log.Info("Vérification au démarrage");
        var check = await services.Updates.CheckAsync(UpdateService.StartupRetryDelays);
        if (!check.UpdateAvailable)
        {
            Shutdown();
            return;
        }
        var window = new UpdatePromptWindow(new UpdatePromptViewModel(check, services.Updates, services.Dialogs));
        window.Closed += (_, _) => Shutdown();
        MainWindow = window;
        window.Show();
    }

    private void ShowSettings(AppServices services, AppArguments args)
    {
        var viewModel = new SettingsViewModel(services.Setup, services.Updates,
                                              AppSettings.Load(services.Paths.SettingsFile),
                                              args.LauncherPath, services.Dialogs);
        var window = new SettingsWindow(viewModel);
        viewModel.UpdateOffered += check =>
            new UpdatePromptWindow(new UpdatePromptViewModel(check, services.Updates, services.Dialogs)) { Owner = window }
                .ShowDialog();
        window.Closed += (_, _) =>
        {
            if (viewModel.PendingCommand is { } command)
            {
                // Instance unique libérée d'abord : la vérification lancée après
                // l'installation ne doit pas se croire en double.
                ReleaseSingleInstance();
                services.Runner.Start(command);
            }
            Shutdown();
        };
        MainWindow = window;
        window.Show();
        _ = viewModel.OpenedAsync();
    }

    private void ReleaseSingleInstance()
    {
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        _singleInstance = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReleaseSingleInstance();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception.ToString());
        MessageBox.Show($"Erreur inattendue :\n\n{e.Exception.Message}", AppInfo.Title,
                        MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
