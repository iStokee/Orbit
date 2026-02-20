using System.IO;
using System.Text;

namespace Orbit.Logging;

internal sealed class ConsoleRedirectWriter : TextWriter
{
	private readonly ConsoleLogService _logService;
	private readonly ConsoleLogSource _source;
	private readonly ConsoleLogLevel _level;
	private readonly TextWriter? _fallback;
	private readonly StringBuilder _buffer = new();
	private readonly object _sync = new();

	public ConsoleRedirectWriter(ConsoleLogService logService, ConsoleLogSource source, ConsoleLogLevel level, TextWriter? fallback)
	{
		_logService = logService;
		_source = source;
		_level = level;
		_fallback = fallback;
	}

	public override Encoding Encoding => Encoding.UTF8;

	public override void Write(char value)
	{
		lock (_sync)
		{
			if (value == '\r')
			{
				return;
			}

			if (value == '\n')
			{
				FlushBufferNoLock();
			}
			else
			{
				_buffer.Append(value);
			}
		}

		_fallback?.Write(value);
	}

	public override void Write(string? value)
	{
		if (value is null)
		{
			return;
		}

		lock (_sync)
		{
			AppendStringNoLock(value);
		}

		_fallback?.Write(value);
	}

	public override void WriteLine()
	{
		lock (_sync)
		{
			FlushBufferNoLock();
		}

		_fallback?.WriteLine();
	}

	public override void WriteLine(string? value)
	{
		if (value is null)
		{
			WriteLine();
			return;
		}

		lock (_sync)
		{
			AppendStringNoLock(value);
			FlushBufferNoLock();
		}

		_fallback?.WriteLine(value);
	}

	public override void Flush()
	{
		lock (_sync)
		{
			FlushBufferNoLock();
		}

		_fallback?.Flush();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Flush();
		}

		base.Dispose(disposing);
	}

	private void AppendStringNoLock(string value)
	{
		int start = 0;
		for (int i = 0; i < value.Length; i++)
		{
			if (value[i] == '\n')
			{
				if (i > start)
				{
					_buffer.Append(value, start, i - start);
				}
				FlushBufferNoLock();
				start = i + 1;
			}
		}

		if (start < value.Length)
		{
			_buffer.Append(value, start, value.Length - start);
		}
	}

	private void FlushBufferNoLock()
	{
		if (_buffer.Length == 0)
		{
			return;
		}

		var message = _buffer.ToString();
		_buffer.Clear();
		_logService.Append(message, _source, _level);
	}
}
