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
        var dialogs = new FakeDialogs();
        var vm = new UpdatePromptViewModel(check, service, dialogs);
        var events = new List<string>();
        vm.RequestHide += () => events.Add($"masquée, occupée {vm.IsBusy}, lancements {runner.Runs.Count}");
        vm.RequestClose += () => events.Add($"fermée, occupée {vm.IsBusy}");
        Assert.True(vm.InstallCommand.CanExecute(null));

        await vm.InstallAsync();
        // Fenêtre masquée avant winget, fermée à la fin.
        Assert.Equal(["masquée, occupée True, lancements 0", "fermée, occupée False"], events);
        Assert.Equal(["close", $"start {exe}"], greenshot.Events);
        var run = Assert.Single(runner.Runs);
        Assert.Equal(@"C:\winget.exe", run.FileName);
        Assert.Equal(Winget.UpgradeArguments, run.Arguments);
        Assert.True(vm.Outcome!.Success);
        Assert.Equal("Greenshot est à jour (version 1.3.323).", vm.Outcome.Message);
        // Pas de message de succès.
        Assert.Empty(dialogs.Shown);
    }

    [Fact]
    public async Task GreenshotRestartedByItsInstallerIsNotStartedTwice()
    {
        var (registry, exe) = GreenshotOnDisk();
        var greenshot = new FakeGreenshot(exe);
        var runner = new FakeRunner { During = () => greenshot.StartedElsewhere(exe) };
        var service = Service(registry, new FakeReleases(FakeReleases.Latest), greenshot, runner);
        var vm = new UpdatePromptViewModel(await service.CheckAsync([]), service, new FakeDialogs());
        await vm.InstallAsync();
        Assert.Equal(["close"], greenshot.Events);
        Assert.True(greenshot.IsRunning);
    }

    [Fact]
    public async Task WingetFailureShowsTheCode()
    {
        var (registry, exe) = GreenshotOnDisk();
        var greenshot = new FakeGreenshot(exe);
        var service = Service(registry, new FakeReleases(FakeReleases.Latest), greenshot, new FakeRunner(unchecked((int)0x8A150011)));
        var dialogs = new FakeDialogs();
        var vm = new UpdatePromptViewModel(await service.CheckAsync([]), service, dialogs);
        var closed = false;
        vm.RequestClose += () => closed = true;
        await vm.InstallAsync();
        Assert.False(vm.Outcome!.Success);
        Assert.Equal("La mise à jour a échoué (code 0x8A150011).", Assert.Single(dialogs.Shown));
        Assert.True(closed);
        // Greenshot est relancé même après un échec.
        Assert.Equal(["close", $"start {exe}"], greenshot.Events);
    }

    [Theory]
    [InlineData(unchecked((int)0x8A15010C))]
    [InlineData(1223)]
    [InlineData(5)]
    public async Task CancellationShowsNothing(int code)
    {
        var (registry, exe) = GreenshotOnDisk();
        var greenshot = new FakeGreenshot(exe);
        var service = Service(registry, new FakeReleases(FakeReleases.Latest), greenshot, new FakeRunner(code));
        var dialogs = new FakeDialogs();
        var vm = new UpdatePromptViewModel(await service.CheckAsync([]), service, dialogs);
        await vm.InstallAsync();
        Assert.True(vm.Outcome!.Cancelled);
        Assert.Empty(dialogs.Shown);
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
        Assert.Null(vm.Outcome);
        Assert.True(vm.InstallCommand.CanExecute(null));

        vm = new UpdatePromptViewModel(check, service, new FakeDialogs());
        await vm.InstallAsync();
        var download = Assert.Single(releases.Downloads);
        Assert.Equal(check.Latest!.InstallerUrl, download.Url);
        var run = Assert.Single(runner.Runs);
        Assert.Equal(download.Destination, run.FileName);
        Assert.True(run.UseShellExecute);
        Assert.True(vm.Outcome!.Success);
    }

    [Fact]
    public async Task LaterClosesWithoutInstalling()
    {
        var (registry, exe) = GreenshotOnDisk();
        var greenshot = new FakeGreenshot(exe);
        var runner = new FakeRunner();
        var service = Service(registry, new FakeReleases(FakeReleases.Latest), greenshot, runner);
        var vm = new UpdatePromptViewModel(await service.CheckAsync([]), service, new FakeDialogs());
        Assert.Equal(["Installer", "Plus tard"], vm.Buttons.Select(b => b.Text));
        Assert.Same(vm.CloseCommand, vm.Buttons[1].Command);
        var closed = false;
        vm.RequestClose += () => closed = true;
        vm.CloseCommand.Execute(null);
        Assert.True(closed);
        Assert.Empty(runner.Runs);
        Assert.Empty(greenshot.Events);
    }
}
