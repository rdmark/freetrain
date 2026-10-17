using System.Text.Json;
using System.Text.Json.Nodes;
using FreeTrain.Contracts;
using FreeTrain.Persistence;
using Xunit;

public class ContractTests
{
    public static IEnumerable<object[]> Fixtures() => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.json")
        .Select(path => new object[] { path });

    [Theory, MemberData(nameof(Fixtures))]
    public void Shared_fixtures_round_trip_without_loss(string path)
    {
        var source = File.ReadAllText(path);
        var options = WireJson.CreateOptions();
        var serialized = Path.GetFileName(path).StartsWith("save-", StringComparison.Ordinal)
            ? JsonSerializer.Serialize(JsonSerializer.Deserialize<SaveDocument>(source, SaveCodec.Json), SaveCodec.Json)
            : JsonSerializer.Serialize(JsonSerializer.Deserialize<WireMessage>(source, options), options);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(source), JsonNode.Parse(serialized)), path);
    }

    [Theory]
    [InlineData("9223372036854775807", long.MaxValue)]
    [InlineData("-9223372036854775808", long.MinValue)]
    [InlineData("9007199254740993", 9007199254740993L)]
    public void Int64_values_preserve_every_digit(string text, long expected)
    {
        var options = WireJson.CreateOptions();
        Assert.Equal(expected, JsonSerializer.Deserialize<long>($"\"{text}\"", options));
        Assert.Equal($"\"{text}\"", JsonSerializer.Serialize(expected, options));
    }

    [Theory]
    [InlineData("9007199254740993")]
    [InlineData("\"9223372036854775808\"")]
    [InlineData("\"01\"")]
    [InlineData("\"+1\"")]
    [InlineData("\"-0\"")]
    public void Noncanonical_or_out_of_range_numbers_are_rejected(string json)
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<long>(json, WireJson.CreateOptions()));
}
