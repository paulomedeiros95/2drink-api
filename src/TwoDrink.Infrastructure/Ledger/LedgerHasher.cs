using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TwoDrink.Infrastructure.Data.Entities;

namespace TwoDrink.Infrastructure.Ledger;

public static class LedgerHasher
{
    public const string Genesis = "GENESIS";

    /// <summary>Postgres timestamptz keeps microseconds; hash only what survives a round-trip.</summary>
    public static DateTimeOffset TruncateToMicroseconds(DateTimeOffset t) =>
        new(t.UtcTicks - t.UtcTicks % 10, TimeSpan.Zero);

    public static string Compute(LedgerEntry e)
    {
        string[] fields =
        [
            e.AccountId.ToString("D"),
            e.Sequence.ToString(CultureInfo.InvariantCulture),
            e.Type.ToString(),
            e.VenueId?.ToString("D") ?? "",
            e.CheckInId?.ToString("D") ?? "",
            e.DrinkType?.ToString() ?? "",
            e.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "",
            e.Tier?.ToString() ?? "",
            e.Points.ToString("F1", CultureInfo.InvariantCulture),
            e.ReversesEntryId?.ToString(CultureInfo.InvariantCulture) ?? "",
            e.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            e.PrevHash,
        ];
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', fields))));
    }
}
