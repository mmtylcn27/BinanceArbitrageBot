using System;
using System.Collections.Generic;
using System.Linq;
using Binance.Net.Objects.Models.Spot;

namespace ArbitrageBot.Objects.Exchange
{
    public class Pair
    {
        public enum Status
        {
            Upper,
            Lower
        }

        public readonly (string Name, int Precision) Base, Quote;
        public readonly string GetPair;

        private readonly BinanceSymbolMarketLotSizeFilter _marketLotFilterInfo;
        private readonly BinanceSymbolLotSizeFilter _lotFilterInfo;
        private readonly BinanceSymbolPriceFilter _priceFilterInfo;
        private readonly BinanceSymbolMinNotionalFilter _minNotionalFilter;
        
        private readonly object _lockObject;
        private readonly List<(decimal price, decimal quantity)> _askList;
        private readonly List<(decimal price, decimal quantity)> _bidList;

        private DateTime _updateTime;
        private decimal _lastSellPrice, _lastBuyPrice;
        private Status _lastSellStatus, _lastBuyStatus;

        public ((decimal price, decimal quantity, Status status) Sell, (decimal price, decimal quantity, Status status) Buy, DateTime updateTime) GetInfo
        {
            get
            {
                lock (_lockObject)
                {
                    if (_askList.Count == 0m || _bidList.Count == 0m)
                        return default;

                    var sellQuantity = _askList.Sum(x => x.quantity);
                    var buyQuantity = _bidList.Sum(x => x.quantity);

                    var sellPrice = _askList.Average(x => x.price).FloorPrice(_priceFilterInfo.TickSize);
                    var buyPrice = _bidList.Average(x => x.price).FloorPrice(_priceFilterInfo.TickSize);

                    var sellStatus = _lastSellPrice == sellPrice ? _lastSellStatus : _lastSellPrice > sellPrice ? Status.Lower : Status.Upper;
                    var buyStatus = _lastBuyPrice == buyPrice ? _lastBuyStatus : _lastBuyPrice > buyPrice ? Status.Lower : Status.Upper;

                    _lastSellPrice = sellPrice;
                    _lastBuyPrice = buyPrice;
                    _lastSellStatus = sellStatus;
                    _lastBuyStatus = buyStatus;

                    return ((sellPrice, sellQuantity, sellStatus), (buyPrice, buyQuantity, buyStatus), _updateTime);
                }
            }
        }

        public void SetValue(List<(decimal price, decimal quantity)> askListParam, List<(decimal price, decimal quantity)> bidListParam, DateTime updateTime)
        {
            lock (_lockObject)
            {
                _askList.Clear(); _bidList.Clear();

                _askList.AddRange(askListParam.ToArray());
                _bidList.AddRange(bidListParam.ToArray());
                _updateTime = updateTime.AddHours(3);
            }
        }

        public decimal LotFilter(decimal value) =>
            value.FloorQuantity(_lotFilterInfo.MinQuantity, _lotFilterInfo.MaxQuantity, _lotFilterInfo.StepSize);

        public decimal MarketLotFilter(decimal value) =>
            value.FloorQuantity(_marketLotFilterInfo.MinQuantity, _marketLotFilterInfo.MaxQuantity, _marketLotFilterInfo.StepSize);

        public bool CheckMinNotional(decimal value)
        {
            if (value >= _minNotionalFilter.MinNotional)
                return true;

            return false;
        }

        public Pair((string Name, int Precision) baseParam, (string Name, int Precision) quoteParam, (BinanceSymbolMarketLotSizeFilter MarketLotFilterInfo, BinanceSymbolLotSizeFilter LotFilterInfo, BinanceSymbolPriceFilter PriceFilterInfo, BinanceSymbolMinNotionalFilter MinNotionalFilter) filterInfo)
        {
            if (string.IsNullOrEmpty(baseParam.Name) || string.IsNullOrEmpty(quoteParam.Name))
                throw new Exception("Base or Quote is empty");

            if(filterInfo.LotFilterInfo is null || filterInfo.PriceFilterInfo is null || filterInfo.MinNotionalFilter is null)
                throw new Exception("Filter is null");

            if (filterInfo.MarketLotFilterInfo is null)
            {
                filterInfo.MarketLotFilterInfo = new BinanceSymbolMarketLotSizeFilter();
                filterInfo.MarketLotFilterInfo.MaxQuantity = filterInfo.LotFilterInfo.MaxQuantity;
                filterInfo.MarketLotFilterInfo.MinQuantity = filterInfo.LotFilterInfo.MinQuantity;
                filterInfo.MarketLotFilterInfo.StepSize = filterInfo.LotFilterInfo.StepSize;
            }

            Base = baseParam;
            Quote = quoteParam;
            GetPair = $"{Base.Name}{Quote.Name}";

            _marketLotFilterInfo = filterInfo.MarketLotFilterInfo;
            _lotFilterInfo = filterInfo.LotFilterInfo;
            _priceFilterInfo = filterInfo.PriceFilterInfo;
            _minNotionalFilter = filterInfo.MinNotionalFilter;

            _lockObject = new object();
            _askList = new List<(decimal price, decimal quantity)>();
            _bidList = new List<(decimal price, decimal quantity)>();
        }
    }
}
