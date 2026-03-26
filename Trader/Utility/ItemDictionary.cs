using System.IO;
using Newtonsoft.Json.Linq;
namespace Trader;

public static class ItemDictionary
{
    public static readonly Dictionary<string, string> IdToName = new();
    public static readonly Dictionary<string, string> LocationIdToName = new();
    public static readonly Dictionary<string, Item> Items = new();
    public static readonly Dictionary<int, string> ItemNumberToId = new();

    public static void Initialize()
    {
        ParseItemNames();
        ParseLocationNames();
        ParseItems();
    }

    private static void ParseItemNames()
    {
        var allNameLines = File.ReadAllLines(Path.Combine(Environment.CurrentDirectory, "items.txt"));

        foreach (var line in allNameLines)
        {
            var lineSplit = line.Split(':');
            if (lineSplit.Length < 3)
            {
                Console.WriteLine($"ok line isn't conform {line}");
                continue;
            }

            var itemId = lineSplit[1].Trim();
            ItemNumberToId[int.Parse(lineSplit[0])] = itemId;
            IdToName[itemId] = lineSplit[2].Trim();
        }
    }

    private static void ParseLocationNames()
    {
        var allNameLines = File.ReadAllLines(Path.Combine(Environment.CurrentDirectory, "world.txt"));

        foreach (var line in allNameLines)
        {
            var lineSplit = line.Split(':');
            if (lineSplit.Length < 2)
            {
                Console.WriteLine($"ok line isn't conform {line}");
                continue;
            }

            LocationIdToName[lineSplit[0].Trim()] = lineSplit[1].Trim();
        }
    }
    private static void ParseItems()
    {
        Items.Clear();

        var json = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "items.json"));
        var jObject = JObject.Parse(json);

        var items = jObject["items"];

        var simpleItems = items["simpleitem"];
        var equipmentItems = items["equipmentitem"];
        var weapons = items["weapon"];

        AddItemsToDict(simpleItems);
        AddItemsToDict(equipmentItems, true);
        AddItemsToDict(weapons);
    }

    private static void AddItemsToDict(JToken? simpleItem, bool overrideSalvageable = false)
    {
        foreach (var item in simpleItem)
        {
            var craftingRequirements = item["craftingrequirements"];
            if (craftingRequirements != null)
            {
                if (craftingRequirements is not JArray)
                    item["craftingrequirements"] = new JArray(craftingRequirements);

                var requirementsArray = item["craftingrequirements"];
                foreach (var requirement in requirementsArray)
                    if (requirement["craftresource"] is not JArray)
                        requirement["craftresource"] = new JArray(requirement["craftresource"]);
            }

            var itemObj = item.ToObject<Item>();
            if (overrideSalvageable)
                itemObj.Salvageable = true;
            Items.TryAdd(itemObj.UniqueName, itemObj);
        }
    }
}
