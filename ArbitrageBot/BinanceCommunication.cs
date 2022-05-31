using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ArbitrageBot.Objects;
using ArbitrageBot.Objects.Database;
using ArbitrageBot.Objects.Database.Objects;
using ArbitrageBot.Objects.Exchange;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Objects;
using Binance.Net.Objects.Models.Spot;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Sockets;
using static ArbitrageBot.Program;

namespace ArbitrageBot
{
    public class BinanceCommunication: IDisposable
    {
        private readonly BinanceClient _client;
        private readonly BinanceSocketClient _socketclient;
        private readonly string _consoleTitle;
        private readonly Settings _settings;
        private readonly SqlContext _dataBase;
        private readonly List<(Pair[] Pairs, ArbitrageInfo GetArbitrage)> _tradeList;
        private readonly bool _testMode;

        private bool _isReady;
        private List<Route> _routeList;
        private decimal _balanceBaseCoin;
       

        private async Task<List<Route>> ParsePairRoute()
        {
            var pairList = new List<((string Name, int Precision) Base, (string Name, int Precision) Quote, (BinanceSymbolMarketLotSizeFilter MarketLotFilterInfo,BinanceSymbolLotSizeFilter LotFilterInfo, BinanceSymbolPriceFilter PriceFilterInfo, BinanceSymbolMinNotionalFilter MinNotionalFilter) FilterInfo)>();
            var routing = new List<Route>();

            var symbolForSpotData = await _client.SpotApi.ExchangeData.GetExchangeInfoAsync();

            foreach (var binanceSymbol in symbolForSpotData.Data.Symbols)
                pairList.Add(((binanceSymbol.BaseAsset, binanceSymbol.BaseAssetPrecision), (binanceSymbol.QuoteAsset, binanceSymbol.QuoteAssetPrecision), (binanceSymbol.MarketLotSizeFilter, binanceSymbol.LotSizeFilter, binanceSymbol.PriceFilter, binanceSymbol.MinNotionalFilter)));

            foreach (var pairOne in pairList)
            {
                if (pairOne.Quote.Name.Equals(_settings.BaseCoin))
                {
                    var getAvailablePairOne = pairList.Where(x => x.Quote.Name.Equals(pairOne.Base.Name)).ToList();

                    foreach (var pairTwo in getAvailablePairOne)
                    {
                        var getAvailablePairTwo = pairList.Where(x => x.Base.Name.Equals(pairTwo.Base.Name) && x.Quote.Name.Equals(_settings.BaseCoin)).ToList();
                        
                        foreach (var pairThree in getAvailablePairTwo)
                            routing.Add(
                                new Route(
                                new Pair(pairOne.Base, pairOne.Quote, pairOne.FilterInfo),
                                new Pair(pairTwo.Base, pairTwo.Quote, pairTwo.FilterInfo),
                                new Pair(pairThree.Base, pairThree.Quote, pairThree.FilterInfo),
                                 _settings.MinTradeValue, _settings.MaxTradeValue, _settings.MinProfitValue, _settings.BinanceFee));
                    }
                }
            }

            return routing;
        }

        private async void PairBookList(DataEvent<IBinanceOrderBook> message)
        {
            if (!_isReady)
                return;

            var askList = new List<(decimal price, decimal quantity)>();
            var bidList = new List<(decimal price, decimal quantity)>();

            message.Data.Asks.ToList().ForEach(ask => askList.Add((ask.Price, ask.Quantity)));
            message.Data.Bids.ToList().ForEach(bid => bidList.Add((bid.Price, bid.Quantity)));

            var getSymbol = message.Data.Symbol.ToUpper();

            var pairList = _routeList.Where(x => x.Pair1.GetPair.Equals(getSymbol) || x.Pair2.GetPair.Equals(getSymbol) || x.Pair3.GetPair.Equals(getSymbol)).ToList();
           
            foreach (var pair in pairList)
            {
                if (pair.Pair1.GetPair.Equals(getSymbol))
                    pair.Pair1.SetValue(askList, bidList, message.Timestamp);
                else if (pair.Pair2.GetPair.Equals(getSymbol))
                    pair.Pair2.SetValue(askList, bidList, message.Timestamp);
                else if (pair.Pair3.GetPair.Equals(getSymbol))
                    pair.Pair3.SetValue(askList, bidList, message.Timestamp);

                await pair.OnPriceChanged();
            }
        }

        private async Task GetArbitrageHandler(Pair[] pairs, ArbitrageInfo info)
        {
           //ConsoleWrite($"Found arbitrage [{info.NamePair1}->>{info.NamePair2}->>{info.NamePair3}][{info.RouteType}]({info.ProcessMoneyValue}) = {info.ProfitMoneyValue} $", ConsoleColor.Yellow);

           if (_balanceBaseCoin >= info.ProcessMoneyValue)
           {
               _balanceBaseCoin -= info.ProcessMoneyValue;

               lock (_tradeList)
               {
                   var found = _tradeList.Any(x => x.GetArbitrage.NamePair1.Equals(info.NamePair1));

                   if(!found)
                       _tradeList.Add((pairs, info));
                   else
                   {
                       _balanceBaseCoin += info.ProcessMoneyValue;
                       return;
                   }
               }

               await ProcessArbitrage(pairs, info);

               lock (_tradeList)
                   _tradeList.Remove((pairs, info));

               _balanceBaseCoin += info.ProcessMoneyValue;
           }
        }

        private Task ProcessArbitrage(Pair[] pairs, ArbitrageInfo info)
        {
            return Task.Run(() =>
            {
                TradeInfo tradeInfo = null;

                ConsoleWrite($"Start trading [{info.NamePair1}->>{info.NamePair2}->>{info.NamePair3}][{info.RouteType}]({info.ProcessMoneyValue}) = {info.ProfitMoneyValue} $", ConsoleColor.Yellow);

                if (!_testMode)
                {
                    if (pairs[0].GetInfo.Sell.status == Pair.Status.Upper || pairs[2].GetInfo.Buy.status == Pair.Status.Lower)
                        ConsoleWrite($"Price changed trade cancelled [{info.NamePair1}->>{info.NamePair2}->>{info.NamePair3}][{info.RouteType}]", ConsoleColor.Red);
                    else
                    {
                        switch (info.RouteType)
                        {
                            case Route.RouteType.Normal:
                            {
                                var amount1 = pairs[0].LotFilter(info.QuantityPair1);

                                ConsoleWrite($"Place order [{info.NamePair1}/{info.PricePair1}/{amount1}]", ConsoleColor.Magenta);

                                var placeOrder1 = PlaceOrder(info.NamePair1, OrderSide.Buy, SpotOrderType.Limit, info.PricePair1, amount1, null);

                                if (!(placeOrder1 is null))
                                {
                                    ConsoleWrite($"Place order success [{info.NamePair1}/{placeOrder1.AverageFillPrice}/{placeOrder1.QuantityFilled}/{placeOrder1.QuoteQuantityFilled}]", ConsoleColor.Green);
                                       
                                    var amount2 = info.QuantityPair2.CalculateValue(0m);

                                    ConsoleWrite($"Place order [{info.NamePair2}/{info.PricePair2}/{amount2}]", ConsoleColor.Magenta);

                                    var placeOrder2 = PlaceOrder(info.NamePair2, OrderSide.Buy, SpotOrderType.Market, null, null, placeOrder1.QuantityFilled.CalculateValue(0m));

                                    while (placeOrder2 is null)
                                        placeOrder2 = PlaceOrder(info.NamePair2, OrderSide.Buy, SpotOrderType.Market, null, null, placeOrder1.QuantityFilled.CalculateValue(0m));

                                    ConsoleWrite($"Place order success [{info.NamePair2}/{placeOrder2.AverageFillPrice}/{placeOrder2.QuantityFilled}/{placeOrder2.QuoteQuantityFilled}]", ConsoleColor.Green);

                                    var amount3 = pairs[2].MarketLotFilter(pairs[2].LotFilter(placeOrder2.QuantityFilled.CalculateValue(0m)));

                                    ConsoleWrite($"Place order [{info.NamePair3}/{info.PricePair3}/{amount3}]", ConsoleColor.Magenta);

                                    var placeOrder3 = PlaceOrder(info.NamePair3, OrderSide.Sell, SpotOrderType.Market, null, amount3, null);

                                    while (placeOrder3 is null)
                                        placeOrder3 = PlaceOrder(info.NamePair3, OrderSide.Sell, SpotOrderType.Market, null, amount3, null);

                                    ConsoleWrite($"Place order success [{info.NamePair3}/{placeOrder3.AverageFillPrice}/{placeOrder3.QuantityFilled}/{placeOrder3.QuoteQuantityFilled}]", ConsoleColor.Green);

                                    tradeInfo = new TradeInfo()
                                    {
                                        CalculateArbitrageInfo = info,
                                        ProcessArbitrageInfo = new ArbitrageInfo()
                                        {
                                            NamePair1 = info.NamePair1,
                                            NamePair2 = info.NamePair2,
                                            NamePair3 = info.NamePair3,
                                            PricePair1 = placeOrder1.AverageFillPrice.HasValue ? placeOrder1.AverageFillPrice.Value : 0m,
                                            PricePair2 = placeOrder2.AverageFillPrice.HasValue ? placeOrder2.AverageFillPrice.Value : 0m,
                                            PricePair3 = placeOrder3.AverageFillPrice.HasValue ? placeOrder3.AverageFillPrice.Value : 0m,
                                            QuantityPair1 = placeOrder1.QuantityFilled,
                                            QuantityPair2 = placeOrder2.QuantityFilled,
                                            QuantityPair3 = placeOrder3.QuoteQuantityFilled,
                                            ProcessMoneyValue = placeOrder1.QuoteQuantityFilled,
                                            ResultMoneyValue = placeOrder3.QuoteQuantityFilled,
                                            FoundDate = DateTime.Now
                                        },
                                        Date = DateTime.Now,
                                        ResultMoney = placeOrder3.QuoteQuantityFilled - placeOrder1.QuoteQuantityFilled
                                    };
                                }
                                else
                                    ConsoleWrite($"Place order failed. price changed [{info.NamePair1}]", ConsoleColor.Red);

                                break;
                            }

                            case Route.RouteType.Reverse:
                            {
                                var amount1 = pairs[0].LotFilter(info.QuantityPair1);

                                ConsoleWrite($"Place order [{info.NamePair1}/{info.PricePair1}/{amount1}]", ConsoleColor.Magenta);

                                var placeOrder1 = PlaceOrder(info.NamePair1, OrderSide.Buy, SpotOrderType.Limit, info.PricePair1, amount1, null);

                                if (!(placeOrder1 is null))
                                {
                                    ConsoleWrite($"Place order success [{info.NamePair1}/{placeOrder1.AverageFillPrice}/{placeOrder1.QuantityFilled}/{placeOrder1.QuoteQuantityFilled}]", ConsoleColor.Green);

                                    var amount2 = pairs[0].MarketLotFilter(pairs[0].LotFilter(placeOrder1.QuantityFilled.CalculateValue(0m)));

                                    ConsoleWrite($"Place order [{info.NamePair2}/{info.PricePair2}/{amount2}]", ConsoleColor.Magenta);

                                    var placeOrder2 = PlaceOrder(info.NamePair2, OrderSide.Sell, SpotOrderType.Market, null, amount2, null);

                                    while (placeOrder2 is null)
                                        placeOrder2 = PlaceOrder(info.NamePair2, OrderSide.Sell, SpotOrderType.Market, null, amount2, null);

                                    ConsoleWrite($"Place order success [{info.NamePair2}/{placeOrder2.AverageFillPrice}/{placeOrder2.QuantityFilled}/{placeOrder2.QuoteQuantityFilled}]", ConsoleColor.Green);

                                    var amount3 = pairs[2].MarketLotFilter(pairs[2].LotFilter(placeOrder2.QuoteQuantityFilled.CalculateValue(0m)));

                                    ConsoleWrite($"Place order [{info.NamePair3}/{info.PricePair3}/{amount3}]", ConsoleColor.Magenta);

                                    var placeOrder3 = PlaceOrder(info.NamePair3, OrderSide.Sell, SpotOrderType.Market, null, amount3, null);

                                    while (placeOrder3 is null)
                                        placeOrder3 = PlaceOrder(info.NamePair3, OrderSide.Sell, SpotOrderType.Market, null, amount3, null);

                                    ConsoleWrite($"Place order success [{info.NamePair3}/{placeOrder3.AverageFillPrice}/{placeOrder3.QuantityFilled}/{placeOrder3.QuoteQuantityFilled}]", ConsoleColor.Green);

                                    tradeInfo = new TradeInfo()
                                    {
                                        CalculateArbitrageInfo = info,
                                        ProcessArbitrageInfo = new ArbitrageInfo()
                                        {
                                            NamePair1 = info.NamePair1,
                                            NamePair2 = info.NamePair2,
                                            NamePair3 = info.NamePair3,
                                            PricePair1 = placeOrder1.AverageFillPrice.HasValue ? placeOrder1.AverageFillPrice.Value : 0m,
                                            PricePair2 = placeOrder2.AverageFillPrice.HasValue ? placeOrder2.AverageFillPrice.Value : 0m,
                                            PricePair3 = placeOrder3.AverageFillPrice.HasValue ? placeOrder3.AverageFillPrice.Value : 0m,
                                            QuantityPair1 = placeOrder1.QuantityFilled,
                                            QuantityPair2 = placeOrder2.QuoteQuantityFilled,
                                            QuantityPair3 = placeOrder3.QuoteQuantityFilled,
                                            ProcessMoneyValue = placeOrder1.QuoteQuantityFilled,
                                            ResultMoneyValue = placeOrder3.QuoteQuantityFilled,
                                            FoundDate = DateTime.Now
                                        },
                                        Date = DateTime.Now,
                                        ResultMoney = placeOrder3.QuoteQuantityFilled - placeOrder1.QuoteQuantityFilled
                                    };
                                }
                                else
                                    ConsoleWrite($"Place order failed. price changed [{info.NamePair1}]", ConsoleColor.Red);

                                break;
                            }
                        }
                    }
                }
                else
                {
                    tradeInfo = new TradeInfo()
                    {
                        CalculateArbitrageInfo = info,
                        Date = DateTime.Now,
                        ResultMoney = info.ProfitMoneyValue
                    };
                }

                if (!(tradeInfo is null))
                {
                    lock (_dataBase)
                    {
                        _dataBase.TradeInfos.Add(tradeInfo);
                        _dataBase.SaveChanges();
                    }

                    ConsoleWrite($"Trade complete [{info.NamePair1}->>{info.NamePair2}->>{info.NamePair3}][{info.RouteType}]({info.ProcessMoneyValue}) = {tradeInfo.ResultMoney} $", ConsoleColor.Green);
                }

            });
        }

        private BinanceOrder PlaceOrder(string symbol, OrderSide orderSide, SpotOrderType spotOrderType, decimal? price, decimal? quantity, decimal? quantityQty)
        {
            var placeHolder = _client.SpotApi.Trading.PlaceOrderAsync(symbol, orderSide, spotOrderType, price: price,
                quantity: quantity, quoteQuantity: quantityQty,
                timeInForce: spotOrderType == SpotOrderType.Limit ? TimeInForce.FillOrKill : null,
                orderResponseType: OrderResponseType.Full).Result;

            if (placeHolder.Success)
            {
                var checkOrder = _client.SpotApi.Trading.GetOrderAsync(symbol, placeHolder.Data.Id).Result;

                if (checkOrder.Success)
                {
                    if (checkOrder.Data.Status == OrderStatus.Filled)
                        return checkOrder.Data;
                }
                else
                {
                    _client.SpotApi.Trading.CancelAllOrdersAsync(symbol).Wait();

                    var Err = $"{symbol} Checkorder failed: [{checkOrder.Error}][price: {price} quantity: {quantity} quouteQuantity: {quantityQty}]";
                    Debug.WriteLine(Err);
                    ConsoleWrite(Err, ConsoleColor.Red);
                }
            }
            else
            {
                var Err = $"{symbol} Placeorder failed: [{placeHolder.Error}][price: {price} quantity: {quantity} quouteQuantity: {quantityQty}]";
                Debug.WriteLine(Err);
                ConsoleWrite(Err, ConsoleColor.Red);
            }

            return null;
        }

        public BinanceCommunication(Settings settingsParam)
        {
            _settings = settingsParam;
            _dataBase = new SqlContext();
            _tradeList = new List<(Pair[] Pairs, ArbitrageInfo GetArbitrage)>();

            if (!string.IsNullOrEmpty(settingsParam.ApiKey) && !string.IsNullOrEmpty(settingsParam.SecretKey))
            {
                var apiInfo = new ApiCredentials(settingsParam.ApiKey, settingsParam.SecretKey);

                _client = new BinanceClient(new BinanceClientOptions() { ApiCredentials = apiInfo });
                _socketclient = new BinanceSocketClient(new BinanceSocketClientOptions() { ApiCredentials = apiInfo });
                
                _consoleTitle = "[Normal Mode]";
                _balanceBaseCoin = 0m;
                _testMode = false;
            }
            else
            {
                _client = new BinanceClient();
                _socketclient = new BinanceSocketClient();
                
                _consoleTitle = "[Test Mode]";
                _balanceBaseCoin = 1000m;
                _testMode = true;
            }

        }

        public void Dispose()
        {
            if (!(_socketclient is null))
                _socketclient.UnsubscribeAllAsync().Wait();
        }

        public async Task<bool> Init()
        {
            _routeList = await ParsePairRoute();

            if (_routeList.Count == 0)
                return false;

            
            if (!_testMode)
            {
                /*var userData = await _client.SpotApi.Account.StartUserStreamAsync();

                if (!userData.Success)
                {
                    ConsoleWrite("Userdata request failed", ConsoleColor.Red);
                    return false;
                }

                ConsoleWrite($"Stream init success: {userData.Data}", ConsoleColor.DarkGreen);

                var userDataStream = await _socketclient.SpotStreams.SubscribeToUserDataUpdatesAsync(userData.Data, 
                    (DataEvent<BinanceStreamOrderUpdate> message) => 
                    {

                    },
                    (DataEvent<BinanceStreamOrderList> message) => 
                    {

                    },
                    (DataEvent<BinanceStreamPositionsUpdate> message) =>
                    {

                    },
                    (DataEvent<BinanceStreamBalanceUpdate> message) =>
                    {
                        if (message.Data.Asset.ToUpper().Equals(_settings.BaseCoin))
                            _balanceBaseCoin = message.Data.BalanceDelta;
                        
                        ConsoleWrite($"Balance Updates[{message.Data.Asset}] = {message.Data.BalanceDelta}", ConsoleColor.Cyan);
                    });


                if (!userDataStream)
                {
                    ConsoleWrite("Userdata stream failed", ConsoleColor.Red);
                    return false;
                }

                ConsoleWrite($"Stream start success: {userDataStream.Data.Id}", ConsoleColor.DarkGreen);
                */
                
                var info = await _client.SpotApi.Account.GetAccountInfoAsync();

                if (!info.Success)
                {
                    ConsoleWrite("Account info failed", ConsoleColor.Red);
                    return false;
                }

                var usdtBalance = info.Data.Balances.FirstOrDefault(x => x.Asset.ToUpper().Equals(_settings.BaseCoin));

                if (usdtBalance is null)
                {
                    ConsoleWrite($"{_settings.BaseCoin} balance info failed", ConsoleColor.Red);
                    return false;
                }

                _balanceBaseCoin = usdtBalance.Available;
            }

            var iAddedCount = 0;
            var iRouteCount = 0;

            var addedList = new List<string>();

            foreach (var route in _routeList)
            {
                var tempList = addedList.Skip(iAddedCount).Take(_settings.MaxCoinCount).ToList();

                if (tempList.Count >= _settings.MaxCoinCount)
                {
                    var socket = await _socketclient.SpotStreams.SubscribeToPartialOrderBookUpdatesAsync(tempList, _settings.MaxOrderBookCount, 100, PairBookList);
                    
                    if (!socket.Success)
                    {
                        ConsoleWrite($"Socket failed {socket.Error?.Message}", ConsoleColor.Red);
                        return false;
                    }

                    ConsoleWrite($"Socket succes: {socket.Data.SocketId}", ConsoleColor.DarkGreen);
                    iAddedCount += tempList.Count;
                }

                if (addedList.FindIndex(x => x.Equals(route.Pair1.GetPair)) == -1)
                    addedList.Add(route.Pair1.GetPair);

                if (addedList.FindIndex(x => x.Equals(route.Pair2.GetPair)) == -1)
                    addedList.Add(route.Pair2.GetPair);

                if (addedList.FindIndex(x => x.Equals(route.Pair3.GetPair)) == -1)
                    addedList.Add(route.Pair3.GetPair);

                route.OnArbitrageHandler += GetArbitrageHandler;
                iRouteCount++;

                ConsoleWrite($"{route.Pair1.GetPair}->>{route.Pair2.GetPair}->>{route.Pair3.GetPair}", ConsoleColor.Blue);
                Console.Title = $"{_consoleTitle} - Route[{iRouteCount}/{_routeList.Count}] Pair[{addedList.Count}] initialize";
            }

            var resultList = addedList.Skip(iAddedCount).ToList();

            if (resultList.Count > 0)
            {
                var socket = await _socketclient.SpotStreams.SubscribeToPartialOrderBookUpdatesAsync(resultList, _settings.MaxOrderBookCount, 100, PairBookList);

                if (!socket.Success)
                {
                    ConsoleWrite($"Socket failed {socket.Error?.Message}", ConsoleColor.Red);
                    return false;
                }

                ConsoleWrite($"Socket succes: {socket.Data.SocketId}", ConsoleColor.DarkGreen);
            }

            Console.Title = $"{_consoleTitle} - Route[{iRouteCount}/{_routeList.Count}] Pair[{addedList.Count}] success";
            ConsoleWrite("Wait arbitrage", ConsoleColor.DarkGreen);
            
            _isReady = true;
            return true;
        }
    }
}
