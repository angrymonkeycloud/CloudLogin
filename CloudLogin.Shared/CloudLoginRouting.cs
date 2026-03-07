using System.Reflection;

namespace AngryMonkey.CloudLogin;

public static class CloudLoginRouting
{
    public static readonly Assembly[] AdditionalAssemblies = [typeof(CloudLoginRouting).Assembly];
}
