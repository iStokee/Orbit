using System;
using System.IO;

namespace Orbit.Services;

public sealed class InjectorPathResolver
{
	private const string DefaultInjectorDll = "XInput1_4_inject.dll";

	public string Resolve(string baseDirectory, string? configuredPath, bool requireMesharpAssets)
	{
		if (string.IsNullOrWhiteSpace(baseDirectory))
		{
			throw new ArgumentException("Base directory is required.", nameof(baseDirectory));
		}

		var normalizedBase = Path.GetFullPath(baseDirectory);
		var normalizedConfigured = (configuredPath ?? string.Empty).Trim();
		var candidate = string.IsNullOrWhiteSpace(normalizedConfigured)
			? Path.GetFullPath(Path.Combine(normalizedBase, DefaultInjectorDll))
			: Path.GetFullPath(Path.IsPathRooted(normalizedConfigured)
				? normalizedConfigured
				: Path.Combine(normalizedBase, normalizedConfigured));

		if (File.Exists(candidate) && IsValidInjectorDirectory(Path.GetDirectoryName(candidate), requireMesharpAssets))
		{
			return candidate;
		}

		var directory = Path.GetDirectoryName(candidate) ?? normalizedBase;
		var sourceLabel = string.IsNullOrWhiteSpace(normalizedConfigured) ? "default" : "configured";
		var message = requireMesharpAssets
			? $"{sourceLabel} injector DLL must exist and be valid. Expected files: '{candidate}', '{Path.Combine(directory, "ME.runtimeconfig.json")}', '{Path.Combine(directory, "csharp_interop.dll")}'."
			: $"{sourceLabel} injector DLL must exist and be valid. Missing: '{candidate}'.";
		throw new FileNotFoundException(message, candidate);
	}

	private static bool IsValidInjectorDirectory(string? directory, bool requireMesharpAssets)
	{
		if (!requireMesharpAssets)
		{
			return !string.IsNullOrWhiteSpace(directory);
		}

		if (string.IsNullOrWhiteSpace(directory))
		{
			return false;
		}

		return File.Exists(Path.Combine(directory, "ME.runtimeconfig.json")) &&
			File.Exists(Path.Combine(directory, "csharp_interop.dll"));
	}
}
