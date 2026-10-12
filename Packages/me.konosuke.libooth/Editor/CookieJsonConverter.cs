#nullable enable

using System;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Libooth.Editor
{
    /// <summary>Maps native cookie JSON and saved sessions to standard .NET cookies.</summary>
    internal sealed class CookieJsonConverter : JsonConverter<Cookie>
    {
        public override Cookie Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var value = document.RootElement;
            var cookie = new Cookie(value.GetProperty("name").GetString(), value.GetProperty("value").GetString(),
                value.GetProperty("path").GetString(), value.GetProperty("domain").GetString())
            {
                Secure = value.GetProperty("secure").GetBoolean(),
                HttpOnly = value.GetProperty("httpOnly").GetBoolean()
            };
            if (!value.GetProperty("session").GetBoolean())
                cookie.Expires = DateTimeOffset.FromUnixTimeSeconds(value.GetProperty("expires").GetInt64()).UtcDateTime;
            return cookie;
        }

        public override void Write(Utf8JsonWriter writer, Cookie cookie, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("name", cookie.Name);
            writer.WriteString("value", cookie.Value);
            writer.WriteString("domain", cookie.Domain);
            writer.WriteString("path", cookie.Path);
            writer.WriteBoolean("secure", cookie.Secure);
            writer.WriteBoolean("httpOnly", cookie.HttpOnly);
            var session = cookie.Expires == DateTime.MinValue;
            writer.WriteBoolean("session", session);
            writer.WriteNumber("expires", session ? 0 : new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds());
            writer.WriteEndObject();
        }
    }
}
