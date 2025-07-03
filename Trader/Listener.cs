using System.IO;
using NATS.Client.Core;
using NATS.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace Trader;

public class Listener
{
    public List<Order> Orders = new List<Order>();
    
    private NatsClient nats = new NatsClient("localhost:4222");
    
    public void Listen()
    {
        ListenToOrders();
        ListenToPrices();
    }
    private async void ListenToOrders()
    {
        await foreach (NatsMsg<string> msg in nats.SubscribeAsync<string>(subject: "marketorders.ingest"))
        {
            Console.WriteLine($"Received: {msg.Subject}: {msg.Data}");
            _ = Database.Instance.AddOrders(msg.Data);
        }
    }
    private async void ListenToPrices()
    {
        await foreach (NatsMsg<string> msg in nats.SubscribeAsync<string>(subject: "markethistories.ingest"))
        {
            Console.WriteLine($"Received: {msg.Subject}: {msg.Data}");
            _ = Database.Instance.AddPrices(msg.Data);
        }
    }
}
