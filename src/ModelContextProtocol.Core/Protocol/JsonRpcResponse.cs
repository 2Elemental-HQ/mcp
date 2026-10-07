using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a successful response message in the JSON-RPC protocol.
/// </summary>
/// <remarks>
/// <para>
/// Response messages are sent in reply to a request message and contain the result of the method execution.
/// Each response includes the same ID as the original request, allowing the sender to match responses
/// with their corresponding requests.
/// </para>
/// <para>
/// This class represents a successful response with a result. For error responses, see <see cref="JsonRpcError"/>.
/// </para>
/// </remarks>
public sealed class JsonRpcResponse : JsonRpcMessageWithId
{
    /// <summary>
    /// Gets or sets the result of the method invocation.
    /// </summary>
    /// <remarks>
    /// This property contains the result data returned by the server in response to the JSON-RPC method request.
    /// </remarks>
    [JsonPropertyName("result")]
    public required JsonNode? Result
    {
        get
        {
            if (_materialize is not null)
            {
                _result = _materialize();
                _materialize = null;
                _write = null;
                _utf8Result = null;
                _typedResult = null;
            }
            return _result;
        }
        set
        {
            _result = value;
            _materialize = null;
            _write = null;
            _utf8Result = null;
            _typedResult = null;
        }
    }

    private JsonNode? _result;
    private Func<JsonNode?>? _materialize;
    private Action<Utf8JsonWriter>? _write;
    private byte[]? _utf8Result;
    private object? _typedResult;

    // Preserve the mutable public node API, but only materialize it when a caller uses it.
    internal static JsonRpcResponse Create<T>(T result, JsonTypeInfo<T> typeInfo) => result is null ? new() { Result = null } : new()
    {
        Result = null,
        _typedResult = result,
        _materialize = () => JsonSerializer.SerializeToNode(result, typeInfo),
        _write = writer => JsonSerializer.Serialize(writer, result, typeInfo),
    };

    internal static JsonRpcResponse Create(byte[] result) => result.AsSpan().SequenceEqual("null"u8) ? new() { Result = null } : new()
    {
        Result = null,
        _utf8Result = result,
        _materialize = () => JsonSerializer.Deserialize(result, McpJsonUtilities.JsonContext.Default.JsonNode),
        _write = writer => writer.WriteRawValue(result),
    };

    internal bool HasResult => _materialize is not null || _result is not null;

    internal bool HasError => _typedResult is CallToolResult tool ? tool.IsError == true :
        _utf8Result is { } bytes ? HasErrorProperty(bytes) :
        Result is JsonObject obj && obj["isError"]?.GetValueKind() == JsonValueKind.True;

    private static bool HasErrorProperty(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;
        bool isError = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            bool matched = reader.ValueTextEquals("isError"u8);
            reader.Read();
            if (matched) isError = reader.TokenType == JsonTokenType.True;
            reader.Skip();
        }
        return isError;
    }

    internal T? DeserializeResult<T>(JsonTypeInfo<T> typeInfo) => _utf8Result is { } bytes ? JsonSerializer.Deserialize(bytes, typeInfo)
        : JsonSerializer.Deserialize(Result, typeInfo);

    internal void WriteResult(Utf8JsonWriter writer, JsonSerializerOptions options)
    {
        if (_write is not null) _write(writer);
        else JsonSerializer.Serialize(writer, _result, options.GetTypeInfo<JsonNode?>());
    }
}
