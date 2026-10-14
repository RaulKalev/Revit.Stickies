using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Stickies.Core.Persistence
{
    /// <summary>
    /// The one JSON format for every Stickies file: camelCase names, enums as camelCase strings ("inProgress"),
    /// dates as ISO 8601 with offset ("2026-10-14T09:30:12+03:00"), nulls written out, unknown properties ignored so
    /// a newer schema can still be read.
    /// </summary>
    public static class MarkupJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) },
            DateParseHandling = DateParseHandling.DateTimeOffset,
            DateFormatString = "yyyy-MM-ddTHH:mm:sszzz",
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Formatting = Formatting.Indented
        };

        public static string Serialize<T>(T value) => JsonConvert.SerializeObject(value, Settings);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
