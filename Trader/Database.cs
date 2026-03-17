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

    public ObservableCollection<Flip> Flips { get; private set; } = new();

    public IDatabase DB => db;
    
    public static Database Instance { get; private set; }

    public void Start()
    {
        db = redis.GetDatabase();
        json = db.JSON();

        CreateIndex();

        Instance = this;
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
            .AddNumericField(new FieldName("$.Timestamp", "Timestamp"))
            .AddNumericField(new FieldName("$.VolumeSold", "VolumeSold"));

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

            if (location.Equals(((int)MarketLocation.CaerleonAuction2).ToString()))
                location = ((int)MarketLocation.Caerleon).ToString();

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
        Flips.Clear();

        await UpgradeUtility.UpdateEnchantPrices();

        var buyOrdersAggregation = db.FT().Aggregate("idx:orders",
            new AggregationRequest($"@LocationID:{{{(int)to}}}").Load(
                new("$.ItemTypeId"),
                new("$.ItemGroupTypeId"),
                new("$.LocationId"),
                new("$.UnitPriceSilver"),
                new("$.QualityLevel"),
                new("$.EnchantmentLevel"),
                new("$.Amount")));

        var sellOrdersAggregation = db.FT().Aggregate("idx:orders",
            new AggregationRequest($"@LocationID:{{{(int)from}}}")
                .GroupBy(["@ItemGroupTypeID", "@QualityLevel", "@EnchantmentLevel"],
                    [Reducers.Min("@UnitPriceSilver").As("min_price")])
                .SortBy(new SortedField("@min_price")).Limit(100000));

        var sellOrders = new Dictionary<string, List<Order>>();
        for (int i = 0; i < sellOrdersAggregation.TotalResults; i++)
        {
            var row = sellOrdersAggregation.GetRow(i);
            var order = new Order(((int)from).ToString(), (string)row["ItemGroupTypeID"],
                int.Parse(row["QualityLevel"]),
                int.Parse(row["EnchantmentLevel"]),
                long.Parse(row["min_price"]));

            if (!sellOrders.TryAdd(order.ItemGroupTypeId, [order]))
                sellOrders[order.ItemGroupTypeId].Add(order);
        }

        for (var i = 0; i < buyOrdersAggregation.TotalResults; i++)
        {
            var buyOrder = buyOrdersAggregation.GetRow(i);

            var buyOrderPrice = long.Parse(buyOrder["$.UnitPriceSilver"]);
            var buyOrderItemName = buyOrder["$.ItemGroupTypeId"].ToString();
            var buyOrderEnchantmentLevel = (int)buyOrder["$.EnchantmentLevel"];
            var buyOrderQualityLevel = (int)buyOrder["$.QualityLevel"];
            var enchantItemCount = prefixToUpgradeAmounts[buyOrder["$.ItemTypeId"].ToString()[3..5]];

            if (!sellOrders.TryGetValue(buyOrderItemName, out var sellOrdersForItem))
                continue;

            int currentEnchantLevel = -1;
            for (int j = 0; j < sellOrdersForItem.Count; j++)
            {
                var sellOrder = sellOrdersForItem[j];

                if (sellOrder.UnitPriceSilver >= buyOrderPrice)
                    break;

                if (currentEnchantLevel == sellOrder.EnchantmentLevel || buyOrderQualityLevel > sellOrder.QualityLevel)
                    continue;

                if (sellOrder.EnchantmentLevel == buyOrderEnchantmentLevel)
                {
                    Flips.Add(new Flip(buyOrder, sellOrder));
                    break;
                }

                //We can't flip upgrade to 4 yet.
                if (buyOrderEnchantmentLevel >= 4)
                    continue;

                //Check for upgrade enchant
                currentEnchantLevel = sellOrder.EnchantmentLevel;
                if (UpgradeUtility.CanFlipUpgrade(sellOrder, buyOrder, sellOrder.EnchantmentLevel,
                    buyOrderEnchantmentLevel,
                    enchantItemCount, from,
                    out var materialCost))
                    Flips.Add(new Flip(buyOrder, sellOrder, Math.Max(1,materialCost), enchantItemCount));
            }
        }
    }

    public async Task AddPricesFromAPI(string priceHistoryJSON)
    {
        var jArray = JArray.Parse(priceHistoryJSON);
        var pipeline = new Pipeline(db);

        List<Task> tasks = new List<Task>();
        foreach (var itemPrice in jArray)
        {
            if (!itemPrice.HasValues)
                continue;

            var location = Enum.Parse<MarketLocation>(itemPrice["location"].ToString().Replace(" ", string.Empty));
            var itemTypeID = itemPrice["item_id"].ToObject<string>();
            var quality = itemPrice["quality"].ToObject<long>();

            var priceHistory = itemPrice["data"].Children();
            foreach (var priceHistoryItem in priceHistory)
            {
                var timestamp = DateTime.Parse(priceHistoryItem["timestamp"].ToString());

                var indexOfAt = itemTypeID.LastIndexOf('@');

                var price = new Price
                {
                    ItemTypeId = itemTypeID,
                    LocationId = ((int)location).ToString(),
                    QualityLevel = quality,
                    Timestamp = timestamp.Ticks,
                    UnitPriceSilver = priceHistoryItem["avg_price"].ToObject<long>(),
                    EnchantmentLevel = indexOfAt >= 0 ? int.Parse(itemTypeID[(indexOfAt + 1)..]) : 0,
                    VolumeSold = priceHistoryItem["item_count"].ToObject<long>(),
                };

                tasks.Add(pipeline.Json.SetAsync($"prices:{itemTypeID}:{price.LocationId}:{quality}:{timestamp.Ticks}",
                    "$", JsonConvert.SerializeObject(price)));
            }
        }

        pipeline.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task AddPrices(string? priceHistoryJSON)
    {
        var jObject = JObject.Parse(priceHistoryJSON);

        var itemID = ItemDictionary.ItemNumberToID[jObject["AlbionId"].ToObject<int>()];
        var qualityLevel = jObject["QualityLevel"].Value<int>();
        var locationID = jObject["LocationId"].Value<string>();

        if (locationID.Equals(((int)MarketLocation.CaerleonAuction2).ToString()))
            locationID = ((int)MarketLocation.Caerleon).ToString();

        var marketHistories = jObject["MarketHistories"].Value<JArray>();

        var pipeline = new Pipeline(db);
        var tasks = new List<Task>();

        var prices = new List<long>();
        var volumes = new List<long>();

        DateTime currentTime = default;
        foreach (var marketHistoryItem in marketHistories)
        {
            var itemAmount = marketHistoryItem["ItemAmount"].Value<long>();
            var silverAmount = marketHistoryItem["SilverAmount"].ToObject<long>();

            var indexOfAt = itemID.LastIndexOf('@');

            var timestamp = DateTime.FromBinary(long.Parse(marketHistoryItem["Timestamp"].ToString()));


            if (currentTime == default)
            {
                currentTime = timestamp;
            }
            else if (timestamp.Date != currentTime.Date)
            {
                var totalVolume = volumes.Sum();

                long avgPrice = 0;
                for (int i = 0; i < volumes.Count; i++)
                    avgPrice += volumes[i] * prices[i];

                avgPrice /= totalVolume;

                prices.Clear();
                volumes.Clear();

                var price = new Price
                {
                    ItemTypeId = itemID,
                    LocationId = locationID,
                    QualityLevel = qualityLevel,
                    Timestamp = timestamp.Ticks,
                    UnitPriceSilver = avgPrice,
                    EnchantmentLevel = indexOfAt >= 0 ? int.Parse(itemID[(indexOfAt + 1)..]) : 0,
                    VolumeSold = totalVolume,
                };

                tasks.Add(pipeline.Json.SetAsync($"prices:{itemID}:{locationID}:{qualityLevel}:{timestamp.Ticks}",
                    "$", JsonConvert.SerializeObject(price)));

                currentTime = timestamp;
            }
            
            prices.Add(silverAmount / itemAmount / 10000);
            volumes.Add(itemAmount);
        }

        pipeline.Execute();
        await Task.WhenAll(tasks);
    }
}
