namespace MemoLex.Services;

internal sealed class AccountChangedException():OperationCanceledException("账号已切换，原账号操作已取消。");

public sealed partial class MaiMemoClient
{
    private readonly AsyncLocal<string?> _operationCredential=new();
    internal string? ExpectedCredentialKey {get;set;}
    private IDisposable BeginAccountOperation()
    {
        var prior=_operationCredential.Value;var current=_tokens.CredentialKey;
        if(current==null)throw new MaiMemoException("请在设置中登录墨墨账号。");
        ValidateAccount(prior??current);_operationCredential.Value=prior??current;
        return new AccountOperation(()=>_operationCredential.Value=prior);
    }
    private void ValidateAccount(string credential)
    {
        if(credential!=_tokens.CredentialKey||ExpectedCredentialKey is {} expected&&credential!=expected)throw new AccountChangedException();
    }
    private sealed class AccountOperation(Action restore):IDisposable
    {
        public void Dispose()=>restore();
    }
}
