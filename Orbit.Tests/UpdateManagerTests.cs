using System;
using System.IO;
using System.IO.Compression;
using Orbit.Services.Updates;
using Xunit;

namespace Orbit.Tests;

public sealed class UpdateManagerTests
{
	[Fact]
	public void ExtractUpdate_ExtractsSafeArchive()
	{
		var zipPath = CreateZip(("Orbit.exe", "binary"), ("docs/readme.txt", "docs"));
		var manager = new UpdateManager();

		var extracted = manager.ExtractUpdate(zipPath);

		Assert.True(File.Exists(Path.Combine(extracted, "Orbit.exe")));
		Assert.True(File.Exists(Path.Combine(extracted, "docs", "readme.txt")));
	}

	[Fact]
	public void ExtractUpdate_RejectsZipSlipPath()
	{
		var zipPath = CreateZip(("../outside.txt", "bad"));
		var manager = new UpdateManager();

		var ex = Assert.Throws<InvalidDataException>(() => manager.ExtractUpdate(zipPath));

		Assert.Contains("unsafe path", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ExtractUpdate_RejectsAbsolutePath()
	{
		var absoluteEntryName = OperatingSystem.IsWindows()
			? "C:/outside.txt"
			: "/tmp/outside.txt";
		var zipPath = CreateZip((absoluteEntryName, "bad"));
		var manager = new UpdateManager();

		var ex = Assert.Throws<InvalidDataException>(() => manager.ExtractUpdate(zipPath));

		Assert.Contains("unsafe path", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ExtractUpdate_RejectsWhitespaceEntryName()
	{
		var zipPath = Path.Combine(Path.GetTempPath(), $"OrbitUpdate_{Guid.NewGuid():N}.zip");
		using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
		{
			archive.CreateEntry(" ");
		}
		var manager = new UpdateManager();

		var ex = Assert.Throws<InvalidDataException>(() => manager.ExtractUpdate(zipPath));

		Assert.Contains("empty entry", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	private static string CreateZip(params (string EntryName, string Contents)[] entries)
	{
		var zipPath = Path.Combine(Path.GetTempPath(), $"OrbitUpdate_{Guid.NewGuid():N}.zip");
		using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
		foreach (var entry in entries)
		{
			var zipEntry = archive.CreateEntry(entry.EntryName);
			using var writer = new StreamWriter(zipEntry.Open());
			writer.Write(entry.Contents);
		}

		return zipPath;
	}
}
