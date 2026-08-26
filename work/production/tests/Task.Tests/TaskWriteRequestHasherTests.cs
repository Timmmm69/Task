using System.Text.Json;
using Task.Application;

namespace Task.Tests;

public sealed class TaskWriteRequestHasherTests
{
    [Fact]
    public void ComputeSha256_IgnoresObjectPropertyOrderRecursively()
    {
        var first = TaskWriteRequestHasher.ComputeSha256(
            """{"title":"Task","details":{"priority":"high","done":false},"tags":["a","b"]}""");
        var second = TaskWriteRequestHasher.ComputeSha256(
            """{"tags":["a","b"],"details":{"done":false,"priority":"high"},"title":"Task"}""");

        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeSha256_PreservesArrayOrder()
    {
        var first = TaskWriteRequestHasher.ComputeSha256("""{"tags":["a","b"]}""");
        var second = TaskWriteRequestHasher.ComputeSha256("""{"tags":["b","a"]}""");

        Assert.NotEqual(Convert.ToHexString(first), Convert.ToHexString(second));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("accessToken")]
    [InlineData("refresh_token")]
    [InlineData("connectionString")]
    [InlineData("cookie")]
    [InlineData("client-secret")]
    public void ComputeSha256_RejectsSensitiveFieldsAtAnyDepth(string fieldName)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["safe"] = new Dictionary<string, string> { [fieldName] = "must-not-persist" },
        });

        var exception = Assert.Throws<ArgumentException>(() => TaskWriteRequestHasher.ComputeSha256(json));

        Assert.DoesNotContain("must-not-persist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_DefensivelyCopiesRequestHashAndChangedFields()
    {
        var hash = Enumerable.Repeat((byte)7, 32).ToArray();
        string[] changedFields = ["title"];
        var command = new TaskWriteCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            actorSessionId: null,
            "POST_api_v1_tasks",
            Guid.NewGuid(),
            "create-key-0001",
            hash,
            Guid.NewGuid(),
            expectedVersion: null,
            "task.create",
            "TaskCreated",
            changedFields,
            """{"title":"Safe"}""",
            _ => throw new NotSupportedException());

        hash[0] = 99;
        changedFields[0] = "password";

        Assert.Equal(7, command.RequestHash[0]);
        Assert.Equal("title", command.ChangedFields[0]);
    }
}
