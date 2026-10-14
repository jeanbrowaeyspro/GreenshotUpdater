using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GreenshotUpdater.Theme;
using GreenshotUpdater.ViewModels;
using static GreenshotUpdater.Tests.Fixtures;

namespace GreenshotUpdater.Tests;

/// <summary>
/// Rendu hors écran de chaque fenêtre, en thème clair et sombre : les fenêtres ne sont
/// jamais affichées (pas de Show), seulement mesurées puis dessinées dans une image.
/// </summary>
public class ViewTests
{
    public static readonly string Captures = Path.Combine(Root, "captures");

    /// <summary>
    /// Dessine le contenu de la fenêtre dans une image, l'enregistre en PNG et renvoie ses
    /// pixels BGRA. Une fenêtre jamais affichée ne se mesure pas et ne reçoit pas le thème
    /// Fluent : son contenu est placé dans un cadre qui porte le dictionnaire Fluent du
    /// thème, les pinceaux de l'application et les ressources de la fenêtre.
    /// </summary>
    private static byte[] Render(Window window, bool dark, string name)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Border { Width = window.Width, Child = content, DataContext = window.DataContext };
        host.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{(dark ? "Dark" : "Light")}.xaml"),
        });
        host.Resources.MergedDictionaries.Add(window.Resources);
        ThemeManager.ApplyBrushes(host.Resources, dark);
        // Fond et texte que le style Fluent donne à une fenêtre affichée.
        host.SetResourceReference(Border.BackgroundProperty, "WindowBackground");
        host.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
        // Liaisons en attente (titre, textes…) : traitées par le répartiteur.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        host.Measure(new Size(window.Width, 1000));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        var width = (int)Math.Ceiling(host.ActualWidth);
        var height = (int)Math.Ceiling(host.ActualHeight);
        Assert.InRange(height, 100, 1000);

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        Directory.CreateDirectory(Captures);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(Captures, $"{name}_{(dark ? "sombre" : "clair")}.png")))
            encoder.Save(file);

        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    /// <summary>Luminosité moyenne (0 à 255) et nombre de pixels qui s'écartent nettement du fond.</summary>
    private static (double Mean, int Contrasted) Measure(byte[] pixels)
    {
        var luminance = Enumerable.Range(0, pixels.Length / 4)
                                  .Select(i => (pixels[i * 4] + pixels[i * 4 + 1] + pixels[i * 4 + 2]) / 3.0)
                                  .ToArray();
        var mean = luminance.Average();
        return (mean, luminance.Count(l => Math.Abs(l - mean) > 80));
    }

    /// <summary>Vérifie le rendu dans les deux thèmes ; renvoie la hauteur de l'image en thème clair.</summary>
    private static int AssertThemes(Func<Window> create, string name)
    {
        var height = 0;
        RunSta(() =>
        {
            var window = create();
            var pixels = Render(window, dark: false, name);
            height = pixels.Length / 4 / (int)Math.Ceiling(window.Width);
            var light = Measure(pixels);
            Assert.Equal(AppInfo.Title, window.Title);
            var dark = Measure(Render(create(), dark: true, name));
            Assert.True(light.Mean > 180, $"fond clair : {light.Mean:F0}");
            Assert.True(dark.Mean < 90, $"fond sombre : {dark.Mean:F0}");
            // Texte et contrôles visibles dans les deux thèmes.
            Assert.True(light.Contrasted > 500, $"clair : {light.Contrasted} pixels contrastés");
            Assert.True(dark.Contrasted > 500, $"sombre : {dark.Contrasted} pixels contrastés");
        });
        return height;
    }

    private static UpdateService Updates(FakeRegistry registry) =>
        new(registry, new FakeReleases(FakeReleases.Latest), new FakeGreenshot(null), new FakeRunner(),
            () => @"C:\winget.exe", Path.GetTempPath());

    [Fact]
    public async Task SettingsAndInstallWindowsRenderInBothThemes()
    {
        var paths = NewPaths();
        var registry = new FakeRegistry().WithGreenshot("1.3.315", null);
        var setup = new Setup(paths, registry, new FakeShortcuts(), new GreenshotConfig(paths.GreenshotIni, new FakeGreenshot(null)));
        // Fenêtre ouverte depuis le lanceur installé : réglages complets.
        var settings = new SettingsViewModel(setup, Updates(registry), new AppSettings(), paths.InstalledLauncher, new FakeDialogs());
        await settings.OpenedAsync();
        Assert.Equal("GreenshotUpdater 1.0.0", AppInfo.Title);
        var settingsHeight = AssertThemes(() => new SettingsWindow(settings), "reglages");

        // Fenêtre ouverte depuis un autre dossier : options et bouton "Installer" seulement.
        var install = new SettingsViewModel(setup, Updates(registry), new AppSettings(), null, new FakeDialogs());
        var installHeight = AssertThemes(() => new SettingsWindow(install), "installation");
        Assert.True(installHeight < settingsHeight - 80, $"installation {installHeight} px, réglages {settingsHeight} px");
    }

    [Fact]
    public async Task UpdatePromptRendersInBothThemes()
    {
        var registry = new FakeRegistry().WithGreenshot("1.3.315", null);
        var service = Updates(registry);
        var check = await service.CheckAsync([]);
        AssertThemes(() => new UpdatePromptWindow(new UpdatePromptViewModel(check, service, new FakeDialogs())), "proposition");
    }
}
