using EngelsizDPI.Core;

namespace EngelsizDPI.Tests;

public sealed class StartupHealthTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("engelsizdpi-health-").FullName;
    private readonly StartupHealth _health;
    private static readonly Version V1 = new(1, 3, 0), V2 = new(1, 4, 0);

    public StartupHealthTests() =>
        _health = new StartupHealth(Path.Combine(_dir, "startup-state"), Path.Combine(_dir, "skipped-version"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Consecutive_unfinished_startups_are_counted()
    {
        Assert.Equal(0, _health.BeginStartup(V2));
        Assert.False(_health.IsBroken(V2));
        Assert.Equal(1, _health.BeginStartup(V2));
        Assert.Equal(2, _health.BeginStartup(V2));
        Assert.True(_health.IsBroken(V2));
    }

    [Fact]
    public void Healthy_startup_resets_the_count()
    {
        _health.BeginStartup(V2);
        _health.BeginStartup(V2);
        _health.MarkHealthy();
        Assert.Equal(0, _health.BeginStartup(V2));
        Assert.False(_health.IsBroken(V2));
    }

    [Fact]
    public void Count_belongs_to_one_version()
    {
        _health.BeginStartup(V2);
        _health.BeginStartup(V2);
        Assert.True(_health.IsBroken(V2));
        Assert.False(_health.IsBroken(V1));
        Assert.Equal(0, _health.BeginStartup(V1));
    }

    [Fact]
    public void Skipped_version_is_remembered_and_state_cleared()
    {
        _health.BeginStartup(V2);
        _health.BeginStartup(V2);
        _health.Skip(V2);
        Assert.True(_health.IsSkipped(V2));
        Assert.False(_health.IsSkipped(V1));
        Assert.False(_health.IsBroken(V2));
    }

    [Fact]
    public void Missing_or_garbage_state_counts_as_healthy()
    {
        Assert.False(_health.IsBroken(V2));
        File.WriteAllText(Path.Combine(_dir, "startup-state"), "çöp");
        Assert.False(_health.IsBroken(V2));
        Assert.Equal(0, _health.BeginStartup(V2));
    }
}
