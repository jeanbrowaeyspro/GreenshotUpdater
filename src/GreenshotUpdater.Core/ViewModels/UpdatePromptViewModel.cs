namespace GreenshotUpdater.ViewModels;

/// <summary>
/// Boîte "Greenshot x est disponible" : Installer / Plus tard. Dès que l'installation
/// silencieuse est lancée, la boîte devient une fenêtre d'attente ("Installation de
/// Greenshot x…") jusqu'à la fin de l'installation, puis un message de fin : "Greenshot
/// x est installé." ou l'erreur, avec OK, qui ferme. Annulation (UAC refusée) : fermée
/// sans message. "Plus tard" ne mémorise rien : la question revient au prochain démarrage.
/// </summary>
public sealed class UpdatePromptViewModel : DialogViewModel
{
    private readonly UpdateCheck _check;
    private readonly UpdateService _updates;
    private readonly IDialogs _dialogs;
    private bool _isBusy;

    public UpdatePromptViewModel(UpdateCheck check, UpdateService updates, IDialogs dialogs)
        : base(DialogKind.Question, check.OfferText)
    {
        _check = check;
        _updates = updates;
        _dialogs = dialogs;
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
    public string WaitingText => $"Installation de Greenshot {VersionText.Display(_check.Latest!.Version)}…\n\n"
                                + "Autorisez la modification de l'appareil si Windows le demande.";

    public async Task InstallAsync()
    {
        if (!_updates.WingetAvailable && !_dialogs.Confirm(
                "winget est introuvable sur ce poste.\n\n"
                + $"Télécharger l'installeur {_check.Latest!.AssetName ?? "de Greenshot"} depuis GitHub et l'exécuter ?",
                "Télécharger", "Annuler"))
            return;

        IsBusy = true;
        ShowWaiting(WaitingText);
        // Hors du fil de l'interface : la fermeture de Greenshot (jusqu'à quelques
        // secondes) ne fige pas la fenêtre d'attente.
        Outcome = await Task.Run(() => _updates.InstallAsync(_check.Latest!));
        IsBusy = false;
        if (Outcome.Cancelled)
            Close();
        else
            ShowMessage(Outcome.Success ? DialogKind.Info : DialogKind.Error, Outcome.Message, "OK");
    }
}
