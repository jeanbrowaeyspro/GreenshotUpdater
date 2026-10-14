namespace GreenshotUpdater.ViewModels;

/// <summary>
/// Petite fenêtre "Greenshot x est disponible" : Installer / Plus tard, puis résultat.
/// "Plus tard" ne mémorise rien : la question revient au prochain démarrage.
/// </summary>
public sealed class UpdatePromptViewModel : ObservableObject
{
    private readonly UpdateCheck _check;
    private readonly UpdateService _updates;
    private readonly IDialogs _dialogs;
    private string _message;
    private bool _isBusy;
    private bool _isDone;

    public UpdatePromptViewModel(UpdateCheck check, UpdateService updates, IDialogs dialogs)
    {
        _check = check;
        _updates = updates;
        _dialogs = dialogs;
        _message = check.OfferText;
        InstallCommand = new RelayCommand(() => _ = InstallAsync(), () => CanChoose);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke(), () => !IsBusy);
    }

    public event Action? RequestClose;

    public string Title => AppInfo.Title;

    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    /// <summary>Installation en cours : boutons inactifs.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
                RefreshState();
        }
    }

    /// <summary>Installation terminée : seul "Fermer" reste.</summary>
    public bool IsDone
    {
        get => _isDone;
        private set
        {
            if (Set(ref _isDone, value))
                RefreshState();
        }
    }

    /// <summary>Question en attente de réponse : boutons Installer et Plus tard.</summary>
    public bool CanChoose => !IsBusy && !IsDone;

    public bool? Success { get; private set; }

    public RelayCommand InstallCommand { get; }

    /// <summary>"Plus tard" avant l'installation, "Fermer" après.</summary>
    public RelayCommand CloseCommand { get; }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(CanChoose));
        InstallCommand.Refresh();
        CloseCommand.Refresh();
    }

    public async Task InstallAsync()
    {
        var useWinget = _updates.WingetAvailable;
        if (!useWinget && !_dialogs.Confirm(
                "winget est introuvable sur ce poste.\n\n"
                + $"Télécharger l'installeur {_check.Latest!.InstallerName ?? "de Greenshot"} depuis GitHub et le lancer ?"))
            return;

        IsBusy = true;
        Message = "Installation en cours… Suivez l'assistant de Greenshot.";
        var outcome = useWinget
            ? await _updates.InstallWithWingetAsync()
            : await _updates.InstallFromGitHubAsync(_check.Latest!);
        Success = outcome.Success;
        Message = outcome.Message;
        IsBusy = false;
        IsDone = true;
    }
}
