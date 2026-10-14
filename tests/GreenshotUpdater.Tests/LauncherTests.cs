using System.Runtime.InteropServices;
using GreenshotUpdater.Launcher;
using static GreenshotUpdater.Tests.Fixtures;

namespace GreenshotUpdater.Tests;

/// <summary>Tests du lanceur : détection du runtime, ligne de commande, extraction de l'application.</summary>
public class LauncherTests
{
    private static string FakeRoot(params string[] versions)
    {
        var root = NewFolder("dotnet");
        foreach (var version in versions)
            Directory.CreateDirectory(Path.Combine(root, "shared", RuntimeCheck.Framework, version));
        return root;
    }

    [Fact]
    public void DetectsRequiredMajorVersion()
    {
        Assert.True(RuntimeCheck.HasFramework(FakeRoot("8.0.11", "10.0.10")));
        Assert.True(RuntimeCheck.HasFramework(FakeRoot("10.0.0-rc.1.25451.107")));
        Assert.False(RuntimeCheck.HasFramework(FakeRoot("8.0.11", "9.0.5")));
        Assert.False(RuntimeCheck.HasFramework(FakeRoot()));
        Assert.False(RuntimeCheck.IsInstalled([FakeRoot("9.0.5"), FakeRoot()]));
        Assert.True(RuntimeCheck.IsInstalled([FakeRoot("9.0.5"), FakeRoot("10.0.1")]));
    }

    [Fact]
    public void RuntimeFoundOnThisMachine() =>
        Assert.True(RuntimeCheck.IsInstalled(RuntimeCheck.CandidateRoots()));

    [Fact]
    public void InstallerUrlIsTheStableX64One() =>
        Assert.Equal("https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe", RuntimeCheck.InstallerUrl);

    [Theory]
    [InlineData("\"C:\\Program Files\\GU\\GreenshotUpdater.exe\" /demarrage", "/demarrage")]
    [InlineData("GreenshotUpdater.exe /demarrage", "/demarrage")]
    [InlineData("\"C:\\GU\\GreenshotUpdater.exe\"", "")]
    [InlineData("GreenshotUpdater.exe", "")]
    public void ExtractsArguments(string commandLine, string expected) =>
        Assert.Equal(expected, CommandLine.Arguments(commandLine));

    [Theory]
    [InlineData("\"C:\\Users\\j\\AppData\\Local\\Programs\\GreenshotUpdater\\GreenshotUpdater.exe\" /demarrage",
                "/lanceur \"C:\\L\\GreenshotUpdater.exe\" /demarrage")]
    [InlineData("\"C:\\Téléchargements\\GreenshotUpdater.exe\"", "/lanceur \"C:\\L\\GreenshotUpdater.exe\"")]
    public void ForwardsLauncherPathAndArguments(string commandLine, string expected) =>
        Assert.Equal(expected, CommandLine.ForApp(@"C:\L\GreenshotUpdater.exe", commandLine));

    [Fact]
    public void LauncherAndAppAgreeOnArguments()
    {
        // Ligne produite par le lanceur, découpée comme Windows le fait pour l'application.
        var line = CommandLine.ForApp(@"C:\Mes documents\GreenshotUpdater.exe", "GreenshotUpdater.exe /demarrage");
        Assert.Equal(new AppArguments(true, @"C:\Mes documents\GreenshotUpdater.exe"), AppArguments.Parse(Split(line)));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    /// <summary>Découpage de Windows (CommandLineToArgvW), sans le nom du programme.</summary>
    private static string[] Split(string arguments)
    {
        var argv = CommandLineToArgvW("app.exe " + arguments, out var count);
        try
        {
            return Enumerable.Range(1, count - 1)
                             .Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!)
                             .ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(argv);
        }
    }

    [Fact]
    public void ExtractsAppOnlyWhenMissingOrDifferent()
    {
        // Dossier de données absent : créé à l'extraction.
        var path = AppLocations.ExtractedApp(AppLocations.DataDir(NewFolder()));
        byte[] v1 = [1, 2, 3, 4], v2 = [1, 2, 3, 5], v3 = [1, 2, 3];

        Assert.False(EmbeddedApp.IsUpToDate(path, v1));
        Assert.True(EmbeddedApp.Extract(v1, path));
        Assert.True(EmbeddedApp.IsUpToDate(path, v1));
        Assert.False(EmbeddedApp.Extract(v1, path));

        // Même taille, contenu différent : empreinte différente.
        Assert.False(EmbeddedApp.IsUpToDate(path, v2));
        Assert.True(EmbeddedApp.Extract(v2, path));
        Assert.Equal(v2, File.ReadAllBytes(path));

        // Taille différente.
        Assert.True(EmbeddedApp.Extract(v3, path));
        Assert.Equal(v3, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
