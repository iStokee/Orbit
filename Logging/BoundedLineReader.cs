using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Logging;

internal sealed class BoundedLineReader(TextReader reader)
{
	internal const int MaxLineLength = 16 * 1024;
	private readonly char[] _buffer = new char[4096];
	private int _position, _length;

	public async Task<string?> ReadLineAsync(CancellationToken token)
	{
		var line = new StringBuilder();
		while (true)
		{
			if (_position == _length)
			{
				_length = await reader.ReadAsync(_buffer.AsMemory(), token).ConfigureAwait(false);
				_position = 0;
				if (_length == 0) return line.Length == 0 ? null : line.ToString();
			}
			var value = _buffer[_position++];
			if (value == '\n') return line.ToString().TrimEnd('\r');
			if (line.Length == MaxLineLength)
				throw new InvalidDataException("Console log line exceeds the 16 KiB limit.");
			line.Append(value);
		}
	}
}
