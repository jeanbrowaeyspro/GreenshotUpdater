using System.ComponentModel;
using System.Windows;
using GreenshotUpdater.ViewModels;

namespace GreenshotUpdater;

/// <summary>
/// Proposition de mise à jour de Greenshot : Installer / Plus tard. Masquée dès que
/// l'installation est lancée, fermée à sa fin.
/// </summary>
public partial class UpdatePromptWindow : Window
{
    private readonly UpdatePromptViewModel _viewModel;

    public UpdatePromptWindow(UpdatePromptViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        viewModel.InstallStarted += Hide;
        viewModel.RequestClose += Close;
    }

    /// <summary>Pas de fermeture pendant l'installation : Greenshot doit être relancé et le résultat journalisé.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = _viewModel.IsBusy;
        base.OnClosing(e);
    }
}
