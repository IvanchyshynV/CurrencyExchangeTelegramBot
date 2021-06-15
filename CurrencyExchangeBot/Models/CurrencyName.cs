using System.Collections.Generic;
using System.Linq;

namespace CurrencyExchangeBot.Models
{
    public class CurrencyName
    {
        public string Currency_name { get; set; }
        public List<string> Countries { get; set; }

        public override string ToString()
        {
            return $"Назва: {Currency_name}, Країни: {Countries.Aggregate((i, j) => i + ", " + j)}";
        }
    }
}
