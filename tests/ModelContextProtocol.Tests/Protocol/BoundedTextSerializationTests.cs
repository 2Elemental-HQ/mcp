using ModelContextProtocol.Protocol;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Checks segmented escaping against the framework's complete-string encoding, including boundary surrogates.
/// </summary>
public sealed class BoundedTextSerializationTests
{
    /// <summary>
    /// Splitting a string into writer segments preserves its JSON bytes and decoded content.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(65535)]
    public void EscapingMatchesCompleteString(int prefixLength)
    {
        var text = string.Concat(new string('x', prefixLength), "😀é\r\n\t\b\f\u0000\"\\<>&\ud800Z\udfff", new string('y', 17000));
        ContentBlock block = new TextContentBlock { Text = text };
        var json = JsonSerializer.Serialize(block, McpJsonUtilities.DefaultOptions);
        var expected = string.Concat("{\"type\":\"text\",\"text\":", JsonSerializer.Serialize(text, McpJsonUtilities.DefaultOptions), "}");
        Assert.Equal(expected, json);
        var decoded = JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions);
        Assert.Equal(Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text)), Assert.IsType<TextContentBlock>(decoded).Text);
    }

    /// <summary>
    /// Random UTF-16 strings retain framework escaping and decoding semantics.
    /// </summary>
    [Fact]
    public void RandomEscapesMatchFramework()
    {
        var random = new Random(73931);
        for (int iteration = 0; iteration < 25; iteration++)
        {
            var characters = new char[10000];
            for (int index = 0; index < characters.Length; index++) characters[index] = (char)random.Next(65536);
            var text = new string(characters);
            var encoded = JsonSerializer.Serialize(text, McpJsonUtilities.DefaultOptions);
            var json = string.Concat("{\"type\":\"text\",\"text\":", encoded, "}");
            var actual = Assert.IsType<TextContentBlock>(JsonSerializer.Deserialize<ContentBlock>(json, McpJsonUtilities.DefaultOptions));
            Assert.Equal(JsonSerializer.Deserialize<string>(encoded, McpJsonUtilities.DefaultOptions), actual.Text);
            Assert.Equal(json, JsonSerializer.Serialize<ContentBlock>(new TextContentBlock { Text = text }, McpJsonUtilities.DefaultOptions));
        }
    }

    /// <summary>
    /// The default base-message fast path preserves the original derived-type contract.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResponseContractIsPreserved(bool nullResult)
    {
        var response = new JsonRpcResponse { Id = new RequestId(42), Result = nullResult ? null : JsonNode.Parse("{\"value\":42}") };
        Assert.Equal(JsonSerializer.Serialize(response, McpJsonUtilities.DefaultOptions), JsonSerializer.Serialize<JsonRpcMessage>(response, McpJsonUtilities.DefaultOptions));
    }

    /// <summary>
    /// User-supplied response converters still control protocol serialization.
    /// </summary>
    [Fact]
    public void CustomResponseConverterIsPreserved()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        options.Converters.Add(new ResponseConverter());
        var response = new JsonRpcResponse { Result = null };
        Assert.Equal("42", JsonSerializer.Serialize<JsonRpcMessage>(response, options));
    }

    /// <summary>
    /// Supplies an observable custom contract for compatibility verification.
    /// </summary>
    private sealed class ResponseConverter : JsonConverter<JsonRpcResponse>
    {
        public override JsonRpcResponse Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, JsonRpcResponse value, JsonSerializerOptions options) => writer.WriteNumberValue(42);
    }

    /// <summary>
    /// Mutating the public result node invalidates the deferred result used by protocol serialization.
    /// </summary>
    [Fact]
    public void ResultMutationIsSerialized()
    {
        var response = Assert.IsType<JsonRpcResponse>(JsonSerializer.Deserialize<JsonRpcMessage>(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"value\":\"before\"}}", McpJsonUtilities.DefaultOptions));
        response.Result!["value"] = "after";
        var encoded = JsonSerializer.Serialize<JsonRpcMessage>(response, McpJsonUtilities.DefaultOptions);
        Assert.Contains("after", encoded);
        Assert.DoesNotContain("before", encoded);
    }
}
