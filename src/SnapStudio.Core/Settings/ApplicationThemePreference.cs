using System.Text.Json.Serialization;

namespace SnapStudio.Core.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<ApplicationThemePreference>))]
public enum ApplicationThemePreference
{
    Dark,
    Light,
    System
}
