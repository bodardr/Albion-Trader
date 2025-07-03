namespace Trader;

public static class ProfitUtility
{
    public static long Calculate(long sellPrice, params long[] buyPrices)
    {
        var profit = MathF.Floor(sellPrice * (1 - 0.025f - 0.08f));

        foreach (var buyPrice in buyPrices)
            profit -= buyPrice;

        return (long)profit;
    }
}
