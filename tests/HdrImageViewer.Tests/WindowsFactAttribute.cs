using Xunit;

namespace HdrImageViewer.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires Windows process and file sharing semantics.";
    }
}
