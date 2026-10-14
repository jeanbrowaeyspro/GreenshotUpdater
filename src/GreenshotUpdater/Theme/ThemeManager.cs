using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace GreenshotUpdater.Theme;

/// <summary>
/// Thème Fluent clair ou sombre, qui suit le réglage "Mode Application" de Windows,
/// y compris quand il change pendant l'exécution.
/// </summary>
/// <remarks>
/// Les pinceaux propres à l'application (texte atténué, bordure des cadres) sont
/// renseignés ici pour chaque thème : aucune couleur n'est écrite dans les XAML.
/// </remarks>
public static class ThemeManager
{
    private static bool _listening;

    /// <summary>Vrai si Windows est réglé sur le thème sombre pour les applications.</summary>
    public static bool SystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception exc) when (exc is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Applique le thème de Windows à l'application et suit ses changements.</summary>
    public static void Apply(Application app)
    {
        if (!_listening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            app.Exit += (_, _) => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _listening = true;
        }
        var dark = SystemIsDark();
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
        ApplyBrushes(app.Resources, dark);
    }

    /// <summary>Pinceaux propres à l'application pour le thème donné.</summary>
    public static void ApplyBrushes(ResourceDictionary resources, bool dark)
    {
        resources["MutedTextBrush"] = Frozen(dark ? Color.FromRgb(0x98, 0xA2, 0xB0) : Color.FromRgb(0x6B, 0x74, 0x80));
        resources["PanelBorderBrush"] = Frozen(dark ? Color.FromRgb(0x33, 0x39, 0x44) : Color.FromRgb(0xDC, 0xE1, 0xE8));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
            return;
        var app = Application.Current;
        app?.Dispatcher.BeginInvoke(() =>
        {
            if (SystemIsDark() != (app.ThemeMode == ThemeMode.Dark))
                Apply(app);
        });
    }
}
