using TwoDrink.Domain.Errors;
using TwoDrink.Domain.Rules;

namespace TwoDrink.Domain.Presence;

public record GeoPoint(double Lat, double Lng);

public record DeviceFix(GeoPoint Point, double AccuracyMeters, bool IsMocked);

/// <summary>Verifica se o aparelho está perto o bastante do bar para o check-in.</summary>
public sealed class Geofence(RulesOptions o)
{
    const double EarthRadiusMeters = 6_371_000;

    public void EnsureInside(DeviceFix fix, GeoPoint venue)
    {
        if (fix.IsMocked)
            throw new RuleViolation("mock_location", "Mocked location is not allowed.");

        var accuracyMargin = Math.Min(Math.Max(fix.AccuracyMeters, 0), o.MaxAccuracyMarginMeters);
        var allowed = o.RadiusMeters + accuracyMargin;
        if (DistanceMeters(fix.Point, venue) > allowed)
            throw new RuleViolation("out_of_range", "Device is outside the venue radius.");
    }

    /// <summary>Distância em metros pela fórmula de haversine.</summary>
    public static double DistanceMeters(GeoPoint a, GeoPoint b)
    {
        static double Rad(double deg) => deg * Math.PI / 180;

        var dLat = Rad(b.Lat - a.Lat);
        var dLng = Rad(b.Lng - a.Lng);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
