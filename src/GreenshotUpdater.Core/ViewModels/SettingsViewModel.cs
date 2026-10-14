using System.Diagnostics;

namespace GreenshotUpdater.ViewModels;

/// <summary>
/// Fenêtre de réglages (lancement manuel). Hors du dossier d'installation : options
/// d'installation et bouton "Installer" seulement. Une fois installé : application des
/// options, versions de Greenshot, vérification, désinstallation.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly Setup _setup;
    private readonly UpdateService _updates;
    private readonly AppSettings _settings;
    private readonly string? _launcherPath;
    private readonly IDialogs _dialogs;
    private bool _isBusy;
    private string _installedVersionText = "…";
    private string _latestVersionText = "…";
    private string _status = "";

    /// <param name="launcherPath">Lanceur transmis par <c>/lanceur</c> : fichier copié à l'installation.</param>
    public SettingsViewModel(Setup setup, UpdateService updates, AppSettings settings,
                             string? launcherPath, IDialogs dialogs)
    {
        _setup = setup;
        _updates = updates;
        _settings = settings;
        _launcherPath = launcherPath;
        _dialogs = dialogs;
        InstallMode = !setup.Paths.IsInInstallDir(launcherPath);
        ApplyCommand = new RelayCommand(Apply, () => !IsBusy && (!InstallMode || CanInstall));
        CheckCommand = new RelayCommand(() => _ = CheckAsync(offer: true), () => !IsBusy && !InstallMode);
        UninstallCommand = new RelayCommand(Uninstall, () => !IsBusy && !InstallMode && _setup.IsInstalled);
        if (InstallMode && !CanInstall)
            Status = "Lancez GreenshotUpdater.exe pour pouvoir l'installer.";
    }

    /// <summary>Une mise à jour de Greenshot est disponible après "Vérifier maintenant".</summary>
    public event Action<UpdateCheck>? UpdateOffered;

    public event Action? RequestClose;

    public string Title => AppInfo.Title;

    // ------------------------------------------------------------- options

    public bool RunAtStartup
    {
        get => _settings.RunAtStartup;
        set { _settings.RunAtStartup = value; OnPropertyChanged(); }
    }

    public bool DisableGreenshotUpdateCheck
    {
        get => _settings.DisableGreenshotUpdateCheck;
        set { _settings.DisableGreenshotUpdateCheck = value; OnPropertyChanged(); }
    }

    public bool DesktopShortcut
    {
        get => _settings.DesktopShortcut;
        set { _settings.DesktopShortcut = value; OnPropertyChanged(); }
    }

    public bool StartMenuShortcut
    {
        get => _settings.StartMenuShortcut;
        set { _settings.StartMenuShortcut = value; OnPropertyChanged(); }
    }

    // ------------------------------------------------------------- état

    /// <summary>Vrai si le lanceur n'est pas celui du dossier d'installation : parcours d'installation.</summary>
    public bool InstallMode { get; }

    /// <summary>Versions de Greenshot, vérification et désinstallation : seulement une fois installé.</summary>
    public bool ShowsSettings => !InstallMode;

    public string ApplyText => InstallMode ? "Installer" : "Appliquer";

    private bool CanInstall => _launcherPath is not null && File.Exists(_launcherPath);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value))
                return;
            ApplyCommand.Refresh();
            CheckCommand.Refresh();
            UninstallCommand.Refresh();
        }
    }

    public string InstalledVersionText
    {
        get => _installedVersionText;
        private set => Set(ref _installedVersionText, value);
    }

    public string LatestVersionText
    {
        get => _latestVersionText;
        private set => Set(ref _latestVersionText, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>
    /// Commande à lancer à la fermeture : première vérification par le lanceur installé
    /// après l'installation, suppression différée après la désinstallation.
    /// </summary>
    public ProcessStartInfo? PendingCommand { get; private set; }

    public RelayCommand ApplyCommand { get; }

    public RelayCommand CheckCommand { get; }

    public RelayCommand UninstallCommand { get; }

    // ------------------------------------------------------------- actions

    private void Apply()
    {
        Run(() =>
        {
            if (!InstallMode)
            {
                Status = string.Join("\n", ["Options appliquées.", .. _setup.Apply(_settings)]);
                return;
            }
            var warnings = _setup.Install(_launcherPath!, _settings);
            _dialogs.Info(string.Join("\n", ["Installation terminée.", .. warnings]));
            PendingCommand = _setup.StartupCheck;
            RequestClose?.Invoke();
        });
    }

    private void Uninstall()
    {
        if (!_dialogs.Confirm("Désinstaller GreenshotUpdater ?\n\n"
                              + "Le démarrage avec Windows et les raccourcis seront retirés, la vérification "
                              + "intégrée de Greenshot sera rétablie et le dossier d'installation supprimé.",
                              "Désinstaller", "Annuler"))
            return;
        Run(() =>
        {
            PendingCommand = _setup.Uninstall();
            _dialogs.Info("GreenshotUpdater est désinstallé.");
            RequestClose?.Invoke();
        });
    }

    /// <summary>Action sur fichiers ou registre : une erreur est affichée, pas propagée.</summary>
    private void Run(Action action)
    {
        IsBusy = true;
        try
        {
            action();
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException or System.Security.SecurityException
                                           or System.Runtime.InteropServices.COMException)
        {
            Log.Error(exc.ToString());
            _dialogs.Error($"Opération impossible :\n\n{exc.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>À l'ouverture de la fenêtre : lecture des versions, seulement une fois installé.</summary>
    public Task OpenedAsync() => InstallMode ? Task.CompletedTask : CheckAsync(offer: false);

    /// <summary>
    /// Lit les versions (une seule tentative) ; avec <paramref name="offer"/>, propose la
    /// mise à jour si elle existe.
    /// </summary>
    public async Task CheckAsync(bool offer)
    {
        IsBusy = true;
        LatestVersionText = "Vérification…";
        var check = await _updates.CheckAsync([]);
        IsBusy = false;
        InstalledVersionText = check.Installed is null ? "non installé" : VersionText.Display(check.Installed.Version);
        LatestVersionText = check.Latest is null ? "inconnue" : VersionText.Display(check.Latest.Version);
        Status = check switch
        {
            { Error: not null } => check.Error,
            { Installed: null } => "Greenshot n'est pas installé.",
            { UpdateAvailable: true } => $"Greenshot {LatestVersionText} est disponible.",
            _ => "Greenshot est à jour.",
        };
        if (offer && check.UpdateAvailable)
            UpdateOffered?.Invoke(check);
    }
}
