namespace Trader;

public class TravelingUtility
{
    public static async Task GetTravelingFlips(bool inRoyalsOnly)
    {
        var items = ItemDictionary.Items.Keys.ToArray();

        var query = new TraderQuery().OfItems(items);
        var prices = await query.GetPrices(Database.Instance.DB);

        var locations = new List<MarketLocation>()
        {
            MarketLocation.Thetford,
            MarketLocation.Lymhurst,
            MarketLocation.Bridgewatch,
            MarketLocation.BlackMarket,
            MarketLocation.Caerleon,
            MarketLocation.Martlock,
            MarketLocation.FortSterling,
            MarketLocation.Brecilien,
        };

        var locationHashSet = locations.ConvertAll<string>(x => ((int)x).ToString()).ToHashSet();

        if (inRoyalsOnly)
        {
            locations.Remove(MarketLocation.Brecilien);
            locations.Remove(MarketLocation.Caerleon);
            locations.Remove(MarketLocation.BlackMarket);
        }

        var travelingFlips = new List<TravelingFlip>();
        foreach (var (item, pricePerLocation) in prices)
        {
            Price minPrice = null;
            Price maxPrice = null;
            foreach (var (location, price) in pricePerLocation)
            {
                var silverPrice = price.UnitPriceSilver;
                if (silverPrice <= 0)
                    continue;

                if (minPrice == null || silverPrice < minPrice.UnitPriceSilver)
                    minPrice = price;
                
                if(maxPrice == null || silverPrice > maxPrice.UnitPriceSilver)
                    maxPrice = price;
            }

            if (minPrice.UnitPriceSilver < maxPrice.UnitPriceSilver * (1 - 0.08f - 0.025f))
                travelingFlips.Add(new (ItemDictionary.Items[item], minPrice, maxPrice));
        }
        
        travelingFlips.Sort((x,y) => y.ProfitPerKg.CompareTo(x.ProfitPerKg));
    }
}
public class TravelingFlip
{
    public Item Item { get; private set; }
    
    public Price MinPrice { get; private set; }
    public Price MaxPrice { get; private set; }
    
    public long ProfitPerUnit { get; private set; }
    public long ProfitPerKg { get; private set; }

    public long PotentialProfits { get; private set; }
    
    public TravelingFlip(Item item, Price minPrice, Price maxPrice)
    {
        Item = item;
        MinPrice = minPrice;
        MaxPrice = maxPrice;
        ProfitPerUnit = (long)(MaxPrice.UnitPriceSilver - (1 - 0.08f - 0.025f) - MinPrice.UnitPriceSilver * 1.025f);
        ProfitPerKg = (long)(ProfitPerUnit / item.Weight);
        PotentialProfits = ProfitPerUnit * MaxPrice.VolumeSold;
    }
}
