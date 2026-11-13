namespace TwoDrink.Infrastructure.Data.Entities;

/// <summary>Pseudonymous account identifier. Users point to it; the ledger never points to a user.</summary>
public class LedgerAccount
{
    public Guid Id { get; set; }
}
