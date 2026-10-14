using System.Text;
using static GreenshotUpdater.Tests.Fixtures;

namespace GreenshotUpdater.Tests;

/// <summary>Versions, réponse GitHub, Greenshot.ini, registre, winget, réglages, journal.</summary>
public class CoreTests
{
    [Theory]
    [InlineData("v1.3.323", "1.3.323.0")]
    [InlineData("1.3.315", "1.3.315.0")]
    [InlineData("Greenshot 1.2.10.6", "1.2.10.6")]
    [InlineData("v1.4.0-UNSTABLE", "1.4.0.0")]
    public void ParsesVersions(string text, string expected) =>
        Assert.Equal(Version.Parse(expected), VersionText.Parse(text));

    [Fact]
    public void ComparesVersionsWrittenDifferently()
    {
        Assert.Equal(VersionText.Parse("1.3.315"), VersionText.Parse("1.3.315.0"));
        Assert.True(VersionText.Parse("v1.3.323") > VersionText.Parse("1.3.315"));
        Assert.True(VersionText.Parse("1.3.4") < VersionText.Parse("1.3.315"));
        Assert.Null(VersionText.Parse("aucune"));
        Assert.Equal("1.3.323", VersionText.Display(VersionText.Parse("v1.3.323")!));
    }

    [Fact]
    public void ReadsGitHubRelease()
    {
        var release = ReleaseInfo.Parse(ReleaseJson)!;
        Assert.Equal(new Version(1, 3, 323, 0), release.Version);
        Assert.Equal("v1.3.323", release.Tag);
        Assert.Equal("Greenshot-INSTALLER-1.3.323-RELEASE.exe", release.InstallerName);
        Assert.EndsWith("/v1.3.323/Greenshot-INSTALLER-1.3.323-RELEASE.exe", release.InstallerUrl);
    }

    [Theory]
    [InlineData("""{ "tag_name": "v1.4.107", "name": "Greenshot 1.4.107 UNSTABLE", "prerelease": false }""")]
    [InlineData("""{ "tag_name": "v1.4.107-UNSTABLE", "name": "Greenshot", "prerelease": false }""")]
    [InlineData("""{ "tag_name": "v1.4.107", "name": "Greenshot", "prerelease": true }""")]
    [InlineData("""{ "tag_name": "nightly", "name": "Greenshot" }""")]
    public void IgnoresUnstableReleases(string json) => Assert.Null(ReleaseInfo.Parse(json));

    [Fact]
    public void ReleaseWithoutInstaller()
    {
        var release = ReleaseInfo.Parse("""{ "tag_name": "v1.3.400", "assets": [] }""")!;
        Assert.Equal(new Version(1, 3, 400, 0), release.Version);
        Assert.Null(release.InstallerUrl);
    }

    // ------------------------------------------------------------- Greenshot.ini

    [Fact]
    public void ChangesOnlyTheCoreLine()
    {
        Assert.Equal(14, GreenshotIni.Read(IniSample));
        var changed = GreenshotIni.With(IniSample, 0);
        Assert.Equal(0, GreenshotIni.Read(changed));
        Assert.Equal(IniSample.Replace("UpdateCheckInterval=14", "UpdateCheckInterval=0"), changed);
        // La clé homonyme d'une autre section n'est pas touchée.
        Assert.Contains("[Editor]\r\nUpdateCheckInterval=99\r\n", changed);
        Assert.Equal(IniSample, GreenshotIni.With(changed, 14));
    }

    [Fact]
    public void AddsMissingLineOrSection()
    {
        var withoutLine = "[Core]\nLanguage=fr-FR\n[Editor]\n";
        Assert.Null(GreenshotIni.Read(withoutLine));
        Assert.Equal("[Core]\nUpdateCheckInterval=0\nLanguage=fr-FR\n[Editor]\n", GreenshotIni.With(withoutLine, 0));

        var withoutSection = "[Editor]\r\nX=1";
        Assert.Equal("[Editor]\r\nX=1\r\n[Core]\r\nUpdateCheckInterval=0\r\n", GreenshotIni.With(withoutSection, 0));
        Assert.Equal(0, GreenshotIni.Read(GreenshotIni.With(withoutSection, 0)));
    }

    [Fact]
    public void FileKeepsEncodingAndLineEndings()
    {
        var folder = NewFolder();
        // UTF-8 avec BOM et caractères accentués, comme l'écrit Greenshot.
        var utf8 = Path.Combine(folder, "utf8.ini");
        var text = IniSample.Replace("fr-FR", "fr-FR ; légende é");
        File.WriteAllText(utf8, text, new UTF8Encoding(true));
        var before = File.ReadAllBytes(utf8);
        GreenshotIni.WriteFile(utf8, 0);
        Assert.Equal(0, GreenshotIni.ReadFile(utf8));
        var after = File.ReadAllBytes(utf8);
        Assert.Equal(before.Length - 1, after.Length);
        Assert.Equal(new UTF8Encoding(true).GetPreamble(), after[..3]);
        Assert.Equal(text.Replace("Interval=14", "Interval=0"), new UTF8Encoding(false).GetString(after[3..]));

        // UTF-16 avec BOM.
        var utf16 = Path.Combine(folder, "utf16.ini");
        File.WriteAllText(utf16, text, Encoding.Unicode);
        GreenshotIni.WriteFile(utf16, 0);
        Assert.Equal(0, GreenshotIni.ReadFile(utf16));
        Assert.Equal(Encoding.Unicode.GetPreamble(), File.ReadAllBytes(utf16)[..2]);
        Assert.Equal(text.Replace("Interval=14", "Interval=0"), File.ReadAllText(utf16));

        Assert.Null(GreenshotIni.ReadFile(Path.Combine(folder, "absent.ini")));
    }

    [Fact]
    public void GreenshotIsClosedBeforeTheIniChangesAndRestarted()
    {
        var paths = NewPaths();
        Directory.CreateDirectory(Path.GetDirectoryName(paths.GreenshotIni)!);
        File.WriteAllText(paths.GreenshotIni, IniSample, new UTF8Encoding(true));
        // À sa fermeture, Greenshot réécrit son fichier (ici avec une autre ligne modifiée).
        var greenshot = new FakeGreenshot(@"C:\Greenshot\Greenshot.exe")
        {
            OnClose = () => File.WriteAllText(paths.GreenshotIni, IniSample.Replace("fr-FR", "en-US"), new UTF8Encoding(true)),
        };
        var config = new GreenshotConfig(paths.GreenshotIni, greenshot);

        Assert.Null(config.SetUpdateCheck(disable: true));
        Assert.Equal(["close", @"start C:\Greenshot\Greenshot.exe"], greenshot.Events);
        var text = File.ReadAllText(paths.GreenshotIni);
        Assert.Contains("Language=en-US", text);
        Assert.Equal(0, GreenshotIni.Read(text));

        // Valeur déjà bonne : Greenshot n'est pas dérangé.
        greenshot.Events.Clear();
        config.SetUpdateCheck(disable: true);
        Assert.Empty(greenshot.Events);
    }

    [Fact]
    public void MissingIniIsReported()
    {
        var paths = NewPaths();
        var greenshot = new FakeGreenshot(null);
        Assert.Contains("introuvable", new GreenshotConfig(paths.GreenshotIni, greenshot).SetUpdateCheck(true));
        Assert.Empty(greenshot.Events);
    }

    // ------------------------------------------------------------- registre

    [Fact]
    public void FindsInstalledGreenshot()
    {
        Assert.Null(InstalledGreenshot.Find(new FakeRegistry()));

        var registry = new FakeRegistry()
            .WithGreenshot("1.2.10", @"C:\Ancien\", RegistryRoot.LocalMachine32, "Greenshot_old")
            .WithGreenshot("1.3.315", @"C:\Program Files\Greenshot\");
        registry.SetString(RegistryRoot.CurrentUser, $@"{InstalledGreenshot.UninstallPath}\Autre", "DisplayName", "Autre 9.9");
        var found = InstalledGreenshot.Find(registry)!;
        Assert.Equal(new Version(1, 3, 315, 0), found.Version);
        Assert.Equal(@"C:\Program Files\Greenshot\Greenshot.exe", found.ExePath);

        var perUser = new FakeRegistry().WithGreenshot("1.3.300", null, RegistryRoot.CurrentUser);
        Assert.Equal(new Version(1, 3, 300, 0), InstalledGreenshot.Find(perUser)!.Version);
        Assert.Null(InstalledGreenshot.Find(perUser)!.ExePath);
    }

    // ------------------------------------------------------------- winget

    [Fact]
    public void BuildsWingetCommand()
    {
        var start = Winget.UpgradeCommand(@"C:\winget.exe");
        Assert.Equal(@"C:\winget.exe", start.FileName);
        Assert.Equal("upgrade --id Greenshot.Greenshot --exact --interactive --accept-source-agreements --accept-package-agreements",
                     start.Arguments);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
    }

    [Fact]
    public void FindsWinget()
    {
        string[] dirs = [@"C:\A", @"C:\B", @"C:\C"];
        Assert.Equal(@"C:\B\winget.exe", Winget.Find(dirs, path => path.StartsWith(@"C:\B") || path.StartsWith(@"C:\C")));
        Assert.Null(Winget.Find(dirs, _ => false));
        Assert.Equal("0", Winget.ExitCodeText(0));
        Assert.Equal("0x8A15002B", Winget.ExitCodeText(unchecked((int)0x8A15002B)));
    }

    // ------------------------------------------------------------- divers

    [Theory]
    [InlineData(new string[0], false, null)]
    [InlineData(new[] { "/demarrage" }, true, null)]
    [InlineData(new[] { "/lanceur", @"C:\Téléchargements\GreenshotUpdater.exe" }, false, @"C:\Téléchargements\GreenshotUpdater.exe")]
    [InlineData(new[] { "/LANCEUR", @"C:\x.exe", "/Demarrage" }, true, @"C:\x.exe")]
    [InlineData(new[] { "/lanceur" }, false, null)]
    public void ParsesArguments(string[] args, bool startup, string? launcher) =>
        Assert.Equal(new AppArguments(startup, launcher), AppArguments.Parse(args));

    [Fact]
    public void SettingsRoundTripInSnakeCase()
    {
        var paths = NewPaths();
        var defaults = AppSettings.Load(paths.SettingsFile);
        Assert.True(defaults.RunAtStartup);
        Assert.True(defaults.DisableGreenshotUpdateCheck);
        Assert.False(defaults.DesktopShortcut);
        Assert.True(defaults.StartMenuShortcut);

        new AppSettings { RunAtStartup = false, DesktopShortcut = true }.Save(paths.SettingsFile);
        var json = File.ReadAllText(paths.SettingsFile);
        Assert.Contains("\"run_at_startup\": false", json);
        Assert.Contains("\"disable_greenshot_update_check\": true", json);
        Assert.Contains("\"desktop_shortcut\": true", json);
        Assert.Contains("\"start_menu_shortcut\": true", json);
        var loaded = AppSettings.Load(paths.SettingsFile);
        Assert.False(loaded.RunAtStartup);
        Assert.True(loaded.DesktopShortcut);

        File.WriteAllText(paths.SettingsFile, "{ illisible");
        Assert.True(AppSettings.Load(paths.SettingsFile).RunAtStartup);
    }

    [Fact]
    public void InstallDirIsRecognised()
    {
        var paths = NewPaths();
        Assert.True(paths.IsInInstallDir(paths.InstalledApp));
        Assert.True(paths.IsInInstallDir(paths.InstalledApp.ToUpperInvariant()));
        Assert.False(paths.IsInInstallDir(Path.Combine(paths.TempDir, AppPaths.AppFileName)));
        Assert.False(paths.IsInInstallDir(null));
    }

    [Fact]
    public void LogRotates()
    {
        var path = Path.Combine(NewFolder(), "journal.log");
        Log.Configure(path);
        try
        {
            File.WriteAllText(path, new string('x', (int)Log.MaxBytes + 1));
            Log.Info("après rotation");
            Assert.True(File.Exists(Path.ChangeExtension(path, ".old.log")));
            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} INFO après rotation", File.ReadAllText(path));
        }
        finally
        {
            Log.Configure("");
        }
    }

    [Fact]
    public void WritesRealShortcut()
    {
        // Vrai raccourci, écrit dans un dossier temporaire uniquement.
        var folder = NewFolder();
        var target = Path.Combine(folder, AppPaths.LauncherFileName);
        File.WriteAllText(target, "");
        var link = Path.Combine(folder, "menu", AppPaths.ShortcutFileName);
        new ShellLinkWriter().Create(link, target, Setup.ShortcutDescription);
        var bytes = File.ReadAllBytes(link);
        Assert.Equal(0x4C, bytes[0]);   // en-tête ShellLink
        Assert.Contains(AppPaths.LauncherFileName, Encoding.Unicode.GetString(bytes) + Encoding.Default.GetString(bytes));
    }
}
