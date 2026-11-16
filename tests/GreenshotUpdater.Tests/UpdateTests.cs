using GreenshotUpdater.ViewModels;
using static GreenshotUpdater.Tests.Fixtures;

namespace GreenshotUpdater.Tests;

/// <summary>Vérification au démarrage, mise à jour silencieuse par winget ou par l'installeur GitHub.</summary>
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

    /// <summary>Proposition de mise à jour sur Greenshot 1.3.315 installé, 1.3.323 disponible.</summary>
    private static async Task<UpdatePromptViewModel> Prompt(UpdateService service, IDialogs? dialogs = null) =>
        new(await service.CheckAsync([]), service, dialogs ?? new FakeDialogs());

    /// <summary>Registre mis à jour à la version publiée, comme le fait l'installeur.</summary>
    private static Action Installs(FakeRegistry registry, string exe) =>
        () => registry.WithGreenshot("1.3.323", Path.GetDirectoryName(exe) + @"\");

    [Fact]
    public async Task WingetUpdateWaitsForTheEndThenConfirms()
    {
        var (registry, exe) = GreenshotOnDisk();
        var greenshot = new FakeGreenshot(exe);
        var installEnd = new TaskCompletionSource();
        var runner = new FakeRunner { During = Installs(registry, exe), Until = installEnd.Task };
        var dialogs = new FakeDialogs();
        var vm = await Prompt(Service(registry, new FakeReleases(FakeReleases.Latest), greenshot, runner), dialogs);
        var closed = 0;
        vm.RequestClose += () => closed++;
        Assert.True(vm.InstallCommand.CanExecute(null));

        var install = vm.InstallAsync();
        // Fenêtre d'attente jusqu'à la fin de winget, sans bouton ni fermeture possible.
        Assert.True(vm.IsWaiting);
        Assert.Equal(DialogKind.Info, vm.Kind);
        Assert.Equal("Installation de Greenshot 1.3.323…\n\nAutorisez la modification de l'appareil si Windows le demande.", vm.Message);
        Assert.Equal(vm.WaitingText, vm.Message);
        Assert.False(vm.CanClose);
        Assert.False(vm.InstallCommand.CanExecute(null));
        await WaitUntil(() => runner.Runs.Count == 1);
        await Task.Delay(50);
        Assert.True(vm.IsWaiting);
        Assert.Null(vm.Outcome);
        Assert.Equal(0, closed);

        installEnd.SetResult();
        await install.WaitAsync(TimeSpan.FromSeconds(10));
        var run = Assert.Single(runner.Runs);
        Assert.Equal(@"C:\winget.exe", run.FileName);
        Assert.Equal(Winget.UpgradeArguments, run.Arguments);
        Assert.True(run.CreateNoWindow);
        Assert.Equal(["close", $"start {exe}"], greenshot.Events);

        // La fenêtre devient la confirmation, OK ferme.
        Assert.True(vm.Outcome!.Success);
        Assert.False(vm.IsWaiting);
        Assert.Equal(DialogKind.Info, vm.Kind);
        Assert.Equal("Greenshot 1.3.323 est installé.", vm.Message);
        var ok = Assert.Single(vm.Buttons);
        Assert.Equal("OK", ok.Text);
        Assert.True(ok.IsDefault && ok.IsCancel);
        Assert.True(vm.CanClose);
        Assert.Equal(0, closed);
        ok.Command.Execute(null);
        Assert.Equal(1, closed);
        Assert.Empty(dialogs.Shown);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "délai dépassé");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task UnexpectedVersionIsReported()
    {
        // Installeur terminé sans erreur, mais le registre donne toujours 1.3.315.
        var (registry, exe) = GreenshotOnDisk();
        var vm = await Prompt(Service(registry, new FakeReleases(FakeReleases.Latest), new FakeGreenshot(exe), new FakeRunner()));
        await vm.InstallAsync();
        Assert.True(vm.Outcome!.Success);
        Assert.Equal("L'installation de Greenshot 1.3.323 s'est terminée, mais la version installée relue est 1.3.315.", vm.Message);
        Assert.Equal("Greenshot 1.3.323 est installé.", UpdateService.InstalledText(new Version(1, 3, 323, 0), new Version(1, 3, 323, 0)));
        Assert.EndsWith("relue est aucune.", UpdateService.InstalledText(new Version(1, 3, 323, 0), null));
    }

    [Fact]
    public async Task GreenshotIsStartedAfterASuccessEvenIfItWasNotRunning()
    {
        var (registry, exe) = GreenshotOnDisk();
        var greenshot = new FakeGreenshot(null);
        var runner = new FakeRunner { During = Installs(registry, exe) };
        var vm = await Prompt(Service(registry, new FakeReleases(FakeReleases.Latest), greenshot, runner));
        await vm.InstallAsync();
        Assert.Equal(["close", $"start {exe}"], greenshot.Events);
    }

    [Fact]
    public async Task GreenshotRestartedByItsInstallerIsNotStartedTwice()
    {
        var (registry, exe) = GreenshotOnDisk();
        var greenshot = new FakeGreenshot(exe);
        var runner = new FakeRunner { During = () => greenshot.StartedElsewhere(exe) };
        var vm = await Prompt(Service(registry, new FakeReleases(FakeReleases.Latest), greenshot, runner));
        await vm.InstallAsync();
        Assert.Equal(["close"], greenshot.Events);
        Assert.True(greenshot.IsRunning);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WingetFailureShowsTheCode(bool wasRunning)
    {
        var (registry, exe) = GreenshotOnDisk();
        var greenshot = new FakeGreenshot(wasRunning ? exe : null);
        var service = Service(registry, new FakeReleases(FakeReleases.Latest), greenshot, new FakeRunner(unchecked((int)0x8A150011)));
        var dialogs = new FakeDialogs();
        var vm = await Prompt(service, dialogs);
        var closed = false;
        vm.RequestClose += () => closed = true;
        await vm.InstallAsync();
        Assert.False(vm.Outcome!.Success);
        // La fenêtre d'attente devient le message d'erreur.
        Assert.False(vm.IsWaiting);
        Assert.Equal(DialogKind.Error, vm.Kind);
        Assert.Equal("La mise à jour a échoué (code 0x8A150011).", vm.Message);
        Assert.Equal("OK", Assert.Single(vm.Buttons).Text);
        Assert.False(closed);
        Assert.Empty(dialogs.Shown);
        // Greenshot relancé seulement s'il tournait avant.
        Assert.Equal(wasRunning ? ["close", $"start {exe}"] : ["close"], greenshot.Events);
    }

    [Theory]
    [InlineData(unchecked((int)0x8A15010C), true)]
    [InlineData(1223, true)]
    [InlineData(5, true)]
    [InlineData(1223, false)]
    public async Task CancellationClosesWithoutMessage(int code, bool wasRunning)
    {
        var (registry, exe) = GreenshotOnDisk();
        var greenshot = new FakeGreenshot(wasRunning ? exe : null);
        var service = Service(registry, new FakeReleases(FakeReleases.Latest), greenshot, new FakeRunner(code));
        var dialogs = new FakeDialogs();
        var vm = await Prompt(service, dialogs);
        var closed = false;
        vm.RequestClose += () => closed = true;
        await vm.InstallAsync();
        Assert.True(vm.Outcome!.Cancelled);
        Assert.True(closed);
        Assert.Empty(dialogs.Shown);
        Assert.Equal(wasRunning ? ["close", $"start {exe}"] : ["close"], greenshot.Events);
    }

    [Fact]
    public async Task WithoutWingetTheGitHubInstallerRunsSilently()
    {
        var (registry, exe) = GreenshotOnDisk();
        var releases = new FakeReleases(FakeReleases.Latest);
        var runner = new FakeRunner { During = Installs(registry, exe) };
        var service = Service(registry, releases, new FakeGreenshot(exe), runner, winget: null);

        var refused = new FakeDialogs(answer: false);
        var vm = await Prompt(service, refused);
        await vm.InstallAsync();
        Assert.Contains("Greenshot-INSTALLER-1.3.323-RELEASE.exe", Assert.Single(refused.Shown));
        Assert.Empty(runner.Runs);
        Assert.Null(vm.Outcome);
        Assert.False(vm.IsWaiting);
        Assert.True(vm.InstallCommand.CanExecute(null));

        vm = await Prompt(service);
        await vm.InstallAsync();
        var download = Assert.Single(releases.Downloads);
        Assert.Equal(FakeReleases.Latest()!.AssetUrl, download.Url);
        var run = Assert.Single(runner.Runs);
        Assert.Equal(download.Destination, run.FileName);
        Assert.Equal("/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART", run.Arguments);
        Assert.Equal(InnoSetup.SilentArguments, run.Arguments);
        // ShellExecute : l'installeur demande lui-même l'élévation (UAC).
        Assert.True(run.UseShellExecute);
        Assert.True(vm.Outcome!.Success);
        Assert.Equal("Greenshot 1.3.323 est installé.", vm.Message);
    }

    [Fact]
    public async Task LaterClosesWithoutInstalling()
    {
        var (registry, exe) = GreenshotOnDisk();
        var greenshot = new FakeGreenshot(exe);
        var runner = new FakeRunner();
        var vm = await Prompt(Service(registry, new FakeReleases(FakeReleases.Latest), greenshot, runner));
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
