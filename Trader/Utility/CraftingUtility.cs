namespace Trader;

public static class CraftingUtility
{
    private static Dictionary<Item, List<CraftingRequirement>> GetValidCrafts(out HashSet<string> itemsToFetch)
    {
        var crafts = new Dictionary<Item, List<CraftingRequirement>>();
        var iToFetch = new HashSet<string>();

        foreach (var item in ItemDictionary.Items.Values)
        {
            AddCraft(item);
        }

        itemsToFetch = iToFetch;
        return crafts;

        void AddCraft(Item item)
        {
            iToFetch.Add(item.UniqueName);
            if (item.CraftingRecipes != null)
                foreach (var recipe in item.CraftingRecipes)
                {
                    var craftValid = true;

                    foreach (var craftResource in recipe.CraftResources)
                    {
                        if (craftResource != null && !string.IsNullOrEmpty(craftResource.UniqueName))
                            iToFetch.Add(craftResource.UniqueName);
                        else
                            craftValid = false;
                    }

                    if (!craftValid)
                        continue;

                    if (!crafts.ContainsKey(item))
                        crafts.Add(item, [recipe]);
                    else
                        crafts[item].Add(recipe);
                }
        }
    }

    public static async Task<List<CraftingInfo>> GetCraftingFlips(MarketLocation origin, MarketLocation sellLocation,
        long budget,
        float volumePercentage)
    {
        var crafts = GetValidCrafts(out var itemsToFetch);

        var originCode = ((int)origin).ToString("D4");
        var sellLocationCode = ((int)sellLocation).ToString("D4");

        var query = new TraderQuery().OfItems(itemsToFetch.ToArray()).OfLocations(originCode, sellLocationCode).OfQualities(1..3);
        var prices = await query.GetPrices(Database.Instance.DB);

        var craftInfos = new List<CraftingInfo>();

        foreach (var (itemId, pricesPerLocation) in prices)
        {
            var id = itemId;
            if (id[^2] == '@')
                id = id[..^2];
            
            var item = ItemDictionary.Items.GetValueOrDefault(id);

            if (item == null)
                continue;

            if (!crafts.TryGetValue(item, out var craftsForItem))
                continue;

            if (item.UniqueName.Contains("ARTIFACT") ||
                item.UniqueName.Contains("ARTEFACT") ||
                item.UniqueName.Contains("CAPEITEM"))
                continue;

            if (!pricesPerLocation.TryGetValue(sellLocationCode, out var itemPrice))
                continue;

            var enchantmentLevel = itemPrice.EnchantmentLevel;
            foreach (var craft in craftsForItem)
            {
                var craftValid = true;
                long[] craftingCosts = new long[craft.CraftResources.Length];

                for (int ingredientIndex = 0; ingredientIndex < craft.CraftResources.Length; ingredientIndex++)
                {
                    var ingredient = craft.CraftResources[ingredientIndex];

                    var ingredientId = ingredient.UniqueName;
                    if (enchantmentLevel > 0)
                    {
                        if (ItemDictionary.IdToName.ContainsKey($"{ingredientId}@{enchantmentLevel}"))
                            ingredientId = $"{ingredientId}@{enchantmentLevel}";
                        else if(ItemDictionary.IdToName.ContainsKey($"{ingredientId}_LEVEL{enchantmentLevel}@{enchantmentLevel}"))
                            ingredientId = $"{ingredientId}_LEVEL{enchantmentLevel}@{enchantmentLevel}";
                    }

                    if (prices.TryGetValue(ingredientId, out var ingredientPricesPerLocation) &&
                        ingredientPricesPerLocation.TryGetValue(originCode, out var ingredientPrice))
                    {
                        craftingCosts[ingredientIndex] = ingredientPrice.UnitPriceSilver * ingredient.Count;
                    }
                    else
                    {
                        craftValid = false;
                        break;
                    }
                }

                if (!craftValid)
                    continue;

                long itemVolume = itemPrice.VolumeSold;

                var craftInfo = new CraftingInfo(item, enchantmentLevel, craft, craftingCosts,
                    itemPrice.UnitPriceSilver, itemVolume);

                var existingCraftIndex =
                    craftInfos.FindIndex(x =>
                        x.Item.UniqueName.Equals(craftInfo.Item.UniqueName) &&
                        x.EnchantmentLevel == craftInfo.EnchantmentLevel);

                //If there is an existing craft that gives more profit, we replace it 
                if (existingCraftIndex >= 0 && craftInfos[existingCraftIndex].UnitProfit < craftInfo.UnitProfit)
                    craftInfos[existingCraftIndex] = craftInfo;
                else if (existingCraftIndex < 0)
                    craftInfos.Add(craftInfo);
            }
        }

        craftInfos.Sort((x, y) => y.ProfitMargin.CompareTo(x.ProfitMargin));
        return craftInfos;
    }

    public static async Task<List<SalvageInfo>> GetSalvageFlips(MarketLocation location)
    {
        var crafts = GetValidCrafts(out var itemsToFetch);
        var locationCode = ((int)location).ToString("D4");

        var query = new TraderQuery().OfItems(itemsToFetch.ToArray()).OfLocations(locationCode).OfQualities(1..3);
        var prices = await query.GetPrices(Database.Instance.DB);

        var salvageInfos = new List<SalvageInfo>();
        foreach (var (itemId, pricesPerLocation) in prices)
        {
            var id = itemId;
            if (id[^2] == '@')
                id = id[..^2];
            
            var item = ItemDictionary.Items.GetValueOrDefault(id);

            if (!item.Salvageable)
                continue;
            
            if (item == null)
                continue;

            if (!crafts.TryGetValue(item, out var craftsForItem))
                continue;

            if (item.UniqueName.Contains("ARTIFACT") ||
                item.UniqueName.Contains("ARTEFACT") ||
                item.UniqueName.Contains("CAPEITEM"))
                continue;

            if (!pricesPerLocation.TryGetValue(locationCode, out var itemPrice))
                continue;

            var enchantmentLevel = itemPrice.EnchantmentLevel;
            foreach (var craft in craftsForItem)
            {
                var craftValid = true;
                long[] craftingCosts = new long[craft.CraftResources.Length];

                for (int ingredientIndex = 0; ingredientIndex < craft.CraftResources.Length; ingredientIndex++)
                {
                    var ingredient = craft.CraftResources[ingredientIndex];

                    var ingredientId = ingredient.UniqueName;
                    if (enchantmentLevel > 0)
                    {
                        if (ItemDictionary.IdToName.ContainsKey($"{ingredientId}@{enchantmentLevel}"))
                            ingredientId = $"{ingredientId}@{enchantmentLevel}";
                        else if(ItemDictionary.IdToName.ContainsKey($"{ingredientId}_LEVEL{enchantmentLevel}@{enchantmentLevel}"))
                            ingredientId = $"{ingredientId}_LEVEL{enchantmentLevel}@{enchantmentLevel}";
                    }

                    if (prices.TryGetValue(ingredientId, out var ingredientPricesPerLocation) &&
                        ingredientPricesPerLocation.TryGetValue(locationCode, out var ingredientPrice))
                    {
                        craftingCosts[ingredientIndex] = ingredientPrice.UnitPriceSilver * ingredient.Count;
                    }
                    else
                    {
                        craftValid = false;
                        break;
                    }
                }

                if (!craftValid)
                    continue;

                long itemVolume = itemPrice.VolumeSold;
                var craftInfo = new SalvageInfo(item, enchantmentLevel, craft, craftingCosts, itemPrice.UnitPriceSilver,
                    itemVolume);

                var existingCraftIndex =
                    salvageInfos.FindIndex(x =>
                        x.Item.UniqueName.Equals(craftInfo.Item.UniqueName) &&
                        x.EnchantmentLevel == craftInfo.EnchantmentLevel);

                //If there is an existing craft that gives more profit, we replace it 
                if (existingCraftIndex >= 0 &&
                    salvageInfos[existingCraftIndex].UnitSalvageProfit < craftInfo.UnitSalvageProfit)
                    salvageInfos[existingCraftIndex] = craftInfo;
                else if (existingCraftIndex < 0)
                    salvageInfos.Add(craftInfo);
            }
        }

        salvageInfos.Sort((x, y) => x.UnitSalvageProfit.CompareTo(y.UnitSalvageProfit));
        return salvageInfos;
    }
}
