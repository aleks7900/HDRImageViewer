using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace HdrImageViewer.Services;

// .NET permits one resolver per assembly. Share it between EXR and JPEG XL so
// both MSVC (jxl.dll) and MinGW (libjxl.dll) names bind to the actual loaded DLL.
internal static class NativeCodecLibraryResolver
{
    private static readonly ConcurrentDictionary<string, DllImportResolver> Resolvers = new(StringComparer.OrdinalIgnoreCase);

    static NativeCodecLibraryResolver()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeCodecLibraryResolver).Assembly,
            (name, assembly, path) => Resolvers.TryGetValue(name, out var resolver) ? resolver(name, assembly, path) : IntPtr.Zero);
    }

    public static void Register(string name, DllImportResolver resolver) => Resolvers[name] = resolver;
}
