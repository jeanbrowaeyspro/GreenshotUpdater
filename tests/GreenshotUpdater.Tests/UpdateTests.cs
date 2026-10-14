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
        var vm = new UpdatePromptViewModel(check, service, dialogs, new FakeWindows().Wait());
        var events = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsWaiting))
                events.Add($"attente, lancements {runner.Runs.Count}");
        };
        vm.RequestHide += () => events.Add($"masquée, occupée {vm.IsBusy}");
        vm.RequestClose += () => events.Add($"fermée, occupée {vm.IsBusy}");
        Assert.True(vm.InstallCommand.CanExecute(null));

        await vm.InstallAsync();
        // Attente affichée avant winget ; winget terminé sans assistant visible : masquée, puis fermée.
        Assert.Equal(["attente, lancements 0", "masquée, occupée True", "fermée, occupée False"], events);
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
        var vm = new UpdatePromptViewModel(await service.CheckAsync([]), service, new FakeDialogs(), new FakeWindows().Wait());
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
        var vm = new UpdatePromptViewModel(await service.CheckAsync([]), service, dialogs, new FakeWindows().Wait());
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
        var vm = new UpdatePromptViewModel(await service.CheckAsync([]), service, dialogs, new FakeWindows().Wait());
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
        var vm = new UpdatePromptViewModel(check, service, refused, new FakeWindows().Wait());
        await vm.InstallAsync();
        Assert.Contains("Greenshot-INSTALLER-1.3.323-RELEASE.exe", Assert.Single(refused.Shown));
        Assert.Empty(runner.Runs);
        Assert.Null(vm.Outcome);
        Assert.True(vm.InstallCommand.CanExecute(null));

        vm = new UpdatePromptViewModel(check, service, new FakeDialogs(), new FakeWindows().Wait());
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
        var vm = new UpdatePromptViewModel(await service.CheckAsync([]), service, new FakeDialogs(), new FakeWindows().Wait());
        Assert.Equal(["Installer", "Plus tard"], vm.Buttons.Select(b => b.Text));
        Assert.Same(vm.CloseCommand, vm.Buttons[1].Command);
        var closed = false;
        vm.RequestClose += () => closed = true;
        vm.CloseCommand.Execute(null);
        Assert.True(closed);
        Assert.Empty(runner.Runs);
        Assert.Empty(greenshot.Events);
    }

    [Fact]
    public async Task WaitingWindowIsHiddenWhenTheWizardAppears()
    {
        var (registry, exe) = GreenshotOnDisk();
        var greenshot = new FakeGreenshot(exe);
        var installerClosed = new TaskCompletionSource();
        var runner = new FakeRunner { Until = installerClosed.Task };
        var service = Service(registry, new FakeReleases(FakeReleases.Latest), greenshot, runner);
        // Assistant visible au troisième examen des fenêtres.
        var windows = new FakeWindows([], [], [], [FakeWindows.Wizard]);
        var dialogs = new FakeDialogs();
        var vm = new UpdatePromptViewModel(await service.CheckAsync([]), service, dialogs, windows.Wait());
        var hidden = new TaskCompletionSource();
        vm.RequestHide += hidden.SetResult;
        var closed = false;
        vm.RequestClose += () => closed = true;

        var install = vm.InstallAsync();
        Assert.True(vm.IsWaiting);
        Assert.Equal(DialogKind.Info, vm.Kind);
        Assert.StartsWith("Préparation de la mise à jour de Greenshot 1.3.323…", vm.Message);
        Assert.Equal(vm.WaitingText, vm.Message);
        Assert.False(vm.CanClose);

        await hidden.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // Masquée à l'apparition de l'assistant, pendant l'installation.
        Assert.Equal(4, windows.Calls);
        Assert.Null(vm.Outcome);
        Assert.True(vm.IsBusy);
        Assert.False(closed);

        installerClosed.SetResult();
        await install.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(closed);
        Assert.True(vm.Outcome!.Success);
        Assert.Equal(["close", $"start {exe}"], greenshot.Events);
        Assert.Empty(dialogs.Shown);
    }

    [Fact]
    public async Task InstallerWaitEndsWhenTheInstallFinishes()
    {
        var installer = new TaskCompletionSource<int>();
        var windows = new FakeWindows();
        var wait = windows.Wait().WaitAsync(installer.Task);
        await Task.Delay(20);
        Assert.False(wait.IsCompleted);
        // winget terminé (échec, UAC refusée…) sans que l'assistant soit apparu.
        installer.SetResult(1223);
        Assert.Equal(InstallerWaitEnd.InstallFinished, await wait.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task InstallerWaitEndsAfterTheTimeout()
    {
        var never = new TaskCompletionSource<int>().Task;
        var end = await new FakeWindows().Wait(TimeSpan.FromMilliseconds(50)).WaitAsync(never)
                                          .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(InstallerWaitEnd.TimedOut, end);
        Assert.InRange(new InstallerWindowWait(new FakeWindows()).Timeout.TotalMinutes, 2, 10);
    }

    [Fact]
    public async Task InstallerWindowAlreadyOpenIsIgnored()
    {
        // Page "Greenshot" déjà ouverte dans un navigateur : ignorée ; l'assistant apparu ensuite compte.
        var page = new TopWindow(7, "Chrome_WidgetWin_1", "Greenshot - GitHub", 900, "chrome");
        var windows = new FakeWindows([page], [page], [page], [page, FakeWindows.Wizard]);
        var never = new TaskCompletionSource<int>().Task;
        var end = await windows.Wait().WaitAsync(never).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(InstallerWaitEnd.InstallerShown, end);
        Assert.Equal(4, windows.Calls);
    }

    [Theory]
    [InlineData("TWizardForm", "Installation - Greenshot", "Greenshot-INSTALLER-1.3.323-RELEASE.tmp", true)]
    [InlineData("TSelectLanguageForm", "Langue de l'assistant d'installation", "", true)]
    [InlineData("TMainForm", "Greenshot", "Greenshot-INSTALLER-1.3.323-RELEASE.tmp", true)]
    [InlineData("WindowsForms10.Window.8.app", "Greenshot", "Greenshot", false)]
    [InlineData("CabinetWClass", "Documents", "explorer", false)]
    public void InstallerWindowsAreRecognised(string className, string title, string process, bool expected) =>
        Assert.Equal(expected, InstallerWindowWait.IsInstaller(new TopWindow(1, className, title, 4242, process)));

    [Fact]
    public void OwnWindowsAreNotTheInstaller() =>
        Assert.False(InstallerWindowWait.IsInstaller(
            new TopWindow(1, "HwndWrapper[GreenshotUpdater.App]", "Préparation de la mise à jour de Greenshot", Environment.ProcessId, "")));
}
