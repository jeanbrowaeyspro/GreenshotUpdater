using static GreenshotUpdater.Tests.Fixtures;

namespace GreenshotUpdater.Tests;

/// <summary>Mise à jour de GreenshotUpdater lui-même, sans réseau (GitHub simulé).</summary>
public class SelfUpdateTests
{
    private static readonly Version Current = new(1, 1, 0, 0);

    /// <summary>Réponse de l'API pour le dépôt de GreenshotUpdater.</summary>
    private static string Json(string tag, bool prerelease = false) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "GreenshotUpdater {{tag}}",
          "prerelease": {{(prerelease ? "true" : "false")}},
          "assets": [
            { "name": "GreenshotUpdater.exe",
              "browser_download_url": "https://github.com/jeanbrowaeyspro/GreenshotUpdater/releases/download/{{tag}}/GreenshotUpdater.exe" }
          ]
        }
        """;

    private static ReleaseInfo? Release(string tag, bool prerelease = false) =>
        ReleaseInfo.Parse(Json(tag, prerelease), ReleaseFeed.Self);

    /// <summary>Lanceur installé factice dans un dossier temporaire.</summary>
    private static string InstalledLauncher()
    {
        var launcher = Path.Combine(NewFolder("install"), AppPaths.LauncherFileName);
        File.WriteAllText(launcher, "MZ ancien");
        return launcher;
    }

    [Fact]
    public void ReadsOnlyStableReleasesWithTheLauncher()
    {
        var release = Release("v1.2.0")!;
        Assert.Equal(new Version(1, 2, 0, 0), release.Version);
        Assert.Equal("GreenshotUpdater.exe", release.AssetName);
        Assert.EndsWith("/v1.2.0/GreenshotUpdater.exe", release.AssetUrl);
        Assert.Null(Release("v1.2.0", prerelease: true));
        Assert.Null(ReleaseInfo.Parse(Fixtures.ReleaseJson, ReleaseFeed.Self)!.AssetUrl);
        Assert.Equal("https://api.github.com/repos/jeanbrowaeyspro/GreenshotUpdater/releases/latest", ReleaseFeed.Self.ApiUrl);
    }

    [Fact]
    public async Task NewerVersionReplacesTheLauncherAndTheOldOneIsCleanedUpNextTime()
    {
        var launcher = InstalledLauncher();
        var releases = new FakeReleases(() => Release("v1.2.0")) { Content = "MZ nouveau" };
        var update = new SelfUpdate(releases, Current, launcher);

        Assert.True(await update.RunAsync());
        Assert.Equal(Release("v1.2.0")!.AssetUrl, Assert.Single(releases.Downloads).Url);
        Assert.Equal("MZ nouveau", File.ReadAllText(launcher));
        Assert.Equal("MZ ancien", File.ReadAllText(update.OldLauncher));
        Assert.False(File.Exists(update.DownloadedLauncher));

        update.CleanUp();
        Assert.False(File.Exists(update.OldLauncher));
        Assert.Equal([AppPaths.LauncherFileName], Directory.GetFiles(Path.GetDirectoryName(launcher)!).Select(Path.GetFileName));
    }

    public static TheoryData<Func<ReleaseInfo?>, string> NothingToDo => new()
    {
        { () => Release("v1.1.0"), "MZ nouveau" },            // même version
        { () => Release("v1.0.0"), "MZ nouveau" },            // version plus ancienne
        { () => null, "MZ nouveau" },                         // seule une préversion
        { FakeReleases.Offline, "MZ nouveau" },               // réseau absent
        { () => Release("v1.2.0"), "<html>erreur</html>" },   // téléchargement qui n'est pas un exe
    };

    [Theory]
    [MemberData(nameof(NothingToDo))]
    public async Task LauncherIsLeftUntouchedOtherwise(Func<ReleaseInfo?> answer, string content)
    {
        var launcher = InstalledLauncher();
        var update = new SelfUpdate(new FakeReleases(answer) { Content = content }, Current, launcher);
        Assert.False(await update.RunAsync());
        Assert.Equal([AppPaths.LauncherFileName], Directory.GetFiles(Path.GetDirectoryName(launcher)!).Select(Path.GetFileName));
        Assert.Equal("MZ ancien", File.ReadAllText(launcher));
    }

    [Fact]
    public async Task NotInstalledMeansNoQuery()
    {
        var releases = new FakeReleases(() => Release("v1.2.0"));
        var missing = Path.Combine(NewFolder(), AppPaths.LauncherFileName);
        Assert.False(await new SelfUpdate(releases, Current, missing).RunAsync());
        Assert.Equal(0, releases.Calls);
    }
}
