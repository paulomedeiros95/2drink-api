namespace TwoDrink.Api.Tests.Infrastructure;

/// <summary>Settable UTC clock for tests. May move backwards; always UTC (Npgsql rejects other offsets).</summary>
public sealed class TestClock : TimeProvider
{
    public static readonly DateTimeOffset DefaultStart = new(2027, 2, 6, 23, 0, 0, TimeSpan.Zero); // 20:00 Sao Paulo

    private long _ticks = DefaultStart.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void SetUtcNow(DateTimeOffset value) => Interlocked.Exchange(ref _ticks, value.UtcTicks);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
