namespace MoneyFlow.DTOs
{
    public class HistoryTransactionDTO
    {
        public string Date { get; set; }
        public string Month { get; set; }
        public string TypeService { get; set; }
        public string Service { get; set; }
        //Declare attribute to display money in currency format with two decimal places
        public decimal Amount { get; set; }
    }
}
