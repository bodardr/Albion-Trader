using System.Net;
using System.Net.Http;
using System.Text;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using NRedisStack.Search.Aggregation;
using StackExchange.Redis;
namespace Trader;

public class TraderQuery
{
    private const int GET_CHAR_LIMIT = 4096;

    private int[] enchants;
    private int[] tiers;
    private string[] itemNames;
    private string[] locations;
    private int[] qualities;

    public enum TimeScale
    {
        Hourly,
        Daily,
    }

    public TraderQuery OfItems(params string[] itemNamesBase)
    {
        itemNames = itemNamesBase;
        return this;
    }

    public TraderQuery OfTiers(Range tierRange) =>
        OfTiers(Enumerable.Range(tierRange.Start.Value, tierRange.End.Value - tierRange.Start.Value).ToArray());

    public TraderQuery OfTiers(params int[] tiers)
    {
        this.tiers = tiers;
        return this;
    }

    public TraderQuery OfQualities(Range qualityRange) =>
        OfQualities(Enumerable.Range(qualityRange.Start.Value, qualityRange.End.Value - qualityRange.Start.Value)
            .ToArray());

    public TraderQuery OfQualities(params int[] qualities)
    {
        this.qualities = qualities;
        return this;
    }

    public TraderQuery OfEnchants(Range enchantRange) =>
        OfEnchants(Enumerable.Range(enchantRange.Start.Value, enchantRange.End.Value - enchantRange.Start.Value)
            .ToArray());

    public TraderQuery OfEnchants(params int[] enchants)
    {
        this.enchants = enchants;
        return this;
    }

    public TraderQuery OfLocations(params string[] locations)
    {
        this.locations = locations;
        return this;
    }

    public async Task<string> GetPriceHistoryJSON(TimeScale timeScale)
    {
        var endStr = GetAllParams();
        if (timeScale == TimeScale.Daily)
            endStr += (string.IsNullOrEmpty(endStr) ? "?" : '&') + "time-scale=24";
        else
            endStr = endStr.TrimEnd('&');

        var charLimit = GET_CHAR_LIMIT - endStr.Length;
        var str = new StringBuilder($"{API.BASE_URL}history/");

        var queries = new List<string>();
        var allNames = GetItemNames();

        foreach (var name in allNames)
        {
            if (str.Length + name.Length + 1 < charLimit)
            {
                str.Append(name + ',');
            }
            else
            {
                //Remove the last comma
                str.Remove(str.Length - 1, 1);
                str.Append(endStr);
                queries.Add(str.ToString());

                //Start a new query
                str = new StringBuilder($"{API.BASE_URL}history/");
            }
        }

        //Remove the last comma
        str.Remove(str.Length - 1, 1);
        str.Append(endStr);
        queries.Add(str.ToString());

        var getRequests = new List<Task<HttpResponseMessage>>();


        var clientHandler = new HttpClientHandler()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        using var httpClient = new HttpClient(clientHandler);
        foreach (var query in queries)
            getRequests.Add(httpClient.GetAsync(query));

        await Task.WhenAll(getRequests);

        var responses = new List<Task<string>>();

        foreach (var request in getRequests)
            responses.Add(request.Result.Content.ReadAsStringAsync());

        await Task.WhenAll(responses);

        var finalResponse = new StringBuilder();
        for (var i = 0; i < responses.Count; i++)
        {
            var response = responses[i];

            // Remove the ']' character.
            if (i > 0)
            {
                finalResponse.Remove(finalResponse.Length - 1, 1);
                finalResponse.Append(',');
                finalResponse.Append(response.Result[1..]);
            }
            else
            {
                finalResponse.Append(response.Result);
            }
        }

        return finalResponse.ToString();
    }

    private string GetAllParams()
    {
        var str = new StringBuilder("?");

        if (itemNames.Length < 1)
            throw new ArgumentException("Object must have some itemNames defined");

        var now = DateTime.UtcNow;
        var threeDaysAgo = now.AddDays(-2);
        str.Append($"date={threeDaysAgo:dd-MM-yyyy}&end_date={now:dd-MM-yyyy}");

        if (locations != null && locations.Length > 0)
            str.Append($"&locations={string.Join(',', locations)}&");

        if (qualities != null && qualities.Length > 0)
            str.Append($"&qualities={string.Join(',', qualities)}&");

        return str.ToString();
    }

    public string[] GetItemNames()
    {
        var hasTiers = tiers != null && tiers.Length >= 1;

        List<string> names =
            new List<string>((itemNames.Length * (tiers?.Length ?? 1) * (enchants?.Length ?? 1)));

        var hasEnchants = enchants != null && enchants.Length > 0;
        if (hasTiers)
        {
            foreach (var item in itemNames)
            foreach (var tier in tiers)
                AddEnchants($"{tier}_{item}");
        }
        else
        {
            foreach (var item in itemNames)
                AddEnchants(item);
        }

        return names.ToArray();

        void AddEnchants(string item)
        {
            if (hasEnchants)
            {
                foreach (var enchant in enchants)
                {
                    var itemName = $"{item}@{enchant}";
                    if (ItemDictionary.IdToName.ContainsKey(itemName))
                        names.Add(itemName.Replace("@", "\\@"));
                }
            }
            else
            {
                names.Add(item);

                for (int i = 1; i < 4; i++)
                {
                    var itemName = $"{item}@{i}";
                    if (ItemDictionary.IdToName.ContainsKey(itemName))
                        names.Add(itemName.Replace("@", "\\@"));
                }
            }
        }
    }

    public async Task<Dictionary<string, Dictionary<string, Price>>> GetPrices(IDatabase db)
    {
        var itemNames = GetItemNames();

        locations ??= Array.ConvertAll(Enum.GetValues<MarketLocation>(), x => ((int)x).ToString());

        var priceAggregation = $"@ItemTypeId:{{{string.Join(" | ", itemNames)}}}";
        if (qualities != null)
            priceAggregation += $" @QualityLevel:[{qualities.Min()} {qualities.Max()}]";

        var pricesAggregation = db.FT().Aggregate("idx:prices",
            new AggregationRequest(
                    priceAggregation)
                .GroupBy([
                        "@LocationId", "@ItemTypeId", "@UnitPriceSilver", "@Timestamp", "@EnchantmentLevel",
                        "@VolumeSold"
                    ],
                    [Reducers.Max("@Timestamp").As("latest_time")]));

        var orderAggregation =
            $"@ItemTypeId:{{{string.Join(" | ", itemNames)}}} @LocationId:{{{string.Join(" | ", locations)}}}";
        if (qualities != null)
            orderAggregation += $" @QualityLevel:[{qualities.Min()} {qualities.Max()}]";

        var ordersAggregation = db.FT().Aggregate("idx:orders",
            new AggregationRequest(orderAggregation)
                .GroupBy(["@LocationId", "@ItemTypeId", "@UnitPriceSilver", "@EnchantmentLevel", "@QualityLevel"],
                    [Reducers.Min("@UnitPriceSilver").As("min_price")])
                .SortBy(new SortedField("@min_price")).Limit(100000));


        var items = new Dictionary<string, Dictionary<string, Price>>();

        for (int i = 0; i < pricesAggregation.TotalResults; i++)
        {
            var item = pricesAggregation.GetRow(i);

            var itemId = (string)item["ItemTypeId"];
            var locationId = (string)item["LocationId"];

            items.TryAdd(itemId, new());
            items[itemId][locationId] = new Price()
            {
                EnchantmentLevel = (int)item["EnchantmentLevel"],
                ItemTypeId = itemId,
                LocationId = locationId,
                QualityLevel = (int)item["QualityLevel"],
                Timestamp = long.Parse((string)item["Timestamp"]),
                UnitPriceSilver = (long)item["UnitPriceSilver"],
                VolumeSold = long.Parse(item["VolumeSold"])
            };
        }

        for (int i = 0; i < ordersAggregation.TotalResults; i++)
        {
            var row = ordersAggregation.GetRow(i);

            var itemId = row["ItemTypeId"];
            var locationId = row["LocationId"];

            var price = new Price()
            {
                LocationId = locationId,
                ItemTypeId = itemId,
                QualityLevel = int.Parse(row["QualityLevel"]),
                EnchantmentLevel = int.Parse(row["EnchantmentLevel"]),
                UnitPriceSilver = long.Parse(row["min_price"]),
                VolumeSold = -1
            };

            items.TryAdd(itemId, new());
            if (!items[itemId].ContainsKey(locationId))
                items[itemId].Add(locationId, price);
        }

        return items;
    }
}
