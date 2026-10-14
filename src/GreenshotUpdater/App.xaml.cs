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
    private IDialogs? _dialogs;

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
        // Boîtes centrées sur la fenêtre active (proposition de mise à jour au-dessus des réglages…).
        _dialogs = new WpfDialogs(() => Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? MainWindow);
        DispatcherUnhandledException += OnUnhandledException;
        ThemeManager.Apply(this);

        var services = new AppServices(paths, _dialogs);
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
        var window = CreateUpdatePrompt(check, services, owner: null);
        window.Closed += (_, _) => Shutdown();
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Fenêtre de mise à jour. Dès que l'installation est lancée, sa fenêtre propriétaire
    /// (réglages) est masquée et elle devient une fenêtre d'attente, masquée à son tour à
    /// l'apparition de l'assistant de Greenshot : le processus attend la fin de
    /// l'installation sans rien afficher (l'instance unique reste prise), puis la fenêtre
    /// propriétaire est fermée, ce qui termine l'application.
    /// </summary>
    private static DialogWindow CreateUpdatePrompt(UpdateCheck check, AppServices services, Window? owner)
    {
        var viewModel = new UpdatePromptViewModel(check, services.Updates, services.Dialogs, services.InstallerWait);
        var window = DialogWindow.Create(viewModel, owner);
        if (owner is not null)
        {
            // Masquer la propriétaire ne masque pas la fenêtre d'attente (fenêtre possédée).
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DialogViewModel.IsWaiting) && viewModel.IsWaiting)
                    owner.Hide();
            };
            window.Closed += (_, _) =>
            {
                if (viewModel.Outcome is not null)
                    owner.Close();
            };
        }
        return window;
    }

    private void ShowSettings(AppServices services, AppArguments args)
    {
        var viewModel = new SettingsViewModel(services.Setup, services.Updates,
                                              AppSettings.Load(services.Paths.SettingsFile),
                                              args.LauncherPath, services.Dialogs);
        var window = new SettingsWindow(viewModel);
        viewModel.UpdateOffered += check => CreateUpdatePrompt(check, services, window).ShowDialog();
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
        _dialogs?.Error($"Erreur inattendue :\n\n{e.Exception.Message}");
        e.Handled = true;
    }
}
