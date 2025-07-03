using System.IO;
using Newtonsoft.Json.Linq;
namespace Trader;

public static class ItemDictionary
{
    public static readonly Dictionary<string, string> IdToName = new();
    public static readonly Dictionary<string, string> LocationIDToName = new();
    public static readonly Dictionary<string, Item> Items = new();
    public static readonly Dictionary<int, string> ItemNumberToID = new();

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

            var itemID = lineSplit[1].Trim();
            ItemNumberToID[int.Parse(lineSplit[0])] = itemID;
            IdToName[itemID] = lineSplit[2].Trim();
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

            LocationIDToName[lineSplit[0].Trim()] = lineSplit[1].Trim();
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

        AddItemsToDict(simpleItems);
        AddItemsToDict(equipmentItems);
    }
    private static void AddItemsToDict(JToken? simpleItem)
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
            Items.TryAdd(itemObj.UniqueName, itemObj);
        }
    }
}
