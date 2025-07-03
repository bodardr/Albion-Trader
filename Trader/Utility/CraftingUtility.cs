using System.Text;
namespace Trader;

public static class CraftingUtility
{ 
    public static async Task GetCraftingFlips()
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

        var query = new TraderQuery().OfItems(itemsToFetch.ToArray());

        var prices = await query.GetPrices(Database.Instance.DB);
        var craftInfos = new List<CraftingInfo>();

        var locations = new[]
        {
            ((int)MarketLocation.Thetford).ToString(),
            ((int)MarketLocation.Lymhurst).ToString(),
            ((int)MarketLocation.Bridgewatch).ToString(),
            ((int)MarketLocation.BlackMarket).ToString(),
            ((int)MarketLocation.Caerleon).ToString(),
            ((int)MarketLocation.Martlock).ToString(),
            ((int)MarketLocation.FortSterling).ToString(),
            ((int)MarketLocation.Brecilien).ToString(),
        };

        foreach (var (item, craft) in crafts)
        {
            long[,] craftingCosts = new long[locations.Length, craft.CraftResources.Length];

            for (int ingredientIndex = 0; ingredientIndex < craft.CraftResources.Length; ingredientIndex++)
            {
                var ingredient = craft.CraftResources[ingredientIndex];
                for (int i = 0; i < locations.Length; i++)
                    if (prices.ContainsKey(ingredient.UniqueName) &&
                        prices[ingredient.UniqueName].TryGetValue(locations[i], out var price))
                        craftingCosts[i, ingredientIndex] = price.UnitPriceSilver * ingredient.Count;
            }

            long[] itemPrices = new long[locations.Length];
            long[] itemVolumes = new long[locations.Length];

            for (int i = 0; i < locations.Length; i++)
            {
                if (!prices.ContainsKey(item.UniqueName) ||
                    !prices[item.UniqueName].TryGetValue(locations[i], out var price))
                    continue;

                itemPrices[i] = price.UnitPriceSilver;
                itemVolumes[i] = price.VolumeSold;
            }

            craftInfos.Add(new CraftingInfo(item, craft, craftingCosts, itemPrices, itemVolumes));
        }

        craftInfos.Sort((x, y) => y.UnitProfits.Max().CompareTo(x.UnitProfits.Max()));
    }
}
