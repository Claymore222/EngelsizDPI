using EngelsizDPI.Core;

namespace EngelsizDPI.Tests;

public sealed class InstallerTests
{
    private const string Installed = @"C:\Program Files\EngelsizDPI\EngelsizDPI.exe";
    private const string Downloaded = @"C:\Users\x\Downloads\EngelsizDPI.exe";
    private static readonly Version V1 = new(1, 0, 0), V2 = new(1, 1, 0);

    [Fact]
    public void Development_build_never_installs() =>
        Assert.Equal(LaunchAction.RunHere, Installer.Decide(false, Downloaded, Installed, V2, null));

    [Fact]
    public void Running_from_install_dir_runs_normally() =>
        Assert.Equal(LaunchAction.RunHere, Installer.Decide(true, Installed, Installed, V1, V1));

    [Fact]
    public void Install_dir_comparison_ignores_case() =>
        Assert.Equal(LaunchAction.RunHere,
            Installer.Decide(true, @"c:\program files\engelsizdpi\ENGELSIZDPI.EXE", Installed, V1, V1));

    [Fact]
    public void First_run_installs() =>
        Assert.Equal(LaunchAction.InstallAndLaunch, Installer.Decide(true, Downloaded, Installed, V1, null));

    [Fact]
    public void Newer_download_upgrades_installed_copy() =>
        Assert.Equal(LaunchAction.InstallAndLaunch, Installer.Decide(true, Downloaded, Installed, V2, V1));

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, 1, 0)]
    public void Same_or_older_download_opens_installed_copy(int major, int minor, int build) =>
        Assert.Equal(LaunchAction.LaunchInstalled,
            Installer.Decide(true, Downloaded, Installed, new Version(major, minor, build), V2));
}
