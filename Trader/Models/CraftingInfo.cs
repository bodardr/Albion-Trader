namespace Trader;

public class CraftingInfo
{
    public Item Item { get; set; }
    public CraftingRequirement Recipe { get; set; }

    public long[] CraftingCosts { get; set; }
    public long ItemPrice { get; set; }

    public long UnitProfit { get; set; }
    public long TradingVolume { get; set; }

    public long PotentialProfits { get; set; }

    public float ProfitMargin => TotalCraftingCost <= 0 ? 0 : (float)ItemPrice / TotalCraftingCost;
    public long TotalCraftingCost { get; set; }

    public CraftingInfo(Item item, CraftingRequirement recipe, long[] craftingCosts, long itemPrice,
        long tradingVolume)
    {
        Item = item;
        Recipe = recipe;
        CraftingCosts = craftingCosts;

        ItemPrice = itemPrice / 10000;
        TradingVolume = tradingVolume;

        var ingredientCount = craftingCosts.Length;
        var craftValid = true;
        for (int j = 0; j < ingredientCount; j++)
        {
            var ingredientCost = craftingCosts[j];
            if (ingredientCost <= 0)
            {
                craftValid = false;
                break;
            }
            TotalCraftingCost += ingredientCost;
        }
        TotalCraftingCost /= 10000;
        TotalCraftingCost += recipe.Silver;

        if (!craftValid)
        {
            TotalCraftingCost = 0;
            UnitProfit = 0;
            return;
        }

        //Sale Price = Sell Order price - Sales tax (2.5%) - Non-Premium Sell Order Tax (8%)
        //Material Costs = Material costs + Sales tax (2.5%)
        UnitProfit = (long)(ItemPrice * (1f - 0.025f - 0.08f) - TotalCraftingCost * 1.025f);
        PotentialProfits = UnitProfit * Math.Max(1, TradingVolume);
    }
}
