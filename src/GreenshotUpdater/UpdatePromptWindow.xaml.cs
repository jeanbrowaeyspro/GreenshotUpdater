using System.ComponentModel;
using System.Windows;
using GreenshotUpdater.ViewModels;

namespace GreenshotUpdater;

/// <summary>Proposition de mise à jour de Greenshot : Installer / Plus tard, puis résultat.</summary>
public partial class UpdatePromptWindow : Window
{
    private readonly UpdatePromptViewModel _viewModel;

    public UpdatePromptWindow(UpdatePromptViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        viewModel.RequestClose += Close;
    }

    /// <summary>Pas de fermeture pendant l'installation : Greenshot doit être relancé et le résultat affiché.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = _viewModel.IsBusy;
        base.OnClosing(e);
    }
}
