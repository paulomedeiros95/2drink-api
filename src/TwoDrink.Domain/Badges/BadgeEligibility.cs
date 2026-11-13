using TwoDrink.Domain.Scoring;

namespace TwoDrink.Domain.Badges;

public static class BadgeEligibility
{
    /// <summary>
    /// Na mesma temporada, o badge de um bar só é concedido na primeira visita
    /// ou quando o tier atual é estritamente maior que o melhor badge já obtido
    /// para esse bar. Queda ou renovação no mesmo tier (ou abaixo) não concede nada.
    /// </summary>
    public static bool ShouldGrant(Tier currentTier, Tier? highestHeldThisSeason) =>
        highestHeldThisSeason is null || currentTier > highestHeldThisSeason;
}
