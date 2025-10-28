using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace Trader;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private Database db = new();
    private Listener listener;

    public List<Flip> Flips => db.Flips;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();

        ItemDictionary.Initialize();

        db.Start();

        listener = new();
        listener.Listen();

        _ = UpgradeUtility.UpdateEnchantPrices(false);
    }

    private void OnFlipButtonClick(object sender, RoutedEventArgs e)
    {
        db.GetFlips(MarketLocation.Caerleon, MarketLocation.BlackMarket);
    }
    
    private void OnCraftingFlipButtonClick(object sender, RoutedEventArgs e)
    {
        _ = CraftingUtility.GetCraftingFlips();
    }
    
    private void OnTravelingFlipButtonClick(object sender, RoutedEventArgs e)
    {
        _ = TravelingUtility.GetTravelingFlips(true);
    }
}