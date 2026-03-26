using System.Reflection;

namespace AngryMonkey.CloudLogin;

public static class CloudLoginRouting
{
    public static readonly Type AssemblyType = typeof(CloudLoginRouting);
    public static readonly Assembly Assembly = typeof(CloudLoginRouting).Assembly;
    public static readonly Assembly[] AdditionalAssemblies = [typeof(CloudLoginRouting).Assembly];
}
