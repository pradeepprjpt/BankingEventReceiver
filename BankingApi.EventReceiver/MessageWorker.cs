using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Polly;

namespace BankingApi.EventReceiver
{
    public class MessageWorker
    {
        private readonly IServiceBusReceiver _serviceBusReceiver;
        private readonly IBankingRespository _bankingRespository;
        private readonly ILogger _logger; //This can be configured to store log into SUMO/Grafana or Kibana as per business requirements

        private const int PEEK_AWAIT_TIME_MILLI_SECOND = 10000;
        private const int MAX_RETRY_ATTEMPT = 3;
        private const int RETRY_TIMESPAN_SECONDS = 5;

        public MessageWorker(
            IServiceBusReceiver serviceBusReceiver,
            IBankingRespository bankingRespository,
            ILogger logger)
        {
            _serviceBusReceiver = serviceBusReceiver;
            _bankingRespository = bankingRespository;
            _logger = logger;
        }

        public async Task Start()
        {
            var peekedEventMessage = await _serviceBusReceiver.Peek().ConfigureAwait(false);
            if (peekedEventMessage is null)
            {
                await _serviceBusReceiver.Peek().WaitAsync(TimeSpan.FromMilliseconds(PEEK_AWAIT_TIME_MILLI_SECOND),
                    new CancellationToken()).ConfigureAwait(false);
            }

            MessageRequest messageRequest = await DeserializeMessageRequest(peekedEventMessage).ConfigureAwait(false);

            BankAccount accountDetails = null;

            await GetRetryPolicy()
                .ExecuteAsync(async () =>
                {
                    await GetAccountDetails(peekedEventMessage, messageRequest).ConfigureAwait(false);
                });

            if (accountDetails is null)
            {
                _logger.LogError($"Bank account does not exists for account number: {messageRequest.BankAccountId}");// This log can be stored on Secure Server 
                await Task.CompletedTask.ConfigureAwait(false);
            }

            await GetRetryPolicy()
                .ExecuteAsync(async () =>
                {
                    await UpdateAccountBalance(peekedEventMessage, messageRequest, accountDetails).ConfigureAwait(false);
                });

            await _serviceBusReceiver.Complete(peekedEventMessage).ConfigureAwait(false);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private async Task UpdateAccountBalance(EventMessage? peekedEventMessage, MessageRequest messageRequest, BankAccount accountDetails)
        {
            switch (messageRequest.MessageType)
            {
                case MessageType.Credit:
                    accountDetails.Balance += messageRequest.Amount;
                    break;

                case MessageType.Debit:
                    accountDetails.Balance -= messageRequest.Amount;
                    break;

                default:
                    await _serviceBusReceiver.MoveToDeadLetter(peekedEventMessage).ConfigureAwait(false);
                    await Task.CompletedTask.ConfigureAwait(false);
                    return;
            };

            try
            {
                await _bankingRespository.UpdateBalance(accountDetails).ConfigureAwait(false);
                _logger.LogInformation("Account balance updated successfully.");
            }
            catch (Exception exception)
            {
                _logger.LogError($"Error occurred while updating balance. Exception: {exception}");
                await _serviceBusReceiver.MoveToDeadLetter(peekedEventMessage).ConfigureAwait(false);
            }
        }

        private async Task<MessageRequest> DeserializeMessageRequest(EventMessage peekedEventMessage)
        {
            MessageRequest messageRequest = null;
            try
            {
                messageRequest = JsonConvert.DeserializeObject<MessageRequest>(peekedEventMessage.MessageBody);
                if (messageRequest is null)
                {
                    _logger.LogError($"Input is not in valid format. MessageBody:{peekedEventMessage.MessageBody}");
                    await _serviceBusReceiver.Abandon(peekedEventMessage).ConfigureAwait(false);
                    await Task.CompletedTask.ConfigureAwait(false);
                }

            }
            catch (Exception exception)
            {
                _logger.LogError($"Error occurred while deserializing message request. MessageBody:{peekedEventMessage.MessageBody}." +
                    $" Exception: {exception}");
                await _serviceBusReceiver.MoveToDeadLetter(peekedEventMessage).ConfigureAwait(false);
                await Task.CompletedTask.ConfigureAwait(false);
            }

            return messageRequest;
        }

        private async Task<BankAccount> GetAccountDetails(EventMessage? peekedEventMessage,
            MessageRequest messageRequest)
        {
            BankAccount accountDetails = null;
            try
            {
                accountDetails = await _bankingRespository.GetBalance(messageRequest.BankAccountId).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError($"Error occurred while retieving account details. Exception: {exception}");

                await _serviceBusReceiver.MoveToDeadLetter(peekedEventMessage).ConfigureAwait(false);
                await Task.CompletedTask.ConfigureAwait(false);
            }

            return accountDetails;
        }

        public static IAsyncPolicy GetRetryPolicy()
        {
            return Policy
                 .Handle<Exception>()
                 .WaitAndRetryAsync(MAX_RETRY_ATTEMPT,
                 retryAttempt => TimeSpan.FromSeconds(Math.Pow(RETRY_TIMESPAN_SECONDS, retryAttempt)));
        }
    }
}
