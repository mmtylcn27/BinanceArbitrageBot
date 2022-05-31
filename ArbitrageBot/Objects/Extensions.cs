using System;
using Binance.Net;

namespace ArbitrageBot.Objects
{
    public static class Extensions
    {
        public static decimal Floor(this decimal value) => 
            BinanceHelpers.Floor(value);
        public static decimal FloorPrice(this decimal value, decimal tickSize) =>
            BinanceHelpers.FloorPrice(tickSize, value);
        public static decimal FloorQuantity(this decimal value, decimal minQuantity, decimal maxQuantity, decimal stepSize) =>
            BinanceHelpers.ClampQuantity(minQuantity, maxQuantity, stepSize, value);
        public static decimal CalculateValue(this decimal value, decimal fee) =>
            ((value - fee) * 0.999m).Floor();
    }
}
