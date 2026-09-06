namespace Pacx.Predictor.Tests;

public class TreeCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pacx-predictor-tests", Guid.NewGuid().ToString("N"));
    private readonly TreeCache _cache;

    public TreeCacheTests() => _cache = new TreeCache(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Roundtrip_returns_what_was_saved()
    {
        var output = new PacxOutput("{\"commands\":[],\"namespaces\":[]}", "# completer\nRegister-ArgumentCompleter");
        _cache.Save("key-1", output);

        var loaded = _cache.TryLoad("key-1");
        Assert.Equal(output, loaded);
    }

    [Fact]
    public void Empty_or_missing_cache_returns_null()
    {
        Assert.Null(_cache.TryLoad("key-1"));
    }

    [Fact]
    public void Different_key_invalidates()
    {
        _cache.Save("key-1", new PacxOutput("{}", null));
        Assert.Null(_cache.TryLoad("key-2"));
    }

    [Fact]
    public void Expires_after_max_age()
    {
        var saved = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        _cache.Save("key-1", new PacxOutput("{}", null), saved);

        Assert.NotNull(_cache.TryLoad("key-1", saved + TreeCache.MaxAge - TimeSpan.FromMinutes(1)));
        Assert.Null(_cache.TryLoad("key-1", saved + TreeCache.MaxAge + TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Completer_script_is_optional()
    {
        _cache.Save("key-1", new PacxOutput("{}", null));
        var loaded = _cache.TryLoad("key-1");
        Assert.NotNull(loaded);
        Assert.Null(loaded!.CompleterScript);
    }

    [Fact]
    public void Corrupt_meta_returns_null()
    {
        _cache.Save("key-1", new PacxOutput("{}", null));
        File.WriteAllText(Path.Combine(_dir, "meta.json"), "not json");
        Assert.Null(_cache.TryLoad("key-1"));
    }

    [Fact]
    public void Key_changes_with_executable_and_is_stable_otherwise()
    {
        Directory.CreateDirectory(_dir);
        var exe = Path.Combine(_dir, OperatingSystem.IsWindows() ? "pacx.exe" : "pacx");
        File.WriteAllText(exe, "v1");
        var key1 = TreeCache.ComputeKey(_dir);
        var key1Again = TreeCache.ComputeKey(_dir);

        File.WriteAllText(exe, "v2-longer");
        File.SetLastWriteTimeUtc(exe, DateTime.UtcNow.AddMinutes(1));
        var key2 = TreeCache.ComputeKey(_dir);

        Assert.Equal(key1, key1Again);
        Assert.NotEqual(key1, key2);
        Assert.Equal("pacx:not-found", TreeCache.ComputeKey(Path.Combine(_dir, "nowhere")));
    }
}
