using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ArbitrageBot.Objects.Exchange;

namespace ArbitrageBot.Objects.Database.Objects
{
    public class ArbitrageInfo
    {
        [Key]
        public long Id { get; set; }
        public DateTime FoundDate { get; set; }
        public string NamePair1 { get; set; }
        public string NamePair2 { get; set; }
        public string NamePair3 { get; set; }
        public decimal ProcessMoneyValue { get; set; }
        public decimal ResultMoneyValue { get; set; }
        public decimal ProfitMoneyValue { get; set; }
        public decimal FeePair1 { get; set; }
        public decimal FeePair2 { get; set; }
        public decimal FeePair3 { get; set; }
        public decimal QuantityPair1 { get; set; }
        public decimal QuantityPair2 { get; set; }
        public decimal QuantityPair3 { get; set; }
        public decimal PricePair1 { get; set; }
        public decimal PricePair2 { get; set; }
        public decimal PricePair3 { get; set; }
        [NotMapped]
        public Route.RouteType RouteType { get; set; }
    }
}
