using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using NRedisStack.Search.Aggregation;
using StackExchange.Redis;
namespace Trader;

public class TraderQuery
{
    private const int GET_CHAR_LIMIT = 4096;

    private int[] enchants = [0];
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

    public TraderQuery OfQuality(Range qualityRange) =>
        OfQuality(Enumerable.Range(qualityRange.Start.Value, qualityRange.End.Value - qualityRange.Start.Value)
            .ToArray());

    public TraderQuery OfQuality(params int[] qualities)
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
        if (tiers == null || tiers.Length < 1)
            return itemNames;

        List<string> names = new List<string>((int)(itemNames.Length * tiers.Length * MathF.Max(1, enchants.Length)));

        var hasEnchants = enchants.Length > 0;
        foreach (var item in itemNames)
        foreach (var tier in tiers)
        {
            if (hasEnchants)
            {
                foreach (var enchant in enchants)
                {
                    if (enchant > 0)
                        names.Add($"T{tier}_{item}@{enchant}");
                    else
                        names.Add($"T{tier}_{item}");
                }
            }
            else
            {
                names.Add($"T{tier}_{item}");
            }
        }

        return names.ToArray();
    }

    public async Task<Dictionary<string, Dictionary<string, Price>>> GetPrices(IDatabase db)
    {
        var itemNames = GetItemNames();

        locations ??= Array.ConvertAll(Enum.GetValues<MarketLocation>(), x => ((int)x).ToString());

        var aggregation = db.FT().Aggregate("idx:prices",
            new AggregationRequest($"@ItemGroupTypeID:{{{string.Join(" | ", itemNames)}}}")
            .GroupBy(["@LocationID", "@ItemGroupTypeID", "@UnitPriceSilver", "@Timestamp", "@EnchantmentLevel", "@VolumeSold"],
                    [Reducers.Max("@Timestamp").As("latest_time")]));

        var items = new Dictionary<string, Dictionary<string, Price>>();

        for (int i = 0; i < aggregation.TotalResults; i++)
        {
            var item = aggregation.GetRow(i);
            
            var itemID = (string)item["ItemGroupTypeID"];
            var locationID = (string)item["LocationID"];
            
            items.TryAdd(itemID, new());
                items[itemID][locationID] = new Price()
                {
                    EnchantmentLevel = (int)item["EnchantmentLevel"],
                    ItemTypeId = itemID,
                    LocationId = locationID,
                    QualityLevel = (int)item["QualityLevel"],
                    Timestamp = long.Parse((string)item["Timestamp"]),
                    UnitPriceSilver = (long)item["UnitPriceSilver"],
                    VolumeSold = long.Parse(item["VolumeSold"])
                };
        }

        return items;
    }
}
