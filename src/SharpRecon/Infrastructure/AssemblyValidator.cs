using System.Reflection.PortableExecutable;

namespace SharpRecon.Infrastructure;

internal static class AssemblyValidator
{
    private static readonly byte[] SingleFileBundleSignature =
    [
        0xd0, 0xe5, 0x2d, 0x67, 0xb9, 0xe2, 0x40, 0x0a,
        0x8e, 0x41, 0x86, 0xf9, 0xb5, 0x4d, 0x83, 0x52
    ];

    public static bool IsManagedAssembly(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            return peReader.PEHeaders.CorHeader is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsSingleFileBundle(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            const int searchSize = 4096;
            var length = stream.Length;
            var offset = Math.Max(0, length - searchSize);
            stream.Seek(offset, SeekOrigin.Begin);

            var buffer = new byte[length - offset];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);

            for (var i = 0; i <= bytesRead - SingleFileBundleSignature.Length; i++)
            {
                if (MatchesSignature(buffer, i))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsMixedMode(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            var corHeader = peReader.PEHeaders.CorHeader;
            return corHeader is not null && !corHeader.Flags.HasFlag(CorFlags.ILOnly);
        }
        catch
        {
            return false;
        }
    }

    public static string? ValidateForLoading(string path)
    {
        if (!File.Exists(path))
            return $"File not found: {path}";

        if (!IsManagedAssembly(path))
            return $"Not a managed .NET assembly: {Path.GetFileName(path)}";

        if (IsSingleFileBundle(path))
            return "Single-file published assemblies are not supported. Extract the bundle first.";

        return null;
    }

    private static bool MatchesSignature(byte[] buffer, int offset)
    {
        for (var j = 0; j < SingleFileBundleSignature.Length; j++)
        {
            if (buffer[offset + j] != SingleFileBundleSignature[j])
                return false;
        }
        return true;
    }
}
