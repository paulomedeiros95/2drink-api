namespace TwoDrink.Domain.Scoring;

public enum DrinkType { Beer, Drink, Gin, Shot, Wine, Water, Juice, Soda, Whey }

public enum DrinkCategory { Alcoholic, NonAlcoholic }

public record DrinkLine(DrinkType Type, int Quantity);
