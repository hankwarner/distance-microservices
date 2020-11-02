using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;


namespace DistanceMicroservices.Models
{
    public partial class GoogleDistanceMatrixAPIResponse
    {
        [JsonProperty("destination_addresses")]
        public string[] DestinationAddresses { get; set; }

        [JsonProperty("origin_addresses")]
        public string[] OriginAddresses { get; set; }

        [JsonProperty("rows")]
        public Row[] Rows { get; set; }

        [JsonProperty("status")]
        public Status Status { get; set; }
    }

    public partial class Row
    {
        [JsonProperty("elements")]
        public Element[] Elements { get; set; }
    }

    public partial class Element
    {
        [JsonProperty("distance")]
        public Distance Distance { get; set; }

        [JsonProperty("duration")]
        public Distance Duration { get; set; }

        [JsonProperty("status")]
        public Status Status { get; set; }
    }

    public partial class Distance
    {
        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("value")]
        public long Value { get; set; }
    }

    public enum Status
    {
        Ok,
        NotFound,
        ZeroResults
    };

    public partial class GoogleDistanceMatrixAPIResponse
    {
        public static GoogleDistanceMatrixAPIResponse FromJson(string json) => JsonConvert.DeserializeObject<GoogleDistanceMatrixAPIResponse>(json, Converter.Settings);
    }

    public static class Serialize
    {
        public static string ToJson(this GoogleDistanceMatrixAPIResponse self) => JsonConvert.SerializeObject(self, Converter.Settings);
    }

    internal static class Converter
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            Converters = {
                StatusConverter.Singleton,
                new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
            },
        };
    }

    internal class StatusConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(Status) || t == typeof(Status?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var value = serializer.Deserialize<string>(reader);
            if (value == "OK")
            {
                return Status.Ok;
            }
            else if (value == "NOT_FOUND")
            {
                return Status.NotFound;
            }
            else if (value == "ZERO_RESULTS")
            {
                return Status.ZeroResults;
            }
            throw new Exception("Cannot unmarshal type Status");
        }

        public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
        {
            if (untypedValue == null)
            {
                serializer.Serialize(writer, null);
                return;
            }
            var value = (Status)untypedValue;
            if (value == Status.Ok)
            {
                serializer.Serialize(writer, "OK");
                return;
            }
            throw new Exception("Cannot marshal type Status");
        }

        public static readonly StatusConverter Singleton = new StatusConverter();
    }
}
