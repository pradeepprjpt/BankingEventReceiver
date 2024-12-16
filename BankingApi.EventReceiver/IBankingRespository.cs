namespace BankingApi.EventReceiver
{
    public interface IBankingRespository
    {
        Task<BankAccount> GetBalance(Guid id);
        Task UpdateBalance(BankAccount account);
    }
}
