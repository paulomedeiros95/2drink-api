namespace TwoDrink.Domain.Rules;

/// <summary>Fuso de negócio (America/Sao_Paulo) e conversão de datas locais para intervalos UTC.</summary>
public static class BusinessTime
{
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <summary>Intervalo [início 00:00, fim+1 00:00) no fuso de negócio, em UTC.</summary>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtcExclusive) Range(DateOnly start, DateOnly end) =>
        (LocalMidnightUtc(start), LocalMidnightUtc(end.AddDays(1)));

    /// <summary>Data local de negócio correspondente a <paramref name="now"/>.</summary>
    public static DateOnly Today(DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Zone).DateTime);

    static DateTimeOffset LocalMidnightUtc(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
