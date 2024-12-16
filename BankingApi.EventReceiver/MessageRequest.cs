namespace BankingApi.EventReceiver
{
    public class MessageRequest
    {
        public Guid Id { get; set; }
        public MessageType MessageType { get; set; }
        public Guid BankAccountId { get; set; }
        public decimal Amount { get; set; }
    }

    public enum MessageType
    {
        Credit,
        Debit,
        Other
    }
}
