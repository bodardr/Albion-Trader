using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Trader;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private Database db = new();
    private Listener listener;

    public ObservableCollection<Flip> Flips => db.Flips;

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
    }

    private void OnFlipButtonClick(object sender, RoutedEventArgs e)
    {
        db.GetFlips(MarketLocation.Caerleon, MarketLocation.BlackMarket);
    }

    private void OnCraftingFlipButtonClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(budgetTextBox.Text, out var budget))
            budget = 1000000;

        if (!float.TryParse(dailyVolumePercentage.Text, out var volumePercentage))
            volumePercentage = 65f;

        _ = CraftingUtility.GetCraftingFlips((MarketLocation)craftingFlipsComboBox.SelectionBoxItem, budget,
            volumePercentage / 100f);
    }

    private void OnTravelingFlipButtonClick(object sender, RoutedEventArgs e)
    {
        _ = TravelingUtility.GetTravelingFlips(true);
    }
}
