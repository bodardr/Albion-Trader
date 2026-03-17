using StackExchange.Redis;
namespace Trader;

public class Order
{

    public long Id { get; set; }

    public string Name => ItemDictionary.IdToName.GetValueOrDefault(ItemTypeId) ?? string.Empty;

    public string ItemTypeId { get; set; }
    public string LocationId { get; set; }
    public string LocationName => ItemDictionary.LocationIDToName.GetValueOrDefault(LocationId) ?? string.Empty;
    public int QualityLevel { get; set; }
    public int EnchantmentLevel { get; set; }

    public string ItemGroupTypeId { get; set; }
    public long UnitPriceSilver { get; set; }
    public int Amount { get; set; }
    public string AuctionType { get; set; }

    public DateTime Expires { get; set; }
    
    // Maps 1-5 to Albion Quality Strings
    public string QualityName => QualityLevel switch
    {
        2 => "Good",
        3 => "Outstanding",
        4 => "Excellent",
        5 => "Masterpiece",
        1 or _ => "Normal",
    };

    public Order()
    {
        
    }
    
    public Order(string LocationID, string itemGroupTypeID, int qualityLevel, int enchantmentLevel, long unitPriceSilver)
    {
        LocationId = LocationID;
        ItemGroupTypeId = itemGroupTypeID;
        ItemTypeId = ItemGroupTypeId + (EnchantmentLevel > 0 ? $"@{EnchantmentLevel}" :  string.Empty);
        QualityLevel = qualityLevel;
        EnchantmentLevel = enchantmentLevel;
        UnitPriceSilver = unitPriceSilver;
    }
    
    public override string ToString()
    {
        return $"{ItemDictionary.IdToName[ItemGroupTypeId]} ({QualityName}) @ {UnitPriceSilver:N0}";
    }
}
