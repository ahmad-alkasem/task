using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Contracts;

public static class SyncJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static string Serialize(ProductSyncMessage message) =>
        JsonSerializer.Serialize(message, Options);

    public static ProductSyncMessage? Deserialize(string payload) =>
        JsonSerializer.Deserialize<ProductSyncMessage>(payload, Options);
}
