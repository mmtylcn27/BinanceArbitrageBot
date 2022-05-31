using System;
using System.Threading.Tasks;
using ArbitrageBot.Objects.Database.Objects;

namespace ArbitrageBot.Objects.Exchange
{
    public class Route
    {
        public enum RouteType
        {
            Normal,
            Reverse
        }

        public readonly Pair Pair1, Pair2, Pair3;
        private readonly decimal _minTradeValue, _maxTradeValue, _minProfitValue, _fee;

        public delegate Task OnArbitrage(Pair[] pairs, ArbitrageInfo info);
        public event OnArbitrage OnArbitrageHandler;

        public Route(Pair route1Param, Pair route2Param, Pair route3Param, decimal minTradeValue, decimal maxTradeValue, decimal minProfitValue, decimal fee)
        {
            Pair1 = route1Param;
            Pair2 = route2Param;
            Pair3 = route3Param;

            _minTradeValue = minTradeValue;
            _maxTradeValue = maxTradeValue;
            _minProfitValue = minProfitValue;
            _fee = fee;
        }

        private (decimal value, decimal fee, decimal price) CalculateBuy(Pair pair, decimal quoteValue)
        {
            var getInfo = pair.GetInfo.Sell;

            if (getInfo.price > 0m && getInfo.quantity > 0m && quoteValue > 0m)
            {
                if (getInfo.price * getInfo.quantity >= quoteValue)
                {
                    var value = quoteValue / getInfo.price;
                    var fee = value * _fee;

                    return (value, fee, getInfo.price);
                }
            }

            return default;
        }

        private (decimal value, decimal fee, decimal price) CalculateSell(Pair pair, decimal baseValue)
        {
            var getInfo = pair.GetInfo.Buy;

            if (getInfo.price > 0m && getInfo.quantity > 0m && baseValue > 0m)
            {
                if (getInfo.quantity >= baseValue)
                {
                    var value = baseValue * getInfo.price;
                    var fee = value * _fee;
                    
                    return (value, fee, getInfo.price);
                }
            }

            return default;
        }

        private Task<ArbitrageInfo> Calculate(Pair[] pairs, decimal valueUsdt, RouteType routeType)
        {
            return Task.Run(() =>
            {
                if (!pairs[0].CheckMinNotional(valueUsdt))
                    return null;

                decimal quantity1 = -1m, quantity2 = -1m, quantity3 = -1m;

                (decimal value, decimal fee, decimal price) firstRoute = default, twoRoute = default, threeRoute = default;

                switch (routeType)
                {
                    case RouteType.Normal:
                    {
                        firstRoute = CalculateBuy(pairs[0], valueUsdt.CalculateValue(valueUsdt * _fee));

                        quantity1 = pairs[0].LotFilter(firstRoute.value.CalculateValue(firstRoute.fee));

                        if (!pairs[1].CheckMinNotional(quantity1))
                            return null;

                        twoRoute = CalculateBuy(pairs[1], quantity1);

                        quantity2 = pairs[2].MarketLotFilter(pairs[2].LotFilter(twoRoute.value.CalculateValue(twoRoute.fee)));

                        threeRoute = CalculateSell(pairs[2], quantity2);

                        if (!pairs[2].CheckMinNotional(threeRoute.value))
                            return null;

                        quantity3 = threeRoute.value.CalculateValue(threeRoute.fee);

                        break;
                    }

                    case RouteType.Reverse:
                    {
                        firstRoute = CalculateBuy(pairs[0], valueUsdt.CalculateValue(valueUsdt * _fee));

                        quantity1 = pairs[1].MarketLotFilter(pairs[1].LotFilter(firstRoute.value.CalculateValue(firstRoute.fee)));

                        twoRoute = CalculateSell(pairs[1], quantity1);

                        if (!pairs[1].CheckMinNotional(twoRoute.value))
                            return null;

                        quantity2 = pairs[2].MarketLotFilter(pairs[2].LotFilter(twoRoute.value.CalculateValue(twoRoute.fee)));

                        threeRoute = CalculateSell(pairs[2], quantity2);

                        if (!pairs[2].CheckMinNotional(threeRoute.value))
                            return null;

                        quantity3 = threeRoute.value.CalculateValue(threeRoute.fee);

                        break;
                    }

                }

                if (!firstRoute.Equals(default) && !twoRoute.Equals(default) && !threeRoute.Equals(default))
                {
                    var processMoney = firstRoute.price * quantity1;
                    processMoney += processMoney * _fee;

                    var result = quantity3 - processMoney;

                    if (result >= _minProfitValue)
                        return new ArbitrageInfo()
                        {
                            FoundDate = DateTime.Now,
                            ProcessMoneyValue = processMoney,
                            ResultMoneyValue = quantity3,
                            ProfitMoneyValue = result,
                            NamePair1 = pairs[0].GetPair,
                            NamePair2 = pairs[1].GetPair,
                            NamePair3 = pairs[2].GetPair,
                            FeePair1 = firstRoute.fee,
                            FeePair2 = twoRoute.fee,
                            FeePair3 = threeRoute.fee,
                            PricePair1 = firstRoute.price,
                            PricePair2 = twoRoute.price,
                            PricePair3 = threeRoute.price,
                            QuantityPair1 = quantity1,
                            QuantityPair2 = quantity2,
                            QuantityPair3 = quantity3,
                            RouteType = routeType
                        };
                }

                return null;
            });
        }

        public async Task OnPriceChanged()
        {
            var getPair1 = Pair1.GetInfo;
            var getPair2 = Pair2.GetInfo;
            var getPair3 = Pair3.GetInfo;

            if (getPair1.Sell.status == Pair.Status.Lower && getPair3.Buy.status == Pair.Status.Upper)
            {
                if (getPair2.Sell.status == Pair.Status.Lower)
                {
                    var pairs = new[] { Pair1, Pair2, Pair3 };

                    var info = await Calculate(pairs, _maxTradeValue, RouteType.Normal);

                    if (info is null)
                        info = await Calculate(pairs, _minTradeValue, RouteType.Normal);

                    if (!(info is null) && !(OnArbitrageHandler is null))
                        await OnArbitrageHandler(pairs, info);
                }
                else if (getPair2.Buy.status == Pair.Status.Upper)
                {
                    var pairs = new[] { Pair3, Pair2, Pair1 };

                    var info = await Calculate(pairs, _maxTradeValue, RouteType.Reverse);

                    if (info is null)
                        info = await Calculate(pairs, _minTradeValue, RouteType.Reverse);

                    if (!(info is null) && !(OnArbitrageHandler is null))
                        await OnArbitrageHandler(pairs, info);
                }
            }
        }
    }
}
