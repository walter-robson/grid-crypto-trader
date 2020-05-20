#include <stdio.h>
#include <math.h>
#include <ctime>


public class Order
{
    public Order()
    {
        OrderCreateTime = DateTime.now;
    }
    public string OrderID {get; set; }
    public string PublicHandlerID {get; set; }
    public DateTime OrderCreateTime {get; set; }
    public string Side {get; set; }
    public decimal Price {get; set; }
    public decimal OrderQty {get; set;}
    public decimal CumQty {get; set; }

    public decimal Notional
    {
        get
        {
            return CumQty * AvgPx;
        }
    }
    public decimal LeavesQty
    {
        get
        {
            return OrderQty - CumQty;
        }
    }
    public decimal AvgPx {get; set; }

}
//each order will be a buy and a sell
public class OrderPair
{
    public OrderPair(DateTime createTime, string pairId){
        PairCreateTime = createTime;
        PairStatus = PairStatuses.Working;
        PairId = pairId;
    }

    public string PairId {get; set; }
    public int Width {get; set; }
    public PairStatuses PairStatus {get; set; }
    public DateTime PairCreateTime {get; set; }

    public Orders.Order Buy {get; set; }
    public Orders.Order Sell {get; set; }

}
//this is the enum for the status of our pairs
public enum PairStatuses
{
    Working,
    Completed,
    NothingDone,
}
//Nbbo will be our "tick" of our data
public class Nbbo
{
    public DateTime Time {get; set; }
    public decimal Bid {get; set; }
    public decimal BidSize {get; set; }
    public decimal Ask {get; set; }
    public decimal AskSize {get; set; }
    
    //helper function
    public decimal Midpoint 
    {
        get
        {
            return (Bid + Ask) / 2;
        }
    }

    public override string ToString()
    {
        return $"{Time.Ticks},{Bid},{BidSize},{Ask},{AskSize}";
    }

//delegate/event pattern that says when OnNbboUpdated is fired it will pass an Nbbo object (tick data)
    public delegate void dgNbboChange(Nbbo nbbo);
    public event dgNbboChange OnNbboUpdated;

}
//NbboListener Class using library from 
// https://github.com/dougdellolio/coinbasepro-csharp
public Nbbo NbboListener_CBP() //CBP: coin base pro
{

}

//we'll probably need to save market data for later use, lets make a class to do that
public void RecordNbbos()
{
    var nbboListener = new NbboListener_CBP();
    nbboListener.OnNbboUpdated += NbboListener_OnNbboUpdated;

    task t = new Task(nbboListener.Start);
    t.Start();

}
private void NbboListener_OnNbboUpdated(Nbbo nbbo)
{
    Console.WriteLine(nbbo.ToString());
    System.IO.File.AppendAllText($"nbbo_{nbbo.Time:yyyyMMdd}.cbp.csv", nbbo.ToString() + Enviornment.NewLine);
}

public class PairsTraderParams
{
    public int MaxPairDuration {get; set; }
    public int PairWidth {get; set; }
}

public class PairsTrader : IDisposable
{
    public List<OrderPair> OrderPairs {get; }
    private int pairCounter = 0;
    public PairsTraderParams PairsParams {get; set; }
    private int processingIntervalMilliseconds = 1000;
    private Nbbo currentNbbo = null;
    private DateTime lastProccesingCycle = DateTime.MinValue;
    private readonly IGateway gateway;

    public PairsTrader(NbboPublisher marketData, 
    IGateway exchangeGateway, 
    PairsTraderParams pairsParams)
        {
            this.gateway = gateway;
            this.gateway.OnOrderUpdated += Gateway_OnOrderUpdated;

            this.marketData = marketData;
            marketData.OnNbboUpdated += OnTickReceived;
            marketData.OnNbboUpdated += this.gateway.OnMarketDataTick;

            PairsParams = pairsParams;
            OrderPairs = new List<OrderPair>();
        }
}

public void OnTickReceived(Nbbo nbbo)
{
    currentNbbo = nbbo;

    if(nbbo.Time - lastProccesingCycle > TimeSpan.FromMilliseconds(processingIntervalMilliseconds))
    {
        lastProccesingCycle = nbbo.Time;

        if(ShouldCreatePair())
            CreatePair(nbbo);
    }
}

private bool ShouldCreatePair()
{
    if(currentNbbo == null) return false; //cant buy with no market data

    if(!OrderPairs.Any()) return true;
    var workingOrderCount = OrderPairs.Count(p => p.PairStatus == OrderPair.PairStatuses.Working);
    if(workingOrderCount == 0) return true;

    return false;
}

private void CreatePair(Nbbo nbbo)
{
    pairCounter++; //used to assign an ID for each pair

    int pairWidth = PairsParams.PairWidth;

    var pair = new OrderPair(nbbo.Time, pairCounter.ToString())
    {
        OpenPrice = nbbo.Midpoint,
        Width = pairWidth,
        Buy = new Order(){OrderQty = .1m, OrderID = $"{pairCounter}_B", Price = nbbo.Midpoint - pairWidth, Side = "B"},
        Sell = new Order(){OrderQty = .1m, OrderID = $"{pairCounter}_S", Price = nbbo.Midpoint + pairWidth, Side = "S"}
    };

    OrderPairs.Add(pair);

    gateway.SendOrder(pair.Buy);
    gateway.SendOrder(pair.Sell);
}
    
public interface IGateway
{
    public void SendOrder(Order order);
    public void CancelOrder(Order order);
    public void MarketDataTick(Nbbo nbbo);
    event GatewayBase.dgOrderUpdated OnOrderUpdated;

}

public class GatewayBase
{
    public delegate void dgOrderUpdated(Order order, DateTime updateTime);
}

public class Gateway_CBP : IGateway
{
    private CoinbaseProClient coinbaseProClient;
    public Gateway_CBP()
    {
        var authenticator = new Authenticator([/*our API tokens will go here */]);
        coinbaseProClient = new CoinbaseProClient(authenticator);
    }

    public event GatewayBase.dgOrderUpdated OnOrderUpdated;

    public void CancelOrder(Order order)
    {
        coinbaseProClient.OrdersService.CancelOrder(order.PublicHandlerID)
    }
    public void OnMarketDataTick(Nbbo nbbo)
    {
        //we dont use this yet
    }
    public async void SendOrder(Order order)
    {
        await coinbaseProClient.OrdersService.PlaceLimitOrderAsync([/*order params here */]);

    }
    //option to insert a more complicated method to listen to CBP socket
}

public class TestGateway : IGateway
{
    private readonly Dictionary<string, Order> makerOrders = new Dictionary<string, Order>();
    private readonly Dictionary<string, Order> takerOrders = new Dictionary<string, Order>();
    private Nbbo priorNbbo = new Nbbo();

    public event GatewayBase.dgOrderUpdated OnOrderUpdated;

    public void SendOrder(Order order)
    {
        if(order.Price > 0 )
        {
            makerOrders[order.OrderID] = order;

        }
        else 
        {
            takerOrders[order.OrderID] = order;
        }
    }
    public void CancelOrder(Order order)
    {
        makerOrders.Remove(order.OrderID);
    }
    public void OnMarketDataTick(Nbbo nboo)
    {
        /*check the processing interval, and dont bother processing if the prices are the same as at the last tick */
        if(((Nbbo.Time - priorNbbo.Time) < TimeSpan.FromMilliseconds(1000)) 
            || 
            ((nbbo.Ask == priorNbbo.Ask) && (nbbo.Bid == priorNbbo.Bid)))
        {
            return;
        }

        priorNbbo = nbbo;

        List<Order> filledMakerOrders = new List<Order>();
        List<Order> filledTakerOrders = new List<Order>();

        //check if this tick (nbbo point) satisfies a maker order
        foreach(var order in makerOrders)
        {
            if(((order.Value.Side == "B") && (order.Value.Price > nbbo.Ask))
                ||
                ((order.Value.Side == "S") && (order.Value.Price < nbbo.Ask)))
            {
                order.Value.AvgPx = order.Value.Price;
                decimal fillQty = GetFillQty(nbbo, order.Value);
                order.Value.CumQty += fillQty;

                if(order.Value.CumQty == order.Value.OrderQty)
                {
                    filledMakerOrders.Add(order.Value);
                }
                OnOrderUpdated?.Invoke(order.Value, nbbo.Time);
            }
        }

        //Fill the taker orders
        foreach(var order in takerOrders)
        {
            order.Value.AvgPx = (order.Value.Side == "B" ? nbbo.Ask : nbbo.Bid);
            order.Value.CumQty += GetFillQty(nbbo, order.Value);
            OnOrderUpdated?.Invoke(order.Value, nbbo.Time);

            if(order.Value.CumQty == order.Value.OrderQty)
            {
                filledTakerOrders.Add(order.Value);
            }

        }
        private decimal GetFillQty(Nbbo nbbo, Order order)
        {
            if(order.Side == "B")
            {
                return nbbo.AskSize > order.LeavesQty ? order.LeavesQty : nbbo.AskSize;
            }
            else 
            {
                return nbbo.AskSize > order.LeavesQty ? order.LeavesQty : nbbo.BidSize;
            }
        }

        filledMakerOrders.ForEach(f => makerOrders.Remove(f.OrderID) );
        filledTakerOrders.ForEach(f => takerOrders.Remove(f.OrderID) );
    }
}