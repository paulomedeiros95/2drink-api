namespace TwoDrink.Domain.Errors;

/// <summary>Violação de regra de negócio. <see cref="Code"/> é snake_case (ex.: out_of_range).</summary>
public class RuleViolation(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
