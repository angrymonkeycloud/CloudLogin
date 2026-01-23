using AngryMonkey.CloudLogin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloudLogin.Shared.CloudLoginServices;

public interface ICloudLoginService
{
    public Task Login();
    public Task BeginLoginAsync(string? returnUrl);
    public Task<string> ProfileUrl();
    public Task Logout();
    public Task FetchUser();
    public Task<UserModel?> FetchUserByEmail(string emailAddress);
    public string? RequestId { get; }
    public UserModel? User { get; }

    public event Action<UserModel?>? UserChanged;
    // Raised when a new RequestId is received (e.g., from native URL callback)
    public event Action<string>? RequestIdChanged;

    // Allow sign-in page to set the RequestId when received via query/deep link
    public void SetRequestId(string? requestId);
}
