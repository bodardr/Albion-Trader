namespace Trader;

public class Price
{
    public string Name => ItemDictionary.IdToName.GetValueOrDefault(ItemTypeId) ?? string.Empty;

    public string ItemTypeId { get; set; }
    public string ItemGroupTypeId => ItemTypeId[^1] == '@' ? ItemTypeId[..^2] : ItemTypeId;

    public string LocationId { get; set; }
    public string LocationName => ItemDictionary.LocationIdToName.GetValueOrDefault(LocationId) ?? string.Empty;

    public long UnitPriceSilver { get; set; }
    public long QualityLevel { get; set; }
    public int EnchantmentLevel { get; set; }

    public long Timestamp { get; set; }
    
    public long VolumeSold { get; set; }
}
