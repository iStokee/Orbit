using System.Windows;
using Orbit.Logging;
using Application = System.Windows.Application;

namespace Orbit;

public partial class App : Application
{
	private ConsolePipeServer? _pipeServer;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		ConsoleLogService.Instance.StartCapture();
		_pipeServer = new ConsolePipeServer();
		_pipeServer.Start();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_pipeServer?.Dispose();
		Settings.Default.Save();
		base.OnExit(e);
	}
}
