using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Orbit.Plugins;

internal sealed class PluginPackage : IDisposable
{
    internal string Folder { get; }
    internal string EntryPath { get; }
    private PluginPackage(string folder, string entry) { Folder = folder; EntryPath = entry; }

    internal static string[] Files(string folder)
    {
        var manifestPath = Path.Combine(folder, "orbit.plugin.json");
        if (File.Exists(manifestPath))
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (manifest.RootElement.TryGetProperty("files", out var files))
                return files.EnumerateArray().Select(value => ResolveFile(folder, value.GetString()!))
                    .Append(manifestPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        var extensions = new[] { ".dll", ".exe", ".json", ".config", ".resources", ".xaml", ".png", ".jpg", ".jpeg", ".svg", ".ico", ".dat", ".bin" };
        return Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !Path.GetRelativePath(folder, path).Split(Path.DirectorySeparatorChar).Any(part => part.Equals("logs", StringComparison.OrdinalIgnoreCase) || part.Equals("cache", StringComparison.OrdinalIgnoreCase) || part.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string ResolveFile(string folder, string relative)
    {
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new InvalidDataException("Plugin manifest names an unsafe or missing file.");
        for (var current = path; current != null && current.Length >= root.Length; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Plugin packages cannot traverse links.");
        return path;
    }

    internal static async Task<string> HashAsync(string entry)
    {
        var folder = Path.GetDirectoryName(entry)!;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Files(folder))
        {
            var name = Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, file));
            hash.AppendData(BitConverter.GetBytes(name.Length)); hash.AppendData(name);
            await using var stream = File.OpenRead(file);
            hash.AppendData(BitConverter.GetBytes(stream.Length));
            var buffer = new byte[81920]; int read;
            while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0) hash.AppendData(buffer.AsSpan(0, read));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static PluginPackage Stage(string entry)
    {
        var source = Path.GetDirectoryName(entry)!;
        var package = new PluginPackage(Path.Combine(Path.GetTempPath(), "OrbitPluginSnapshots", Guid.NewGuid().ToString("N")), Path.GetFileName(entry));
        try
        {
            foreach (var file in Files(source))
            {
                var destination = Path.Combine(package.Folder, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
            }
            if (!File.Exists(Path.Combine(package.Folder, package.EntryPath))) throw new InvalidDataException("Plugin manifest must include its entry assembly.");
            return package;
        }
        catch { package.Dispose(); throw; }
    }

    internal static bool IsEntryAssembly(string path)
    {
        try
        {
            using var stream = File.OpenRead(path); using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return false;
            var reader = pe.GetMetadataReader();
            return reader.TypeDefinitions.Any(handle => Implements(handle, 0));
            bool Implements(TypeDefinitionHandle handle, int depth)
            {
                if (depth > 32) return false;
                var type = reader.GetTypeDefinition(handle);
                foreach (var item in type.GetInterfaceImplementations())
                {
                    var target = reader.GetInterfaceImplementation(item).Interface;
                    if (target.Kind == HandleKind.TypeReference)
                    {
                        var reference = reader.GetTypeReference((TypeReferenceHandle)target);
                        if (reader.GetString(reference.Name) == "IOrbitPlugin" && reader.GetString(reference.Namespace) == "Orbit.Plugins") return true;
                    }
                }
                return !type.BaseType.IsNil && type.BaseType.Kind == HandleKind.TypeDefinition && Implements((TypeDefinitionHandle)type.BaseType, depth + 1);
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException) { return false; }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(Folder)) Directory.Delete(Folder, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
