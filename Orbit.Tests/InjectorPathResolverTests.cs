using System;
using System.IO;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class InjectorPathResolverTests
{
	private readonly InjectorPathResolver _resolver = new();

	[Fact]
	public void Resolve_UsesDefaultInjectorWhenConfiguredPathIsEmpty()
	{
		var dir = CreateTempDir();
		var expected = Path.Combine(dir, "XInput1_4_inject.dll");
		File.WriteAllText(expected, "dll");

		var resolved = _resolver.Resolve(dir, configuredPath: "", requireMesharpAssets: false);

		Assert.Equal(Path.GetFullPath(expected), resolved);
	}

	[Fact]
	public void Resolve_ResolvesRelativeConfiguredPathAgainstBaseDirectory()
	{
		var dir = CreateTempDir();
		var customDir = Path.Combine(dir, "custom");
		Directory.CreateDirectory(customDir);
		var expected = Path.Combine(customDir, "inject.dll");
		File.WriteAllText(expected, "dll");

		var resolved = _resolver.Resolve(dir, "custom/inject.dll", requireMesharpAssets: false);

		Assert.Equal(Path.GetFullPath(expected), resolved);
	}

	[Fact]
	public void Resolve_RequiresMesharpAssetsWhenEnabled()
	{
		var dir = CreateTempDir();
		var injector = Path.Combine(dir, "XInput1_4_inject.dll");
		File.WriteAllText(injector, "dll");

		Assert.Throws<FileNotFoundException>(() => _resolver.Resolve(dir, null, requireMesharpAssets: true));

		File.WriteAllText(Path.Combine(dir, "ME.runtimeconfig.json"), "{}");
		File.WriteAllText(Path.Combine(dir, "csharp_interop.dll"), "interop");

		var resolved = _resolver.Resolve(dir, null, requireMesharpAssets: true);
		Assert.Equal(Path.GetFullPath(injector), resolved);
	}

	private static string CreateTempDir()
	{
		var dir = Path.Combine(Path.GetTempPath(), $"OrbitInjectorPathTests_{Guid.NewGuid():N}");
		Directory.CreateDirectory(dir);
		return dir;
	}
}
