using NRedisStack.Search.Aggregation;
namespace Trader;

public static class UpgradeUtility
{
    private static Dictionary<string, long[,]> enchantCostsPerTier;

    public static IReadOnlyDictionary<string, long[,]> EnchantCostsPerTier => enchantCostsPerTier;

    public static async Task UpdateEnchantPrices(bool fetchFromAODB = false)
    {
        var allEnchantsQuery = new TraderQuery().OfItems("RUNE", "SOUL", "RELIC").OfTiers(4..9);

        if (fetchFromAODB)
            await FetchEnchantPricesFromAODB(allEnchantsQuery);

        var prices = await allEnchantsQuery.GetPrices(Database.Instance.DB);

        enchantCostsPerTier = new();
        foreach (var (item, pricesPerLocation) in prices)
        {
            var nameSplit = item.Split('_');
            var tier = int.Parse(nameSplit[0][1..]);
            var index = nameSplit[1] switch
            {
                "RELIC" => 2,
                "SOUL" => 1,
                "RUNE" or _ => 0,
            };

            foreach (var (location, price) in pricesPerLocation)
            {
                enchantCostsPerTier.TryAdd(location, new long[5, 3]);
                enchantCostsPerTier[location][tier - 4, index] = price.UnitPriceSilver;
            }
        }
    }
    private static async Task FetchEnchantPricesFromAODB(TraderQuery allEnchantsQuery)
    {
        var json = await allEnchantsQuery.GetPriceHistoryJSON(TraderQuery.TimeScale.Daily);
        await Database.Instance.AddPricesFromAPI(json);
    }

    public static bool CanFlipUpgrade(Order sellOrder, Row buyOrder, int enchantLevelFrom, int enchantLevelTo,
        int upgradeAmount, MarketLocation location, out long materialCost)
    {
        materialCost = 0;
        var profit = ProfitUtility.Calculate((long)buyOrder["$.UnitPriceSilver"] / 10000,
            sellOrder.UnitPriceSilver / 10000);

        if (profit < 0)
            return false;

        var tier = int.Parse(buyOrder["$.ItemTypeId"].ToString()[1].ToString());

        var locationAdjusted = location is MarketLocation.CaerleonAuction2 ? MarketLocation.Caerleon : location;
        var enchantCostsForTier = EnchantCostsPerTier[((int)locationAdjusted).ToString()];

        for (int i = enchantLevelFrom; i < enchantLevelTo; i++)
            materialCost += enchantCostsForTier[tier - 4, i] * upgradeAmount;

        return profit - materialCost > 0;
    }
}
