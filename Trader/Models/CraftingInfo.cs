namespace Trader;

public class CraftingInfo
{
    public Item Item { get; set; }
    public CraftingRequirement Recipe { get; set; }

    public long[,] CraftingCosts { get; set; }
    public long[] ItemPrices { get; set; }

    public long[] UnitProfits { get; set; }
    public long[] TradingVolumes { get; set; }
    
    public long[] PotentialProfits { get; set; }

    public CraftingInfo(Item item, CraftingRequirement recipe, long[,] craftingCosts, long[] itemPrices,
        long[] tradingVolumes)
    {
        Item = item;
        Recipe = recipe;
        CraftingCosts = craftingCosts;
        
        ItemPrices = itemPrices;
        TradingVolumes = tradingVolumes;

        UnitProfits = new long[ItemPrices.Length];

        var ingredientCount = craftingCosts.GetLength(1);
        for (int i = 0; i < ItemPrices.Length; i++)
        {
            long totalCraftingCost = recipe.Silver;
            var craftValid = true;
            for (int j = 0; j < ingredientCount; j++)
            {
                var ingredientCost = craftingCosts[i, j];
                if (ingredientCost <= 0)
                {
                    craftValid = false;
                    break;
                }
                totalCraftingCost += ingredientCost;
            }

            if (!craftValid)
            {
                UnitProfits[i] = 0;
                continue;
            }
            
            //Sale Price = Sell Order price - Sales tax (2.5%) - Non-Premium Sell Order Tax (8%)
            //Material Costs = Material costs + Sales tax (2.5%)
            UnitProfits[i] = (long)(ItemPrices[i] * (1f - 0.025f - 0.08f) - totalCraftingCost * 1.025f);
        }
        
        PotentialProfits = new long[ItemPrices.Length];
        for (int i = 0; i < ItemPrices.Length; i++)
            PotentialProfits[i] = UnitProfits[i] * TradingVolumes[i];
    }
}
