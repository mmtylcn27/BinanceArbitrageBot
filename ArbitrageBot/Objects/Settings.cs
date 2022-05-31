using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArbitrageBot.Objects
{
    public class Settings
    {
        public string BaseCoin, ApiKey, SecretKey;
        public int MaxCoinCount, MaxOrderBookCount;
        public decimal BinanceFee, MinTradeValue, MaxTradeValue, MinProfitValue;
    }
}
