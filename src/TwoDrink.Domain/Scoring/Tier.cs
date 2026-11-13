namespace TwoDrink.Domain.Scoring;

/// <summary>Ordem importa: maior = melhor.</summary>
public enum Tier { Default = 0, Bronze = 1, Prata = 2, Ouro = 3, Diamante = 4, Epic = 5 }

public static class TierPoints
{
    public static decimal Of(Tier t) => t switch
    {
        Tier.Default => 1m,
        Tier.Bronze => 3m,
        Tier.Prata => 5m,
        Tier.Ouro => 10m,
        Tier.Diamante => 25m,
        Tier.Epic => 50m,
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "Tier desconhecido."),
    };
}
