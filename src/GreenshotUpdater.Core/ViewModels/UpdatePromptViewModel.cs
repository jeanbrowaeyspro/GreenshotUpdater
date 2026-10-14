namespace GreenshotUpdater.ViewModels;

/// <summary>
/// Boîte "Greenshot x est disponible" : Installer / Plus tard. Dès que l'installation
/// est lancée, la boîte devient une fenêtre d'attente ("Préparation de la mise à jour…")
/// jusqu'à l'apparition de l'assistant de Greenshot (<see cref="InstallerWindowWait"/>),
/// puis disparaît (<see cref="DialogViewModel.RequestHide"/>) ; elle se ferme à la fin,
/// sans message de succès (erreur seulement en cas d'échec).
/// "Plus tard" ne mémorise rien : la question revient au prochain démarrage.
/// </summary>
public sealed class UpdatePromptViewModel : DialogViewModel
{
    private readonly UpdateCheck _check;
    private readonly UpdateService _updates;
    private readonly IDialogs _dialogs;
    private readonly InstallerWindowWait _installerWait;
    private bool _isBusy;

    public UpdatePromptViewModel(UpdateCheck check, UpdateService updates, IDialogs dialogs, InstallerWindowWait installerWait)
        : base(DialogKind.Question, check.OfferText)
    {
        _check = check;
        _updates = updates;
        _dialogs = dialogs;
        _installerWait = installerWait;
        InstallCommand = new RelayCommand(() => _ = InstallAsync(), () => !IsBusy);
        CloseCommand = new RelayCommand(Close, () => !IsBusy);
        Buttons = [new("Installer", InstallCommand, true, false), new("Plus tard", CloseCommand, false, true)];
    }

    /// <summary>Installation en cours : la fenêtre ne se ferme pas, les boutons sont inactifs.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value))
                return;
            InstallCommand.Refresh();
            CloseCommand.Refresh();
        }
    }

    public override bool CanClose => !IsBusy;

    /// <summary>Issue de la dernière installation ; null tant qu'aucune n'a été lancée.</summary>
    public UpdateOutcome? Outcome { get; private set; }

    public RelayCommand InstallCommand { get; }

    public RelayCommand CloseCommand { get; }

    /// <summary>Message de la fenêtre d'attente.</summary>
    public string WaitingText => $"Préparation de la mise à jour de Greenshot {VersionText.Display(_check.Latest!.Version)}…\n\n"
                                + "L'assistant d'installation de Greenshot s'ouvrira dans un moment.";

    public async Task InstallAsync()
    {
        var useWinget = _updates.WingetAvailable;
        if (!useWinget && !_dialogs.Confirm(
                "winget est introuvable sur ce poste.\n\n"
                + $"Télécharger l'installeur {_check.Latest!.InstallerName ?? "de Greenshot"} depuis GitHub et le lancer ?",
                "Télécharger", "Annuler"))
            return;

        IsBusy = true;
        ShowWaiting(WaitingText);
        // Hors du fil de l'interface : la fermeture de Greenshot (jusqu'à quelques
        // secondes) ne fige pas la fenêtre d'attente.
        var install = Task.Run(() => useWinget
                                   ? _updates.InstallWithWingetAsync()
                                   : _updates.InstallFromGitHubAsync(_check.Latest!));
        await _installerWait.WaitAsync(install);
        Hide();
        Outcome = await install;
        IsBusy = false;
        if (Outcome.ShowsError)
            _dialogs.Error(Outcome.Message);
        Close();
    }
}
