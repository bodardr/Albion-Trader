using Newtonsoft.Json.Linq;
using NRedisStack.Search.Aggregation;
using Document = NRedisStack.Search.Document;
namespace Trader;

public struct Flip
{
    public Order SellOrder { get; private set; }
    public Order BuyOrder { get; private set; }

    public long Profit { get; private set; }
    public long TotalUpgradeCost { get; private set; }

    public bool IsUpgrade => TotalUpgradeCost > 0;

    public int UpgradeItemCount { get; private set; } = 0;
    public long Budget => (long)((SellOrder.UnitPriceSilver + TotalUpgradeCost) * 1.08f);

    public string UpgradeInfo => IsUpgrade
        ? $"x{UpgradeItemCount} {SellOrder.ItemTypeId[..2]}.{SellOrder.EnchantmentLevel} to {SellOrder.ItemTypeId[..2]}.{BuyOrder.EnchantmentLevel}\nCost: {TotalUpgradeCost}"
        : string.Empty;

    public Flip(Row buyOrder, Document sellOrder)
    {
        BuyOrder = new Order
        {
            Id = (long)buyOrder["$.Id"],
            Amount = (int)buyOrder["$.Amount"],
            AuctionType = "offer",
            EnchantmentLevel = (int)buyOrder["$.EnchantmentLevel"],
            QualityLevel = (int)buyOrder["$.QualityLevel"],
            LocationId = buyOrder["$.LocationId"].ToString(),
            ItemTypeId = buyOrder["$.ItemTypeId"].ToString(),
            ItemGroupTypeId = buyOrder["$.ItemGroupTypeId"].ToString(),
            UnitPriceSilver = (long)buyOrder["$.UnitPriceSilver"] / 10000,
        };

        SellOrder = new Order
        {
            Id = (long)sellOrder["$.Id"],
            Amount = (int)sellOrder["$.Amount"],
            AuctionType = "request",
            EnchantmentLevel = (int)sellOrder["$.EnchantmentLevel"],
            QualityLevel = (int)sellOrder["$.QualityLevel"],
            LocationId = sellOrder["$.LocationId"].ToString(),
            ItemTypeId = sellOrder["$.ItemTypeId"].ToString(),
            ItemGroupTypeId = sellOrder["$.ItemGroupTypeId"].ToString(),
            UnitPriceSilver = (long)sellOrder["$.UnitPriceSilver"] / 10000,
        };

        Profit = ProfitUtility.Calculate(BuyOrder.UnitPriceSilver, SellOrder.UnitPriceSilver);
    }


    public Flip(Row buyOrderRow, Document sellOrderDoc, long totalUpgradeCost, int upgradeItemCount) : this(buyOrderRow,
        sellOrderDoc)
    {
        TotalUpgradeCost = totalUpgradeCost;
        UpgradeItemCount = upgradeItemCount;
        //Buy order plus tax
        Profit -= (long)(totalUpgradeCost * (1.025 + 0.08f));
    }
}
