using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orbit.Classes
{
    class SerializableClasses
    {
    }

	[Serializable]
	public class SerializableAccentColor
	{
		public string Name { get; set; }
		public string ColorHex { get; set; }

		public SerializableAccentColor() { }

		public SerializableAccentColor(string name, string colorHex)
		{
			Name = name;
			ColorHex = colorHex;
		}
	}

	[Serializable]
	public class SerializableAppTheme
	{
		public string Name { get; set; }
		public string BackgroundColorHex { get; set; }
		public string HighlightColorHex { get; set; }

		public SerializableAppTheme() { }

		public SerializableAppTheme(string name, string backgroundColorHex, string highlightColorHex)
		{
			Name = name;
			BackgroundColorHex = backgroundColorHex;
			HighlightColorHex = highlightColorHex;
		}
	}
}
