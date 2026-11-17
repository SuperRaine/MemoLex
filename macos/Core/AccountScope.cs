namespace MemoLex.Services;

public sealed partial class MaiMemoClient
{
    internal IDisposable BindNativeAccount(string key)
    {
        ValidateAccount(key);
        return BeginAccountOperation();
    }
    internal void CheckNativeAccount(string key) => ValidateAccount(key);
}
