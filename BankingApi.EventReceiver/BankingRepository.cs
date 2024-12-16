using Microsoft.EntityFrameworkCore;

namespace BankingApi.EventReceiver
{
    public class BankingRepository : IBankingRespository
    {
        private readonly BankingApiDbContext _bankingApiDbContext;

        public BankingRepository(BankingApiDbContext bankingApiDbContext)
        {
            _bankingApiDbContext = bankingApiDbContext;
        }

        public async Task<BankAccount> GetBalance(Guid id)
        {
            var bankAccount = await _bankingApiDbContext.BankAccounts.FindAsync(id)
                .ConfigureAwait(false);

            return bankAccount;
        }

        public async Task UpdateBalance(BankAccount account)
        {
            ArgumentNullException.ThrowIfNull(account);

            _bankingApiDbContext.BankAccounts.Update(account);
            await _bankingApiDbContext.SaveChangesAsync();
        }
    }
}
