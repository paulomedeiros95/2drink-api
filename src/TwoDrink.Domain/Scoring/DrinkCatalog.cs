using TwoDrink.Domain.Rules;

namespace TwoDrink.Domain.Scoring;

public sealed class DrinkCatalog(RulesOptions o)
{
    /// <summary>Categoria fixa de cada tipo de bebida (não configurável).</summary>
    public static DrinkCategory Category(DrinkType t) => t switch
    {
        DrinkType.Beer or DrinkType.Drink or DrinkType.Gin or DrinkType.Shot or DrinkType.Wine
            => DrinkCategory.Alcoholic,
        DrinkType.Water or DrinkType.Juice or DrinkType.Soda or DrinkType.Whey
            => DrinkCategory.NonAlcoholic,
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "Tipo de bebida desconhecido."),
    };

    public DrinkCategory CategoryOf(DrinkType t) => Category(t);

    public decimal PointsFor(DrinkLine l) =>
        l.Quantity * (CategoryOf(l.Type) == DrinkCategory.Alcoholic
            ? o.AlcoholicPoints
            : o.NonAlcoholicPoints);
}
