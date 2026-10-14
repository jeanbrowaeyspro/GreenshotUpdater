using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GreenshotUpdater.ViewModels;

namespace GreenshotUpdater;

/// <summary>
/// Seule fenêtre de message de l'application (questions, informations, erreurs,
/// proposition de mise à jour) : thème Fluent de l'application, clair ou sombre, comme
/// la fenêtre de réglages, y compris quand le thème de Windows change.
/// </summary>
public partial class DialogWindow : Window
{
    private readonly DialogViewModel _viewModel;

    public DialogWindow(DialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Symbol.Text = Glyph(viewModel.Kind);
        Symbol.SetResourceReference(TextBlock.ForegroundProperty,
                                    viewModel.Kind == DialogKind.Error ? "SystemFillColorCriticalBrush" : "AccentTextFillColorPrimaryBrush");
        foreach (var model in viewModel.Buttons)
        {
            var button = new Button
            {
                Content = model.Text,
                Command = model.Command,
                IsDefault = model.IsDefault,
                IsCancel = model.IsCancel,
                MinWidth = 100,
                Margin = new Thickness(8, 0, 0, 0),
            };
            if (model.IsDefault)
                button.SetResourceReference(StyleProperty, "AccentButtonStyle");
            ButtonsPanel.Children.Add(button);
        }
        viewModel.RequestHide += Hide;
        viewModel.RequestClose += Close;
    }

    /// <summary>
    /// Fenêtre centrée sur <paramref name="owner"/> s'il est visible ; sinon centrée à
    /// l'écran, au premier plan et présente dans la barre des tâches.
    /// </summary>
    public static DialogWindow Create(DialogViewModel viewModel, Window? owner)
    {
        var window = new DialogWindow(viewModel);
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.ShowInTaskbar = false;
        }
        else
            window.Topmost = true;
        return window;
    }

    /// <summary>Symbole Segoe Fluent Icons de chaque nature de message.</summary>
    public static string Glyph(DialogKind kind) => kind switch
    {
        DialogKind.Error => "",
        DialogKind.Info => "",
        _ => "",
    };

    /// <summary>Pas de fermeture pendant une opération (installation de Greenshot en cours).</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = !_viewModel.CanClose;
        base.OnClosing(e);
    }
}
