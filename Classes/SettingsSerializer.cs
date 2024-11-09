using System.Collections.Generic;
using Newtonsoft.Json; // If using Newtonsoft.Json
					   // using System.Text.Json; // If using System.Text.Json

namespace Orbit.Classes
{
	public static class SettingsSerializer
	{
		// Serialize List<SerializableAccentColor> to JSON string
		public static string SerializeAccentColors(List<SerializableAccentColor> accents)
		{
			return JsonConvert.SerializeObject(accents, Formatting.Indented);
			// For System.Text.Json:
			// return JsonSerializer.Serialize(accents, new JsonSerializerOptions { WriteIndented = true });
		}

		// Deserialize JSON string to List<SerializableAccentColor>
		public static List<SerializableAccentColor> DeserializeAccentColors(string json)
		{
			return JsonConvert.DeserializeObject<List<SerializableAccentColor>>(json) ?? new List<SerializableAccentColor>();
			// For System.Text.Json:
			// return JsonSerializer.Deserialize<List<SerializableAccentColor>>(json) ?? new List<SerializableAccentColor>();
		}

		// Similarly for SerializableAppTheme
		public static string SerializeAppThemes(List<SerializableAppTheme> themes)
		{
			return JsonConvert.SerializeObject(themes, Formatting.Indented);
			// For System.Text.Json:
			// return JsonSerializer.Serialize(themes, new JsonSerializerOptions { WriteIndented = true });
		}

		public static List<SerializableAppTheme> DeserializeAppThemes(string json)
		{
			return JsonConvert.DeserializeObject<List<SerializableAppTheme>>(json) ?? new List<SerializableAppTheme>();
			// For System.Text.Json:
			// return JsonSerializer.Deserialize<List<SerializableAppTheme>>(json) ?? new List<SerializableAppTheme>();
		}
	}
}
