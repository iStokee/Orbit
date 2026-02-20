using System;
using System.Windows;
using Velopack;

namespace Orbit;

internal static class Program
{
	[STAThread]
	private static void Main()
	{
		VelopackApp.Build().Run();

		var app = new App();
		app.InitializeComponent();
		app.Run();
	}
}
