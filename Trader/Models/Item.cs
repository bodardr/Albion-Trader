using System.Text.Json.Serialization;
using Newtonsoft.Json;
namespace Trader;

[Serializable]
public class Item
{
    [JsonProperty("@uniquename")]
    public string UniqueName { get; set; }

    [JsonProperty("@tier")]
    public int Tier { get; set; }

    [JsonProperty("@weight")]
    public double Weight { get; set; }

    [JsonProperty("@maxstacksize")]
    public int MaxStackSize { get; set; }

    [JsonProperty("@itemvalue")]
    public double ItemValue { get; set; }

    [JsonProperty("@famevalue")]
    public double FameValue { get; set; }

    [JsonProperty("craftingrequirements")]
    public CraftingRequirement[] CraftingRecipes { get; set; }

    public CraftingRequirement MainRequirement => CraftingRecipes != null ? CraftingRecipes[0] : null;
}
[Serializable]
public class CraftingRequirement
{
    [JsonProperty("@silver")]
    public int Silver { get; set; }

    [JsonProperty("@time")]
    public double Time { get; set; }

    [JsonProperty("@craftingfocus")]
    public int CraftingFocus { get; set; }

    [JsonProperty("@amountcrafted")]
    public int AmountCrafted { get; set; } = 1;
    
    [JsonProperty("craftresource")]
    public CraftResource[] CraftResources { get; set; }
}
[Serializable]
public class CraftResource
{
    [JsonProperty("@uniquename")]
    public string UniqueName { get; set; }

    [JsonProperty("@enchantmentLevel")]
    public int EnchantmentLevel { get; set; }

    [JsonProperty("@count")]
    public int Count { get; set; }
}
