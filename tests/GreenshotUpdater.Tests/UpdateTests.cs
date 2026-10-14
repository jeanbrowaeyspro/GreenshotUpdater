using GreenshotUpdater.ViewModels;
using static GreenshotUpdater.Tests.Fixtures;

namespace GreenshotUpdater.Tests;

/// <summary>Vérification au démarrage, mise à jour par winget ou par l'installeur GitHub.</summary>
public class UpdateTests
{
    /// <summary>Greenshot 1.3.315 installé dans un dossier temporaire (exécutable factice).</summary>
    private static (FakeRegistry Registry, string Exe) GreenshotOnDisk(string version = "1.3.315")
    {
        var folder = NewFolder();
        var exe = Path.Combine(folder, "Greenshot.exe");
        File.WriteAllText(exe, "");
        return (new FakeRegistry().WithGreenshot(version, folder + @"\"), exe);
    }

    private static UpdateService Service(FakeRegistry registry, FakeReleases releases, FakeGreenshot greenshot,
                                         FakeRunner runner, string? winget = @"C:\winget.exe", List<TimeSpan>? waits = null) =>
        new(registry, releases, greenshot, runner, () => winget, NewFolder("temp"))
        {
            Wait = (delay, _) =>
            {
                waits?.Add(delay);
                return Task.CompletedTask;
            },
        };

    [Fact]
    public void StartupRetriesLastAboutOneMinute() =>
        Assert.InRange(UpdateService.StartupRetryDelays.Sum(d => d.TotalSeconds), 50, 70);

    [Fact]
    public async Task NetworkNotReadyIsRetried()
    {
        var (registry, _) = GreenshotOnDisk();
        var waits = new List<TimeSpan>();
        var releases = new FakeReleases(FakeReleases.Offline, FakeReleases.Offline, FakeReleases.Latest);
        var check = await Service(registry, releases, new FakeGreenshot(null), new FakeRunner(), waits: waits)
            .CheckAsync(UpdateService.StartupRetryDelays);
        Assert.Equal(3, releases.Calls);
        Assert.Equal(UpdateService.StartupRetryDelays[..2], waits);
        Assert.True(check.UpdateAvailable);
        Assert.Equal("Greenshot 1.3.323 est disponible (version installée : 1.3.315). Installer maintenant ?", check.OfferText);
    }

    [Fact]
    public async Task NetworkAbsentGivesUpSilently()
    {
        var (registry, _) = GreenshotOnDisk();
        var waits = new List<TimeSpan>();
        var releases = new FakeReleases(FakeReleases.Offline);
        var check = await Service(registry, releases, new FakeGreenshot(null), new FakeRunner(), waits: waits)
            .CheckAsync(UpdateService.StartupRetryDelays);
        Assert.Equal(UpdateService.StartupRetryDelays.Length + 1, releases.Calls);
        Assert.Equal(UpdateService.StartupRetryDelays, waits);
        Assert.False(check.UpdateAvailable);
        Assert.NotNull(check.Error);
    }

    [Fact]
    public async Task NothingToOffer()
    {
        var (upToDate, _) = GreenshotOnDisk("1.3.323");
        var check = await Service(upToDate, new FakeReleases(FakeReleases.Latest), new FakeGreenshot(null), new FakeRunner())
            .CheckAsync([]);
        Assert.False(check.UpdateAvailable);

        // Greenshot absent : rien à proposer.
        check = await Service(new FakeRegistry(), new FakeReleases(FakeReleases.Latest), new FakeGreenshot(null), new FakeRunner())
            .CheckAsync([]);
        Assert.Null(check.Installed);
        Assert.False(check.UpdateAvailable);

        // Seule une préversion (null) : rien à proposer.
        var (registry, _) = GreenshotOnDisk();
        check = await Service(registry, new FakeReleases(() => null), new FakeGreenshot(null), new FakeRunner()).CheckAsync([]);
        Assert.False(check.UpdateAvailable);
    }

    [Fact]
    public async Task WingetUpdateClosesRunsAndRestartsGreenshot()
    {
        var (registry, exe) = GreenshotOnDisk();
        var greenshot = new FakeGreenshot(exe);
        // Pendant winget, le registre passe à la nouvelle version.
        var runner = new FakeRunner { During = () => registry.WithGreenshot("1.3.323", Path.GetDirectoryName(exe) + @"\") };
        var service = Service(registry, new FakeReleases(FakeReleases.Latest), greenshot, runner);
        var check = await service.CheckAsync([]);
        var vm = new UpdatePromptViewModel(check, service, new FakeDialogs());
        Assert.True(vm.CanChoose);

        await vm.InstallAsync();
        Assert.Equal(["close", $"start {exe}"], greenshot.Events);
        var run = Assert.Single(runner.Runs);
        Assert.Equal(@"C:\winget.exe", run.FileName);
        Assert.Equal(Winget.UpgradeArguments, run.Arguments);
        Assert.True(vm.Success);
        Assert.Equal("Greenshot est à jour (version 1.3.323).", vm.Message);
        Assert.True(vm.IsDone);
        Assert.False(vm.CanChoose);
        Assert.True(vm.CloseCommand.CanExecute(null));
    }

    [Fact]
    public async Task WingetFailureShowsTheCode()
    {
        var (registry, exe) = GreenshotOnDisk();
        var greenshot = new FakeGreenshot(exe);
        var service = Service(registry, new FakeReleases(FakeReleases.Latest), greenshot, new FakeRunner(unchecked((int)0x8A150011)));
        var vm = new UpdatePromptViewModel(await service.CheckAsync([]), service, new FakeDialogs());
        await vm.InstallAsync();
        Assert.False(vm.Success);
        Assert.Equal("La mise à jour a échoué (code 0x8A150011).", vm.Message);
        // Greenshot est relancé même après un échec.
        Assert.Equal(["close", $"start {exe}"], greenshot.Events);
    }

    [Fact]
    public async Task WithoutWingetTheGitHubInstallerIsOffered()
    {
        var (registry, exe) = GreenshotOnDisk();
        var releases = new FakeReleases(FakeReleases.Latest);
        var runner = new FakeRunner();
        var service = Service(registry, releases, new FakeGreenshot(exe), runner, winget: null);
        var check = await service.CheckAsync([]);

        var refused = new FakeDialogs(answer: false);
        var vm = new UpdatePromptViewModel(check, service, refused);
        await vm.InstallAsync();
        Assert.Contains("Greenshot-INSTALLER-1.3.323-RELEASE.exe", Assert.Single(refused.Shown));
        Assert.Empty(runner.Runs);
        Assert.True(vm.CanChoose);

        vm = new UpdatePromptViewModel(check, service, new FakeDialogs());
        await vm.InstallAsync();
        var download = Assert.Single(releases.Downloads);
        Assert.Equal(check.Latest!.InstallerUrl, download.Url);
        var run = Assert.Single(runner.Runs);
        Assert.Equal(download.Destination, run.FileName);
        Assert.True(run.UseShellExecute);
        Assert.True(vm.Success);
    }

    [Fact]
    public async Task LaterClosesWithoutInstalling()
    {
        var (registry, exe) = GreenshotOnDisk();
        var greenshot = new FakeGreenshot(exe);
        var runner = new FakeRunner();
        var service = Service(registry, new FakeReleases(FakeReleases.Latest), greenshot, runner);
        var vm = new UpdatePromptViewModel(await service.CheckAsync([]), service, new FakeDialogs());
        var closed = false;
        vm.RequestClose += () => closed = true;
        vm.CloseCommand.Execute(null);
        Assert.True(closed);
        Assert.Empty(runner.Runs);
        Assert.Empty(greenshot.Events);
    }
}
