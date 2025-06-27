using System.Text.Json.Serialization;

namespace Models.LoggingModels;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4
}