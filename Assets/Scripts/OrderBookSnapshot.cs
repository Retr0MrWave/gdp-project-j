using System;
using System.Collections.Generic;

[Serializable]
public struct OrderBookLevel
{
    public double price;
    public double quantity;

    public OrderBookLevel(double price, double quantity)
    {
        this.price = price;
        this.quantity = quantity;
    }
}

[Serializable]
public class OrderBookSnapshot
{
    public string symbol;
    public long capturedAtMs;
    public List<OrderBookLevel> bids = new List<OrderBookLevel>();
    public List<OrderBookLevel> asks = new List<OrderBookLevel>();
}
