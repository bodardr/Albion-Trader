using System.IO;
namespace Trader;

public static class ItemDictionary
{
    public static readonly Dictionary<string, string> IdToName = new();
    public static readonly Dictionary<string, string> LocationIDToName = new();

    public static void Initialize()
    {
        ParseItemNames();
        ParseLocationNames();
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

            IdToName[lineSplit[1].Trim()] = lineSplit[2].Trim();
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
}
