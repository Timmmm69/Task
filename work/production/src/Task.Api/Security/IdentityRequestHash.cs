using System.Security.Cryptography;
using System.Text.Json;

namespace Task.Api.Security;

internal static class IdentityRequestHash
{
    // Canonicalize in memory; persist only the digest, never password-bearing request JSON.
    public static byte[] Compute(HttpContext context, JsonElement? body)
    {
        var envelope=JsonSerializer.SerializeToElement(new {
            method=context.Request.Method, path=context.Request.Path.Value,
            ifMatch=context.Request.Headers.IfMatch.ToString(), body
        });
        using var stream=new MemoryStream();
        using(var writer=new Utf8JsonWriter(stream)) Write(writer,envelope);
        return SHA256.HashData(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer,JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach(var property in value.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); Write(writer,property.Value); }
            writer.WriteEndObject();
        }
        else if(value.ValueKind==JsonValueKind.Array)
        { writer.WriteStartArray(); foreach(var item in value.EnumerateArray())Write(writer,item); writer.WriteEndArray(); }
        else value.WriteTo(writer);
    }
}
