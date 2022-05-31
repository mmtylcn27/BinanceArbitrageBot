using System;
using System.IO;
using System.Threading.Tasks;
using ArbitrageBot.Objects;
using Newtonsoft.Json;

namespace ArbitrageBot
{
    class Program
    {
        private static object _consoleLock = new();

        public static void ConsoleWrite(string message, ConsoleColor color)
        {
            lock (_consoleLock)
            {
                var oldColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"{DateTime.Now} - ");
                Console.ForegroundColor = color;
                Console.Write(message + Environment.NewLine);
                Console.ForegroundColor = oldColor;
            }
        }

        static async Task MainAsync()
        {
            Settings _settings;

            if (!File.Exists("set.json"))
            {
                _settings = new Settings()
                {
                    BaseCoin = "USDT",
                    BinanceFee = 0.001m,
                    MaxCoinCount = 100,
                    MaxOrderBookCount = 5,
                    MinTradeValue = 50m,
                    MaxTradeValue = 150m,
                    MinProfitValue = 1m
                };

                await File.WriteAllTextAsync("set.json", JsonConvert.SerializeObject(_settings, Formatting.Indented));
            }
            else
                _settings = JsonConvert.DeserializeObject<Settings>(await File.ReadAllTextAsync("set.json"));

            using (var binance = new BinanceCommunication(_settings))
            {
                if (await binance.Init())
                    await Task.Delay(-1);
            }
        }

        static void Main(string[] args)
        {
            MainAsync().Wait();
            Console.Read();
        }
    }
}
