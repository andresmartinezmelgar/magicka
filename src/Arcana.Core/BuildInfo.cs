using System.Reflection;

namespace Arcana.Core;

public static class BuildInfo
{
    public static string Version { get; } = ReadVersion();
    public static long UpdateBuildNumber { get; } = ReadUpdateBuildNumber();

    private static string ReadVersion() => typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? "development";
    private static long ReadUpdateBuildNumber() => long.TryParse(typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "ArcanaUpdateBuild")?.Value, out long build) && build > 0 ? build : 0;
}
