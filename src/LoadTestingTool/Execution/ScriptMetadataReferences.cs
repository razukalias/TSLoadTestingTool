using System.Reflection;
using System.Reflection.Metadata;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace LoadTestingTool.Execution;

internal static class ScriptMetadataReferences
{
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Assembly.Location is used only when available; single-file assemblies use TryGetRawMetadata below.")]
    public static MetadataReference Create(Assembly assembly)
    {
        var location = assembly.Location;
        if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
            return MetadataReference.CreateFromFile(location);

        unsafe
        {
            if (System.Reflection.Metadata.AssemblyExtensions.TryGetRawMetadata(assembly, out byte* blob, out var length) && length > 0)
                return MetadataReference.CreateFromImage(new ReadOnlySpan<byte>(blob, length).ToArray());
        }

        throw new NotSupportedException($"Cannot create a Roslyn metadata reference for assembly '{assembly.FullName}'. The assembly has no file location and raw metadata is unavailable.");
    }
}
