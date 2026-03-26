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

    public ObservableCollection<Flip> Flips => db.Flips;
    public ObservableCollection<SalvageInfo> SalvageFlips { get; set; } = new();
    public ObservableCollection<CraftingInfo> Crafts { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();

        DataContext = this;

        ItemDictionary.Initialize();

        db.Start();

        listener = new();
        listener.Listen();

        _ = UpgradeUtility.UpdateEnchantPrices();
        craftingFlipsComboBox.ItemsSource = Enum.GetValues<MarketLocation>();
        craftingFlipsComboBox.SelectedIndex = 0;
        craftingFlipsDestComboBox.ItemsSource = Enum.GetValues<MarketLocation>();
        craftingFlipsDestComboBox.SelectedIndex = 0;
        salvageFlipsComboBox.ItemsSource = Enum.GetValues<MarketLocation>();
        salvageFlipsComboBox.SelectedIndex = 0;
    }

    private void OnFlipButtonClick(object sender, RoutedEventArgs e)
    {
        db.GetFlips(MarketLocation.Caerleon, MarketLocation.BlackMarket);
    }

    private async void OnCraftingFlipButtonClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(budgetTextBox.Text, out var budget))
            budget = 1000000;

        if (!float.TryParse(dailyVolumePercentage.Text, out var volumePercentage))
            volumePercentage = 65f;

        var crafts = await CraftingUtility.GetCraftingFlips((MarketLocation)craftingFlipsComboBox.SelectionBoxItem, (MarketLocation)craftingFlipsDestComboBox.SelectionBoxItem, budget,
            volumePercentage / 100f);
        Crafts.Clear();
        foreach (var craft in crafts)
        {
            Crafts.Add(craft);
        }
    }

    private void OnTravelingFlipButtonClick(object sender, RoutedEventArgs e)
    {
        _ = TravelingUtility.GetTravelingFlips(true);
    }

    private async void OnSalvageFlipButtonClick(object sender, RoutedEventArgs e)
    {
        var flips = await CraftingUtility.GetSalvageFlips((MarketLocation)salvageFlipsComboBox.SelectionBoxItem);

        SalvageFlips.Clear();
        foreach (var info in flips)
            SalvageFlips.Add(info);
    }
}
