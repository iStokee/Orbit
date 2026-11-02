using System;
using System.IO;

namespace Orbit.Services;

internal static class AtomicFile
{
	internal static void WriteText(string path, string content)
	{
		path = Path.GetFullPath(path);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(temporary, content);
			if (File.Exists(path)) File.Replace(temporary, path, null);
			else File.Move(temporary, path);
		}
		finally { if (File.Exists(temporary)) File.Delete(temporary); }
	}
}
