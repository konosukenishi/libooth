using System.Text.Json;

namespace Libooth.Editor
{
    /// <summary>
    /// Keeps JSON member names in camel case while C# members use Pascal case.
    /// </summary>
    internal static class JsonSerialization
    {
        internal static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
