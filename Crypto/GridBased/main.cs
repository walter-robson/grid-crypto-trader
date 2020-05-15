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

//we'll probably need to save market data for later use, lets make a class to do that
