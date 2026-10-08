using AngryMonkey.CloudLogin.Server.Core.Application;
using AngryMonkey.CloudLogin.Server.Core.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace AngryMonkey.CloudLogin.Server;

public partial class CloudLoginServer
{
    private AuthorizationTransactionService? Transactions => _accessor.HttpContext?.RequestServices.GetService<AuthorizationTransactionService>();

    private async Task<bool> IsAllowedSignInRedirectAsync(string? target)
    {
        if (AuthorizationTransactionService.TryParseReference(target, out string transactionId))
            return Transactions is { } transactions && await transactions.IsPendingAsync(transactionId, LoginRequestKinds.Authorization);

        if (string.IsNullOrWhiteSpace(target) || IsRelativePath(target) || CloudLoginShared.IsSameOrigin(target, LoginUrl))
            return IsAllowedRedirect(target);

        return _configuration.AllowLegacyRedirectHandoff && IsAllowedRedirect(target);
    }

    private async Task<AuthorizationTransaction?> ConsumeSignInTransactionAsync(string? referer)
    {
        if (!AuthorizationTransactionService.TryParseReference(referer, out string transactionId))
            return null;

        AuthorizationTransaction? transaction = Transactions is null ? null : await Transactions.ConsumeAsync(transactionId, LoginRequestKinds.Authorization);

        return transaction ?? throw new UnauthorizedAccessException("This sign-in request has expired or was already used. Start again from the application.");
    }

    private async Task<string> CompleteTransactionAsync(Guid userId, AuthorizationTransaction transaction)
    {
        AuthorizationTransactionService transactions = Transactions ?? throw new InvalidOperationException("Authorization transactions are not available.");

        Guid code = await CreateLoginRequest(userId);
        await transactions.BindLoginRequestAsync(code, transaction);

        string destination = CloudLoginShared.AppendQueryParameter(transaction.ReturnUrl, "code", code.ToString());
        return string.IsNullOrEmpty(transaction.State) ? destination : CloudLoginShared.AppendQueryParameter(destination, "state", transaction.State);
    }
}
