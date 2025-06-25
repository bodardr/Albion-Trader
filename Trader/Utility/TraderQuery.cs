using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using NRedisStack;
using NRedisStack.Search;
using StackExchange.Redis;
namespace Trader;

public class TraderQuery
{
    private const string baseURL = "https://west.albion-online-data.com/api/v2/stats/";

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
        var url = $"{baseURL}history/{GetAllParams()}";

        if (timeScale == TimeScale.Daily)
            url += "time-scale=24";
        else
            url = url.TrimEnd('&');

        var response = await new HttpClient().GetAsync(url);
        return await response.Content.ReadAsStringAsync();
    }

    private string GetAllParams()
    {
        var str = new StringBuilder();

        if (itemNames.Length < 1 || tiers.Length < 1 || enchants.Length < 1)
        {
            throw new ArgumentException("Object must have itemNames, tiers and enchants defined");
        }

        str.AppendJoin(',', GetItemNames());
        str.Append('?');

        if (locations != null && locations.Length > 0)
            str.Append($"locations={string.Join(',', locations)}&");

        if (qualities != null && qualities.Length > 0)
            str.Append($"qualities={string.Join(',', qualities)}&");

        return str.ToString();
    }
    public List<string> GetItemNames()
    {
        List<string> names = new List<string>((int)(itemNames.Length * tiers.Length * MathF.Max(1, enchants.Length)));

        foreach (var item in itemNames)
        foreach (var tier in tiers)
        foreach (var enchant in enchants)
        {
            if (enchant > 0)
                names.Add($"T{tier}_{item}@{enchant}");
            else
                names.Add($"T{tier}_{item}");
        }
        return names;
    }

    public async Task<Dictionary<string, Dictionary<string, Price>>> GetPrices(IDatabase db)
    {
        var itemNames = GetItemNames();

        locations ??= Array.ConvertAll(Enum.GetValues<MarketLocation>(), x => ((int)x).ToString());
        
        var pipeline = new Pipeline(db);

        var tasks = new Dictionary<string, Dictionary<string, Task<SearchResult>>>();
        foreach (var item in itemNames)
        foreach (var location in locations)
        {
            tasks.TryAdd(item, new());
            tasks[item][location] = pipeline.Ft.SearchAsync("idx:prices",
                new Query($"@LocationID:{{{location}}} @ItemGroupTypeID:{{{item}}}").SetSortBy("Timestamp", false).Limit(0, 1));
        }

        pipeline.Execute();

        await Task.WhenAll(tasks.SelectMany(x => x.Value.Values).ToList());
        
        var items = new Dictionary<string, Dictionary<string, Price>>();
        foreach (var (item, pricesPerLocation) in tasks)
        foreach (var (location, price) in pricesPerLocation)
        {
            var searchResult = price.Result;
            
            items.TryAdd(item, new());
            if (searchResult.TotalResults > 0)
                items[item][location] = JsonConvert.DeserializeObject<Price>(searchResult.ToJson()[0]);
        }

        return items;
    }
}
