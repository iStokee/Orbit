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

	[Theory]
	[InlineData("https://github.com/iStokee/Orbit/releases/download/orbit%2Fv1.0.5/orbit-win-x64.zip")]
	[InlineData("https://github.com/iStokee/Orbit/releases/download/orbit%2Fv1.0.5/orbit-win-x64.zip.sha256")]
	public void ValidateAssetDownloadUrl_AcceptsProjectReleaseAssets(string url)
	{
		UpdateManager.ValidateAssetDownloadUrl(url);
	}

	[Theory]
	[InlineData("http://github.com/iStokee/Orbit/releases/download/v1/orbit-win-x64.zip", "https")]
	[InlineData("https://evil.example.com/iStokee/Orbit/releases/download/v1/orbit-win-x64.zip", "github.com")]
	[InlineData("https://github.com/attacker/Orbit/releases/download/v1/orbit-win-x64.zip", "release assets")]
	[InlineData("https://github.com/iStokee/OtherRepo/releases/download/v1/orbit-win-x64.zip", "release assets")]
	[InlineData("https://github.com/iStokee/Orbit/archive/refs/heads/main.zip", "release assets")]
	[InlineData("not a url", "absolute URL")]
	public void ValidateAssetDownloadUrl_RejectsForeignUrls(string url, string expectedMessagePart)
	{
		var ex = Assert.Throws<InvalidDataException>(() => UpdateManager.ValidateAssetDownloadUrl(url));

		Assert.Contains(expectedMessagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
	[InlineData("9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08  orbit-win-x64.zip")]
	[InlineData("\n 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08 *orbit-win-x64.zip\n")]
	public void ParseSha256_AcceptsSha256SumFormats(string content)
	{
		var hash = UpdateManager.ParseSha256(content);

		Assert.Equal("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", hash, ignoreCase: true);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("not-a-hash orbit-win-x64.zip")]
	[InlineData("9f86d081884c7d659a2feaa0c55ad015")]
	[InlineData("zz86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
	public void ParseSha256_RejectsInvalidContent(string content)
	{
		Assert.Throws<InvalidDataException>(() => UpdateManager.ParseSha256(content));
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
