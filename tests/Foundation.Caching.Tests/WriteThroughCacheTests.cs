using Foundation.Caching;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Foundation.Caching.Tests;

[TestClass]
public class WriteThroughCacheTests
{
    private sealed class Person;

    private sealed class MutableScope(CacheScope initial) : ICacheScopeProvider
    {
        public CacheScope Current { get; set; } = initial;
    }

    private sealed class Harness
    {
        public required MutableScope Scope { get; init; }
        public required WriteThroughCacheState<Person> State { get; init; }
        public required WriteThroughCacheOptions Options { get; init; }
        public required HybridCache Hybrid { get; init; }

        public WriteThroughCache<Person> Cache() => new(Hybrid, Scope, State, Options);
    }

    private static Harness NewHarness(int tenant = 1, int? subScope = 10, Action<WriteThroughCacheOptions>? configure = null)
    {
        ServiceCollection services = new();
        services.AddFoundationCaching(new ConfigurationBuilder().Build());
        HybridCache hybrid = services.BuildServiceProvider().GetRequiredService<HybridCache>();

        WriteThroughCacheOptions options = new();
        configure?.Invoke(options);
        return new Harness
        {
            Scope = new MutableScope(new CacheScope(tenant, subScope)),
            State = new WriteThroughCacheState<Person>(),
            Options = options,
            Hybrid = hybrid
        };
    }

    private sealed class Counter
    {
        public int Calls;
        public ValueTask<int> Next(CancellationToken _) => ValueTask.FromResult(++Calls);
    }

    [TestMethod]
    public async Task SecondRead_IsServedFromCache()
    {
        WriteThroughCache<Person> cache = NewHarness().Cache();
        Counter counter = new();

        int first = await cache.GetOrCreateAsync("p1", counter.Next);
        int second = await cache.GetOrCreateAsync("p1", counter.Next);

        Assert.AreEqual(1, first);
        Assert.AreEqual(1, second);
        Assert.AreEqual(1, counter.Calls);
    }

    [TestMethod]
    public async Task Write_EvictsSoNextReadIsFresh()
    {
        WriteThroughCache<Person> cache = NewHarness().Cache();
        Counter counter = new();
        await cache.GetOrCreateAsync("p1", counter.Next);

        await cache.WriteAsync(_ => Task.CompletedTask);
        int afterWrite = await cache.GetOrCreateAsync("p1", counter.Next);

        Assert.AreEqual(2, afterWrite);
    }

    [TestMethod]
    public async Task Write_ReturnsResult_AndEvicts()
    {
        WriteThroughCache<Person> cache = NewHarness().Cache();
        Counter counter = new();
        await cache.GetOrCreateAsync("p1", counter.Next);

        string result = await cache.WriteAsync(_ => Task.FromResult("saved"));

        Assert.AreEqual("saved", result);
        Assert.AreEqual(2, await cache.GetOrCreateAsync("p1", counter.Next));
    }

    [TestMethod]
    public async Task FailedWrite_StillEvictsAndPropagates()
    {
        WriteThroughCache<Person> cache = NewHarness().Cache();
        Counter counter = new();
        await cache.GetOrCreateAsync("p1", counter.Next);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => cache.WriteAsync(_ => throw new InvalidOperationException("db down")));

        Assert.AreEqual(2, await cache.GetOrCreateAsync("p1", counter.Next));
    }

    [TestMethod]
    public async Task Write_EvictsEvenWhenCallerTokenIsCancelled()
    {
        WriteThroughCache<Person> cache = NewHarness().Cache();
        Counter counter = new();
        await cache.GetOrCreateAsync("p1", counter.Next);
        using CancellationTokenSource cts = new();

        await cache.WriteAsync(_ => { cts.Cancel(); return Task.CompletedTask; }, cts.Token);

        Assert.AreEqual(2, await cache.GetOrCreateAsync("p1", counter.Next));
    }

    [TestMethod]
    public async Task Tenants_NeverShareEntries_AndOneTenantsWriteDoesNotEvictAnother()
    {
        Harness h = NewHarness(tenant: 1);
        Counter counter = new();
        await h.Cache().GetOrCreateAsync("p1", counter.Next); // tenant 1 -> 1

        h.Scope.Current = new CacheScope(2, 10);
        int tenant2 = await h.Cache().GetOrCreateAsync("p1", counter.Next); // must miss
        Assert.AreEqual(2, tenant2);

        await h.Cache().WriteAsync(_ => Task.CompletedTask); // tenant 2 write

        h.Scope.Current = new CacheScope(1, 10);
        Assert.AreEqual(1, await h.Cache().GetOrCreateAsync("p1", counter.Next)); // tenant 1 still cached
    }

    [TestMethod]
    public async Task SubScopes_AreIsolatedWithinATenant()
    {
        Harness h = NewHarness(tenant: 1, subScope: 10);
        Counter counter = new();
        await h.Cache().GetOrCreateAsync("p1", counter.Next);

        h.Scope.Current = new CacheScope(1, 11);
        Assert.AreEqual(2, await h.Cache().GetOrCreateAsync("p1", counter.Next));

        h.Scope.Current = new CacheScope(1, null);
        Assert.AreEqual(3, await h.Cache().GetOrCreateAsync("p1", counter.Next));
    }

    [TestMethod]
    public async Task UnresolvedTenant_BypassesCacheBothWays()
    {
        WriteThroughCache<Person> cache = NewHarness(tenant: 0).Cache();
        Counter counter = new();

        await cache.GetOrCreateAsync("p1", counter.Next);
        await cache.GetOrCreateAsync("p1", counter.Next);
        await cache.WriteAsync(_ => Task.CompletedTask);

        Assert.AreEqual(2, counter.Calls);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Jane Doe")]
    [DataRow("a/b")]
    [DataRow("abc\n")]
    public async Task KeyThatIsNotAPlainIdentifier_IsRejected(string key)
    {
        WriteThroughCache<Person> cache = NewHarness().Cache();
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await cache.GetOrCreateAsync(key, new Counter().Next));
    }

    [TestMethod]
    public async Task OverlongKey_IsRejected()
    {
        WriteThroughCache<Person> cache = NewHarness().Cache();
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await cache.GetOrCreateAsync(new string('a', 129), new Counter().Next));
    }

    [TestMethod]
    public async Task PerScopeBound_EvictsOldestEntry()
    {
        Harness h = NewHarness(configure: o => o.MaxEntriesPerScope = 2);
        WriteThroughCache<Person> cache = h.Cache();
        Counter counter = new();

        await cache.GetOrCreateAsync("a", counter.Next);
        await cache.GetOrCreateAsync("b", counter.Next);
        await cache.GetOrCreateAsync("c", counter.Next); // evicts "a"
        int calls = counter.Calls;

        await cache.GetOrCreateAsync("b", counter.Next); // still cached
        Assert.AreEqual(calls, counter.Calls);
        await cache.GetOrCreateAsync("a", counter.Next); // evicted, reloaded
        Assert.AreEqual(calls + 1, counter.Calls);
    }

    [TestMethod]
    public async Task ScopeBound_IsPerScope_NotGlobal()
    {
        Harness h = NewHarness(tenant: 1, configure: o => o.MaxEntriesPerScope = 1);
        Counter counter = new();
        await h.Cache().GetOrCreateAsync("a", counter.Next);

        h.Scope.Current = new CacheScope(2, 10);
        await h.Cache().GetOrCreateAsync("a", counter.Next);

        h.Scope.Current = new CacheScope(1, 10);
        int calls = counter.Calls;
        await h.Cache().GetOrCreateAsync("a", counter.Next); // tenant 1's entry survived tenant 2's activity
        Assert.AreEqual(calls, counter.Calls);
    }

    [TestMethod]
    public async Task Write_ResetsScopeTracking()
    {
        Harness h = NewHarness(configure: o => o.MaxEntriesPerScope = 5);
        WriteThroughCache<Person> cache = h.Cache();
        await cache.GetOrCreateAsync("a", new Counter().Next);
        await cache.GetOrCreateAsync("b", new Counter().Next);
        string tag = $"{typeof(Person).FullName}:t1:c10";
        Assert.AreEqual(2, h.State.Count(tag));

        await cache.WriteAsync(_ => Task.CompletedTask);

        Assert.AreEqual(0, h.State.Count(tag));
    }

    private sealed class StepClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    [TestMethod]
    public void TrackedScopes_AreBoundedGlobally_LeastRecentlyUsedForgottenFirst()
    {
        StepClock clock = new(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero));
        WriteThroughCacheState<Person> state = new(clock);
        TimeSpan ttl = TimeSpan.FromMinutes(30);

        for (int tenant = 1; tenant <= 5; tenant++)
        {
            state.Track($"t{tenant}", $"t{tenant}:k", 10, ttl, maxScopes: 3);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.AreEqual(3, state.ScopeCount);
        Assert.AreEqual(0, state.Count("t1"));
        Assert.AreEqual(1, state.Count("t5"));
    }

    [TestMethod]
    public void IdleScopes_AreForgottenOnceTheirEntriesHaveExpired()
    {
        StepClock clock = new(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero));
        WriteThroughCacheState<Person> state = new(clock);
        TimeSpan ttl = TimeSpan.FromMinutes(5);
        state.Track("old", "old:k", 10, ttl, maxScopes: 2);
        state.Track("recent", "recent:k", 10, ttl, maxScopes: 2);
        clock.Advance(TimeSpan.FromMinutes(10));
        state.Track("recent", "recent:k2", 10, ttl, maxScopes: 2); // touches "recent" so it is not idle

        state.Track("new", "new:k", 10, ttl, maxScopes: 2);

        Assert.AreEqual(0, state.Count("old"));
        Assert.AreEqual(2, state.Count("recent"));
        Assert.AreEqual(1, state.Count("new"));
    }

    [TestMethod]
    public async Task ForgettingALiveScope_AlsoEvictsItsCachedEntries()
    {
        Harness h = NewHarness(tenant: 1, configure: o => { o.MaxTrackedScopes = 1; o.MaxEntriesPerScope = 1; });
        Counter counter = new();

        await h.Cache().GetOrCreateAsync("a", counter.Next); // tenant 1 / a
        h.Scope.Current = new CacheScope(2, 10);
        await h.Cache().GetOrCreateAsync("a", counter.Next); // tenant 2 pushes tenant 1 out of the tracker
        h.Scope.Current = new CacheScope(1, 10);
        await h.Cache().GetOrCreateAsync("b", counter.Next); // tenant 1 returns with a second key

        int calls = counter.Calls;
        await h.Cache().GetOrCreateAsync("a", counter.Next); // tenant 1 / a must have been evicted with its scope

        Assert.AreEqual(calls + 1, counter.Calls);
    }

    [TestMethod]
    public void ByDefault_AnAggregateIsTreatedAsClassified_AndLocalTtlIsShort()
    {
        WriteThroughCacheOptions defaults = new() { Ttl = TimeSpan.FromHours(1) };

        Assert.IsTrue(defaults.ContainsClassifiedData);
        Assert.AreEqual(TimeSpan.FromMinutes(5), defaults.EffectiveTtl);
        Assert.AreEqual(TimeSpan.FromMinutes(1), defaults.EffectiveLocalTtl);
        Assert.AreEqual(TimeSpan.FromSeconds(30), new WriteThroughCacheOptions { Ttl = TimeSpan.FromSeconds(30) }.EffectiveLocalTtl);
    }

    [TestMethod]
    public async Task AWriteInOneSubScope_EvictsTheTenantsEntriesInOtherSubScopes()
    {
        Harness h = NewHarness(tenant: 1, subScope: 10);
        Counter counter = new();
        await h.Cache().GetOrCreateAsync("p1", counter.Next); // cached under sub-scope 10
        h.Scope.Current = new CacheScope(1, 11);
        await h.Cache().GetOrCreateAsync("p1", counter.Next); // cached under sub-scope 11

        h.Scope.Current = new CacheScope(1, 11);
        await h.Cache().WriteAsync(_ => Task.CompletedTask); // the write happens in sub-scope 11

        h.Scope.Current = new CacheScope(1, 10);
        int calls = counter.Calls;
        await h.Cache().GetOrCreateAsync("p1", counter.Next);
        Assert.AreEqual(calls + 1, counter.Calls); // sub-scope 10's copy was evicted as well
    }

    [TestMethod]
    public async Task Write_EvictsBeforeTheWriteRuns_AsWellAsAfter()
    {
        WriteThroughCache<Person> cache = NewHarness().Cache();
        Counter counter = new();
        await cache.GetOrCreateAsync("p1", counter.Next);
        int readDuringWrite = 0;

        await cache.WriteAsync(async ct =>
        {
            readDuringWrite = await cache.GetOrCreateAsync("p1", counter.Next); // already evicted, so this reloads
        });

        Assert.AreEqual(2, readDuringWrite);
        Assert.AreEqual(3, await cache.GetOrCreateAsync("p1", counter.Next)); // and the post-write eviction dropped it again
    }

    private sealed class ThrowingEvictionCache(HybridCache inner) : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            inner.GetOrCreateAsync(key, state, factory, options, tags, cancellationToken);

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            inner.SetAsync(key, value, options, tags, cancellationToken);

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            inner.RemoveAsync(key, cancellationToken);

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
            throw new IOException("redis down");
    }

    [TestMethod]
    public async Task EvictionFailure_NeverReplacesTheWritesResult_AndIsReported()
    {
        Harness h = NewHarness();
        List<Exception> reported = [];
        h.Options.OnEvictionFailed = reported.Add;
        WriteThroughCache<Person> cache = new(new ThrowingEvictionCache(h.Hybrid), h.Scope, h.State, h.Options);

        string result = await cache.WriteAsync(_ => Task.FromResult("committed"));

        Assert.AreEqual("committed", result);
        Assert.IsTrue(reported.Count >= 1);
        Assert.IsInstanceOfType<IOException>(reported[0]);
    }

    [TestMethod]
    public async Task EvictionFailure_DoesNotMaskTheWritesOwnException_EvenWithAThrowingHook()
    {
        Harness h = NewHarness();
        h.Options.OnEvictionFailed = _ => throw new InvalidOperationException("hook failed");
        WriteThroughCache<Person> cache = new(new ThrowingEvictionCache(h.Hybrid), h.Scope, h.State, h.Options);

        await Assert.ThrowsExactlyAsync<FormatException>(() => cache.WriteAsync(_ => throw new FormatException("write failed")));
    }

    private sealed class ThrowingRemoveCache(HybridCache inner) : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            inner.GetOrCreateAsync(key, state, factory, options, tags, cancellationToken);

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            inner.SetAsync(key, value, options, tags, cancellationToken);

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            throw new IOException("redis down");

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
            inner.RemoveByTagAsync(tag, cancellationToken);
    }

    private sealed class CountingCache(HybridCache inner) : HybridCache
    {
        public int Removes { get; private set; }
        public int TagRemoves { get; private set; }

        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            inner.GetOrCreateAsync(key, state, factory, options, tags, cancellationToken);

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            inner.SetAsync(key, value, options, tags, cancellationToken);

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Removes++;
            return inner.RemoveAsync(key, cancellationToken);
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        {
            TagRemoves++;
            return inner.RemoveByTagAsync(tag, cancellationToken);
        }
    }

    [TestMethod]
    public async Task ForgettingALiveScope_EvictsItByOneTagCall_NotOneCallPerEntry()
    {
        Harness h = NewHarness(configure: o => o.MaxTrackedScopes = 1);
        CountingCache counting = new(h.Hybrid);
        WriteThroughCache<Person> cache = new(counting, h.Scope, h.State, h.Options);

        for (int i = 0; i < 5; i++)
            await cache.GetOrCreateAsync($"k{i}", _ => ValueTask.FromResult(i));

        h.Scope.Current = new CacheScope(1, 99); // a new scope at capacity forgets the first, which is still live
        await cache.GetOrCreateAsync("other", _ => ValueTask.FromResult(0));

        Assert.AreEqual(0, counting.Removes);
        Assert.AreEqual(1, counting.TagRemoves);
    }

    [TestMethod]
    public async Task ReadPathEvictionFailure_DoesNotFailTheRead_AndIsReported()
    {
        Harness h = NewHarness(configure: o => o.MaxEntriesPerScope = 1);
        List<Exception> reported = [];
        h.Options.OnEvictionFailed = reported.Add;
        WriteThroughCache<Person> cache = new(new ThrowingRemoveCache(h.Hybrid), h.Scope, h.State, h.Options);

        await cache.GetOrCreateAsync("p1", _ => ValueTask.FromResult(1));
        int second = await cache.GetOrCreateAsync("p2", _ => ValueTask.FromResult(2)); // evicts p1 past the bound

        Assert.AreEqual(2, second);
        Assert.AreEqual(1, reported.Count);
        Assert.IsInstanceOfType<IOException>(reported[0]);
    }

    [TestMethod]
    public void Constructor_RejectsBoundsThatWouldBreakTheTracker()
    {
        Harness h = NewHarness();
        foreach (Action<WriteThroughCacheOptions> bad in new Action<WriteThroughCacheOptions>[]
        {
            o => o.MaxEntriesPerScope = 0, o => o.MaxEntriesPerScope = -1, o => o.MaxTrackedScopes = 0,
            o => o.Ttl = TimeSpan.Zero, o => o.MaxLocalCacheTtl = TimeSpan.Zero
        })
        {
            WriteThroughCacheOptions options = new();
            bad(options);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new WriteThroughCache<Person>(h.Hybrid, h.Scope, h.State, options));
        }
    }

    [TestMethod]
    public void Constructor_RejectsNullDependencies()
    {
        Harness h = NewHarness();
        Assert.ThrowsExactly<ArgumentNullException>(() => new WriteThroughCache<Person>(null!, h.Scope, h.State, h.Options));
        Assert.ThrowsExactly<ArgumentNullException>(() => new WriteThroughCache<Person>(h.Hybrid, null!, h.State, h.Options));
        Assert.ThrowsExactly<ArgumentNullException>(() => new WriteThroughCache<Person>(h.Hybrid, h.Scope, null!, h.Options));
        Assert.ThrowsExactly<ArgumentNullException>(() => new WriteThroughCache<Person>(h.Hybrid, h.Scope, h.State, null!));
    }

    [TestMethod]
    public void ClassifiedData_TtlIsCappedAtFiveMinutes()
    {
        WriteThroughCacheOptions classified = new() { Ttl = TimeSpan.FromHours(1), ContainsClassifiedData = true };
        WriteThroughCacheOptions shortTtl = new() { Ttl = TimeSpan.FromMinutes(2), ContainsClassifiedData = true };
        WriteThroughCacheOptions plain = new() { Ttl = TimeSpan.FromHours(1), ContainsClassifiedData = false };

        Assert.AreEqual(TimeSpan.FromMinutes(5), classified.EffectiveTtl);
        Assert.AreEqual(TimeSpan.FromMinutes(2), shortTtl.EffectiveTtl);
        Assert.AreEqual(TimeSpan.FromHours(1), plain.EffectiveTtl);
    }

    [TestMethod]
    public async Task NullFactoryOrWrite_Throws()
    {
        WriteThroughCache<Person> cache = NewHarness().Cache();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () => await cache.GetOrCreateAsync<int>("k", null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => cache.WriteAsync((Func<CancellationToken, Task>)null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => cache.WriteAsync<int>(null!));
    }

    [TestMethod]
    public async Task AddFoundationWriteThroughCache_ResolvesAndWorks()
    {
        ServiceCollection services = new();
        services.AddFoundationCaching(new ConfigurationBuilder().Build());
        services.AddSingleton<ICacheScopeProvider>(new MutableScope(new CacheScope(3, 4)));
        services.AddFoundationWriteThroughCache<Person>(o => { o.ContainsClassifiedData = true; o.MaxEntriesPerScope = 10; });
        await using ServiceProvider sp = services.BuildServiceProvider();
        await using AsyncServiceScope scope = sp.CreateAsyncScope();

        WriteThroughCache<Person> cache = scope.ServiceProvider.GetRequiredService<WriteThroughCache<Person>>();
        Counter counter = new();
        await cache.GetOrCreateAsync("p1", counter.Next);
        await cache.GetOrCreateAsync("p1", counter.Next);

        Assert.AreEqual(1, counter.Calls);
    }

    [TestMethod]
    public void AddFoundationWriteThroughCache_RejectsNonsensicalOptions()
    {
        ServiceCollection services = new();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => services.AddFoundationWriteThroughCache<Person>(o => o.MaxEntriesPerScope = 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => services.AddFoundationWriteThroughCache<Person>(o => o.MaxEntriesPerScope = -5));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => services.AddFoundationWriteThroughCache<Person>(o => o.MaxTrackedScopes = 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => services.AddFoundationWriteThroughCache<Person>(o => o.Ttl = TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => services.AddFoundationWriteThroughCache<Person>(o => o.MaxLocalCacheTtl = TimeSpan.FromSeconds(-1)));
    }
}
