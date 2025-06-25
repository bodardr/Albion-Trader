using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace Trader;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private Listener listener = new();
    private Database db = new();

    public List<Flip> Flips => db.Flips;

    public event PropertyChangedEventHandler? PropertyChanged;
    
    public MainWindow()
    {
        InitializeComponent();
        
        ItemDictionary.Initialize();
        
        db.Start();
        
        listener.Database = db;
        listener.Listen();
    }

    private void OnFlipButtonClick(object sender, RoutedEventArgs e)
    {
        db.GetFlips(MarketLocation.Caerleon, MarketLocation.BlackMarket);
    }
}