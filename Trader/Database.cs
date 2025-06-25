using System.Collections.ObjectModel;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using NRedisStack.Search.Aggregation;
using NRedisStack.Search.Literals.Enums;
using StackExchange.Redis;
namespace Trader;

public class Database
{
    private const string CONNECTION_STRING = "localhost:6379";

    private static readonly ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(CONNECTION_STRING);
    private static readonly Dictionary<string, int> prefixToUpgradeAmounts = new Dictionary<string, int>()
    {
        { "SH", 96 },
        { "HE", 96 },
        { "OF", 96 },
        { "CA", 96 },
        { "AR", 192 },
        { "BA", 192 },
        { "MA", 288 },
        { "2H", 384 }
    };

    private IDatabase db;
    private IJsonCommands json;

    private Dictionary<string, long[,]> enchantCostsPerTier;

    public List<Flip> Flips { get; private set; } = new();

    public void Start()
    {
        db = redis.GetDatabase();
        json = db.JSON();

        CreateIndex();
        GetEnchantPrices();
    }

    private void CreateIndex()
    {
        try
        {
            db.FT().DropIndex("idx:orders");
        }
        catch(Exception)
        {
            // ignored
        }

        try
        {
            db.FT().DropIndex("idx:prices");
        }
        catch(Exception)
        {
            // ignored
        }

        var ordersSchema = new Schema()
            .AddNumericField(new FieldName("$.Id", "ID"))
            .AddTagField(new FieldName("$.ItemGroupTypeId", "ItemGroupTypeID"))
            .AddTagField(new FieldName("$.LocationId", "LocationID"))
            .AddNumericField(new FieldName("$.UnitPriceSilver", "UnitPriceSilver"))
            .AddNumericField(new FieldName("$.QualityLevel", "QualityLevel"))
            .AddNumericField(new FieldName("$.EnchantmentLevel", "EnchantmentLevel"));

        db.FT().Create("idx:orders", FTCreateParams.CreateParams().On(IndexDataType.JSON).Prefix("orders:"),
            ordersSchema);

        var pricesSchema = new Schema()
            .AddTagField(new FieldName("$.ItemGroupTypeId", "ItemGroupTypeID"))
            .AddTagField(new FieldName("$.LocationId", "LocationID"))
            .AddNumericField(new FieldName("$.UnitPriceSilver", "UnitPriceSilver"))
            .AddNumericField(new FieldName("$.QualityLevel", "QualityLevel"))
            .AddNumericField(new FieldName("$.EnchantmentLevel", "EnchantmentLevel"))
            .AddNumericField(new FieldName("$.Timestamp", "Timestamp"));

        db.FT().Create("idx:prices", FTCreateParams.CreateParams().On(IndexDataType.JSON).Prefix("prices:"),
            pricesSchema);
    }
    public async Task AddOrders(string ordersJSON)
    {
        var orders = JObject.Parse(ordersJSON)["Orders"];

        var presentAYearLater = DateTime.UtcNow + TimeSpan.FromDays(365);
        var halfHour = TimeSpan.FromSeconds(1800);
        var hour = TimeSpan.FromSeconds(3600);

        var children = orders.Children();

        var pipeline = new Pipeline(db);
        var tasks = new List<Task>();

        foreach (var child in children)
        {
            var location = child["LocationId"].Value<string>();
            child["LocationId"] = location;

            var key = $"orders:{location}:{child["ItemGroupTypeId"].Value<string>()}:{child["Id"].Value<string>()}";
            tasks.Add(pipeline.Json.SetAsync(key, "$", child.ToString(Formatting.None)));

            tasks.Add(pipeline.Db.KeyExpireAsync(key,
                DateTime.Parse((string)child["Expires"]) > presentAYearLater ? halfHour : hour));
            //If the sale won't expire (a.k.a from the BM), expire after thirty minutes.
            //Otherwise, an hour.
        }

        pipeline.Execute();
        await Task.WhenAll(tasks);
    }
    public async Task GetFlips(MarketLocation from, MarketLocation to)
    {
        var allBuyOrders = db.FT().Aggregate("idx:orders",
            new AggregationRequest($"@LocationID:{{{(int)to}}}").Load(
                new("$.ItemTypeId"),
                new("$.ItemGroupTypeId"),
                new("$.LocationId"),
                new("$.UnitPriceSilver"),
                new("$.QualityLevel"),
                new("$.EnchantmentLevel"),
                new("$.Amount")));


        var sellOrders = new List<Task<SearchResult>>();
        Flips.Clear();

        var tasks = new List<Task>();

        var trBatchCount = 0;
        for (int i = 0; i < allBuyOrders.TotalResults; i++)
        {
            var order = allBuyOrders.GetRow(i);
            var price = order["$.UnitPriceSilver"];
            var quality = order["$.QualityLevel"];
            var enchantmentLevel = order["$.EnchantmentLevel"];

            var query = new Query($"@LocationID:{{{(int)from}}} @ItemGroupTypeID:{{{order["$.ItemGroupTypeId"]}}}")
                .AddFilter(new Query.NumericFilter("UnitPriceSilver", 0, (double)price - 1))
                .AddFilter(new Query.NumericFilter("EnchantmentLevel", 0, (double)enchantmentLevel))
                .AddFilter(new Query.NumericFilter("QualityLevel", (double)quality, double.PositiveInfinity))
                .Limit(0, 100)
                .SetSortBy("UnitPriceSilver", true).ReturnFields(
                    "$.ItemTypeId",
                    "$.ItemGroupTypeId",
                    "$.LocationId",
                    "$.UnitPriceSilver",
                    "$.QualityLevel",
                    "$.EnchantmentLevel",
                    "$.Amount");

            sellOrders.Add(db.FT().SearchAsync("idx:orders", query));
        }

        await Task.WhenAll(sellOrders);

        for (var i = 0; i < sellOrders.Count; i++)
        {
            if (!sellOrders[i].IsCompleted)
                continue;

            var buyOrder = allBuyOrders.GetRow(i);
            var buyOrderEnchantmentLevel = (int)buyOrder["$.EnchantmentLevel"];
            var enchantItemCount = prefixToUpgradeAmounts[buyOrder["$.ItemTypeId"].ToString()[3..5]];

            var sellOrdersForItem = sellOrders[i].Result;

            if (sellOrdersForItem.TotalResults <= 0)
                continue;

            int currentEnchantLevel = -1;
            for (int j = 0; j < sellOrdersForItem.TotalResults; j++)
            {
                var sellOrder = sellOrdersForItem.Documents[j];
                var sellOrderEnchantLevel = (int)sellOrder["$.EnchantmentLevel"];

                if (currentEnchantLevel == sellOrderEnchantLevel)
                    continue;

                if (sellOrderEnchantLevel == buyOrderEnchantmentLevel)
                {
                    Flips.Add(new Flip(buyOrder, sellOrder));
                    break;
                }

                //We can't flip upgrade to 4 yet.
                if (buyOrderEnchantmentLevel >= 4)
                    continue;

                //Check for upgrade enchant
                currentEnchantLevel = sellOrderEnchantLevel;
                if (CanFlipUpgrade(sellOrder, buyOrder, sellOrderEnchantLevel, buyOrderEnchantmentLevel,
                    enchantItemCount, from,
                    out var materialCost))
                    Flips.Add(new Flip(buyOrder, sellOrder, materialCost, enchantItemCount));
            }
        }

        Flips.Sort((x, y) => y.Profit.CompareTo(x.Profit));
    }
    private async Task GetEnchantPrices()
    {
        var allEnchantsQuery = new TraderQuery().OfItems("RUNE", "SOUL", "RELIC").OfTiers(4..9);

        var json = await allEnchantsQuery.GetPriceHistoryJSON(TraderQuery.TimeScale.Daily);

        await AddPrices(json);

        var prices = await allEnchantsQuery.GetPrices(db);

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

    public async Task AddPrices(string priceHistoryJSON)
    {
        var jArray = JArray.Parse(priceHistoryJSON);
        var pipeline = new Pipeline(db);

        var epoch = DateTime.UnixEpoch;

        List<Task> tasks = new List<Task>();
        foreach (var itemPrice in jArray)
        {
            var location = Enum.Parse<MarketLocation>(itemPrice["location"].ToString().Replace(" ", string.Empty));
            var itemTypeID = itemPrice["item_id"].ToObject<string>();
            var quality = itemPrice["quality"].ToObject<int>();

            var priceHistory = itemPrice["data"].Children();
            foreach (var priceHistoryItem in priceHistory)
            {
                var timestamp = DateTime.Parse(priceHistoryItem["timestamp"].ToString());

                var indexOfAt = itemTypeID.LastIndexOf('@');
                var totalMS = (long)(timestamp - epoch).TotalMilliseconds;

                var price = new Price
                {
                    ItemTypeId = itemTypeID,
                    LocationId = ((int)location).ToString(),
                    QualityLevel = quality,
                    Timestamp = totalMS,
                    UnitPriceSilver = priceHistoryItem["avg_price"].ToObject<long>(),
                    EnchantmentLevel = indexOfAt >= 0 ? int.Parse(itemTypeID[(indexOfAt + 1)..]) : 0
                };

                tasks.Add(pipeline.Json.SetAsync($"prices:{price.LocationId}:{itemTypeID}:{quality}:{totalMS}",
                    "$",
                    JsonConvert.SerializeObject(price)));
            }
        }
        pipeline.Execute();

        Task.WhenAll(tasks);
    }

    private bool CanFlipUpgrade(Document sellOrder, Row buyOrder, int enchantLevelFrom, int enchantLevelTo,
        int upgradeAmount, MarketLocation location, out long materialCost)
    {
        materialCost = 0;
        var profit = ProfitUtility.Calculate((long)buyOrder["$.UnitPriceSilver"] / 10000,
            (long)sellOrder["$.UnitPriceSilver"] / 10000);

        if (profit < 0)
            return false;

        var tier = int.Parse(buyOrder["$.ItemTypeId"].ToString()[1].ToString());

        var enchantCostsForTier = enchantCostsPerTier[((int)location).ToString()];

        for (int i = enchantLevelFrom; i < enchantLevelTo; i++)
            materialCost += enchantCostsForTier[tier - 4, i] * upgradeAmount;

        return profit - materialCost > 0;
    }
}
