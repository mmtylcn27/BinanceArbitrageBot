using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArbitrageBot.Objects.Database.Objects
{
    public class TradeInfo
    {
        [Key]
        public long Id { get; set; }
        public DateTime Date { get; set; }
        public decimal ResultMoney { get; set; }
        public virtual ArbitrageInfo CalculateArbitrageInfo { get; set; }
        public virtual ArbitrageInfo ProcessArbitrageInfo { get; set; }
    }
}
