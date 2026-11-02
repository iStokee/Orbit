using System;
using System.IO;
using Orbit.Updater;
using Xunit;

namespace Orbit.Tests;

public sealed class UpdateTransactionTests
{
	[Fact]
	public void Apply_ReplacesApplicationAndUpdater()
	{
		var (source, target) = CreateFolders();
		File.WriteAllText(Path.Combine(source, "Orbit.Updater.exe"), "new updater");
		Assert.Equal(2, UpdateTransaction.Apply(source, target));
		Assert.Equal("new", File.ReadAllText(Path.Combine(target, "Orbit.exe")));
		Assert.Equal("new updater", File.ReadAllText(Path.Combine(target, "Orbit.Updater.exe")));
	}

	[Fact]
	public void Apply_RollsBackAlreadyReplacedFilesWhenLaterReplacementFails()
	{
		var (source, target) = CreateFolders();
		File.WriteAllText(Path.Combine(source, "z-locked.dll"), "new locked");
		File.WriteAllText(Path.Combine(source, "added.dll"), "new file");
		var locked = Path.Combine(target, "z-locked.dll");
		File.WriteAllText(locked, "old locked");
		using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			var error = Record.Exception(() => UpdateTransaction.Apply(source, target));
			Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
		}
		Assert.Equal("old", File.ReadAllText(Path.Combine(target, "Orbit.exe")));
		Assert.Equal("old locked", File.ReadAllText(locked));
		Assert.False(File.Exists(Path.Combine(target, "added.dll")));
	}

	private static (string Source, string Target) CreateFolders()
	{
		var root = Path.Combine(Path.GetTempPath(), "OrbitUpdateAudit-" + Guid.NewGuid().ToString("N"));
		var source = Path.Combine(root, "source");
		var target = Path.Combine(root, "target");
		Directory.CreateDirectory(source);
		Directory.CreateDirectory(target);
		File.WriteAllText(Path.Combine(source, "Orbit.exe"), "new");
		File.WriteAllText(Path.Combine(target, "Orbit.exe"), "old");
		return (source, target);
	}
}
