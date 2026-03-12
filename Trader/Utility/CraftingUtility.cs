using System.Text;
namespace Trader;

public static class CraftingUtility
{
    public static async Task GetCraftingFlips(MarketLocation location, long budget, float volumePercentage)
    {
        HashSet<string> itemsToFetch = new();

        var crafts = new List<(Item, CraftingRequirement)>();

        foreach (var item in ItemDictionary.Items.Values)
        {
            itemsToFetch.Add(item.UniqueName);
            if (item.CraftingRecipes != null)
                foreach (var recipe in item.CraftingRecipes)
                {
                    var craftValid = true;

                    foreach (var craftResource in recipe.CraftResources)
                    {
                        if (craftResource != null && !string.IsNullOrEmpty(craftResource.UniqueName))
                            itemsToFetch.Add(craftResource.UniqueName);
                        else
                            craftValid = false;
                    }

                    if (craftValid)
                        crafts.Add(new(item, recipe));
                }
        }

        var locationCode = ((int)location).ToString();

        var query = new TraderQuery().OfItems(itemsToFetch.ToArray()).OfLocations(locationCode);
        var prices = await query.GetPrices(Database.Instance.DB);

        var craftInfos = new List<CraftingInfo>();

        foreach (var (item, craft) in crafts)
        {
            long[] craftingCosts = new long[craft.CraftResources.Length];

            for (int ingredientIndex = 0; ingredientIndex < craft.CraftResources.Length; ingredientIndex++)
            {
                var ingredient = craft.CraftResources[ingredientIndex];
                if (prices.ContainsKey(ingredient.UniqueName) &&
                    prices[ingredient.UniqueName].TryGetValue(locationCode, out var ingredientPrice))
                    craftingCosts[ingredientIndex] = ingredientPrice.UnitPriceSilver * ingredient.Count;
            }

            if (!prices.ContainsKey(item.UniqueName) ||
                !prices[item.UniqueName].TryGetValue(locationCode, out var itemPrice))
                continue;

            long itemVolume = itemPrice.VolumeSold;

            var craftInfo = new CraftingInfo(item, craft, craftingCosts, itemPrice.UnitPriceSilver, itemVolume);

            var existingCraftIndex = craftInfos.FindIndex(x => x.Item.UniqueName.Equals(craftInfo.Item.UniqueName));
            
            //If there is an existing craft and it gives less profit, we replace it 
            if (existingCraftIndex >= 0 && craftInfos[existingCraftIndex].UnitProfit < craftInfo.UnitProfit)
                craftInfos[existingCraftIndex] = craftInfo;
            else if (existingCraftIndex < 0)
                craftInfos.Add(craftInfo);
        }

        //We remove all artifact rolls because they're random.
        craftInfos.RemoveAll(x =>
            x.Item.UniqueName.Contains("ARTIFACT") ||
            x.Item.UniqueName.Contains("ARTEFACT") ||
            x.Item.UniqueName.Contains("CAPEITEM"));

        craftInfos.Sort((x, y) => y.ProfitMargin.CompareTo(x.ProfitMargin));

        StringBuilder volumeInfoStr = new();
        volumeInfoStr.AppendLine("Volume information is required on the following items : ");
        for (int i = 0; i < 30; i++)
        {
            var craftInfo = craftInfos[i];

            if (craftInfo.TradingVolume > 0)
                continue;

            volumeInfoStr.AppendLine(craftInfo.Item.DisplayName);
        }

        var volumeInformationRequired = volumeInfoStr.ToString();

        var craftsToMakeList = new StringBuilder();
        craftsToMakeList.AppendLine("Crafts To Make:");
        Dictionary<string, long> requiredIngredients = new();
        for (int i = 0; i < 30; i++)
        {
            var craftInfo = craftInfos[i];

            if (craftInfo.TradingVolume <= 0)
                continue;

            var volume = (long)Math.Ceiling(craftInfo.TradingVolume * volumePercentage);

            foreach (var resource in craftInfo.Recipe.CraftResources)
            {
                var itemName = ItemDictionary.IdToName[resource.UniqueName];
                if (!requiredIngredients.TryGetValue(itemName, out var ingredientAmount))
                    requiredIngredients.Add(itemName, resource.Count * volume);
                else
                    requiredIngredients[itemName] = ingredientAmount + resource.Count * volume;
            }

            craftsToMakeList.AppendLine($"- {volume}x {craftInfo.Item.DisplayName}");
        }

        var ingredientsRequired = new StringBuilder("Ingredients Required");

        foreach (var (itemName, amount) in requiredIngredients)
            ingredientsRequired.AppendLine($"- {amount}x {itemName}");

        var ingredientsRequiredStr = ingredientsRequired.ToString() + craftsToMakeList.ToString();
    }
}
