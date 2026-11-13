namespace TwoDrink.Domain.Scoring;

public static class HydrationGoal
{
    /// <summary>
    /// Meta: águas ≥ max(1, ⌈alcoólicas/2⌉). Só <see cref="DrinkType.Water"/> conta como água.
    /// </summary>
    public static bool IsMet(IEnumerable<DrinkLine> lines)
    {
        var waters = 0;
        var alcoholic = 0;
        foreach (var l in lines)
        {
            if (l.Type == DrinkType.Water) waters += l.Quantity;
            if (DrinkCatalog.Category(l.Type) == DrinkCategory.Alcoholic) alcoholic += l.Quantity;
        }
        var required = Math.Max(1, (alcoholic + 1) / 2);
        return waters >= required;
    }
}
