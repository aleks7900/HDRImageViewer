// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;

namespace HdrImageViewer.Services;

public static class Localization
{
#if WINDOWS
    private static readonly object s_loaderLock = new();
    private static Microsoft.Windows.ApplicationModel.Resources.ResourceLoader? s_resourceLoader;
    private static bool s_resourceLoaderInitializationFailed;
#endif

#if WINDOWS
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [System.Runtime.InteropServices.DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    private static IntPtr s_appSdkLanguageOverrideAddress;
    private static bool s_appSdkAddressResolved;

    private static IntPtr GetAppSdkLanguageOverrideAddress(IntPtr hModule)
    {
        if (s_appSdkAddressResolved)
        {
            return s_appSdkLanguageOverrideAddress;
        }

        try
        {
            // Fast path: inspect known RVA 0x16CF in Windows App SDK 2.x
            // Instruction: 48 8B 0D ?? ?? ?? ?? (mov rcx, [rip + disp32])
            if (System.Runtime.InteropServices.Marshal.ReadByte(hModule, 0x16CF) == 0x48 &&
                System.Runtime.InteropServices.Marshal.ReadByte(hModule, 0x16CF + 1) == 0x8B &&
                System.Runtime.InteropServices.Marshal.ReadByte(hModule, 0x16CF + 2) == 0x0D)
            {
                var disp = System.Runtime.InteropServices.Marshal.ReadInt32(hModule, 0x16CF + 3);
                s_appSdkLanguageOverrideAddress = IntPtr.Add(hModule, 0x16CF + 7 + disp);
                s_appSdkAddressResolved = true;
                return s_appSdkLanguageOverrideAddress;
            }

            // Fallback scan: scan .text section (0x1000..0x20000) for instruction sequence:
            // 48 8B CF FF 15 ?? ?? ?? ?? 48 89 7C 24 28 48 8B 0D
            for (var offset = 0x1000; offset < 0x20000; offset++)
            {
                if (System.Runtime.InteropServices.Marshal.ReadByte(hModule, offset) == 0x48 &&
                    System.Runtime.InteropServices.Marshal.ReadByte(hModule, offset + 1) == 0x8B &&
                    System.Runtime.InteropServices.Marshal.ReadByte(hModule, offset + 2) == 0xCF &&
                    System.Runtime.InteropServices.Marshal.ReadByte(hModule, offset + 3) == 0xFF &&
                    System.Runtime.InteropServices.Marshal.ReadByte(hModule, offset + 4) == 0x15 &&
                    System.Runtime.InteropServices.Marshal.ReadByte(hModule, offset + 9) == 0x48 &&
                    System.Runtime.InteropServices.Marshal.ReadByte(hModule, offset + 10) == 0x89 &&
                    System.Runtime.InteropServices.Marshal.ReadByte(hModule, offset + 11) == 0x7C &&
                    System.Runtime.InteropServices.Marshal.ReadByte(hModule, offset + 12) == 0x24 &&
                    System.Runtime.InteropServices.Marshal.ReadByte(hModule, offset + 14) == 0x48 &&
                    System.Runtime.InteropServices.Marshal.ReadByte(hModule, offset + 15) == 0x8B &&
                    System.Runtime.InteropServices.Marshal.ReadByte(hModule, offset + 16) == 0x0D)
                {
                    var movOffset = offset + 14;
                    var disp = System.Runtime.InteropServices.Marshal.ReadInt32(hModule, movOffset + 3);
                    s_appSdkLanguageOverrideAddress = IntPtr.Add(hModule, movOffset + 7 + disp);
                    s_appSdkAddressResolved = true;
                    return s_appSdkLanguageOverrideAddress;
                }
            }
        }
        catch
        {
            // Scanning failure is handled gracefully below
        }

        s_appSdkAddressResolved = true;
        return IntPtr.Zero;
    }

    private static void ClearWindowsAppSdkLanguageOverride()
    {
        try
        {
            var hModule = GetModuleHandle("Microsoft.Windows.ApplicationModel.Resources.dll");
            if (hModule == IntPtr.Zero)
            {
                return;
            }

            var targetAddress = GetAppSdkLanguageOverrideAddress(hModule);
            if (targetAddress != IntPtr.Zero)
            {
                var oldPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(targetAddress);
                if (oldPtr != IntPtr.Zero)
                {
                    _ = WindowsDeleteString(oldPtr);
                    System.Runtime.InteropServices.Marshal.WriteIntPtr(targetAddress, IntPtr.Zero);
                }
            }
        }
        catch
        {
            // Ignore if unpackaged memory clearing is unavailable
        }
    }
#endif

    public static string CurrentLanguage { get; private set; } = string.Empty;

    /// <summary>
    /// Applies a language preference override to the application.
    /// If <paramref name="language"/> is null, empty, or whitespace, the Windows App SDK
    /// and Windows PrimaryLanguageOverride are explicitly cleared, restoring the
    /// Windows system/user default language.
    /// </summary>
    public static void ApplyLanguagePreference(string? language)
    {
        var overrideValue = string.IsNullOrWhiteSpace(language) ? string.Empty : language.Trim();

#if WINDOWS
        try
        {
            if (string.IsNullOrEmpty(overrideValue))
            {
                ClearWindowsAppSdkLanguageOverride();
                try
                {
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = string.Empty;
                }
                catch
                {
                    // Ignore if packaged API is unavailable in unpackaged execution
                }
            }
            else
            {
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = overrideValue;
                try
                {
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = overrideValue;
                }
                catch
                {
                    // Ignore if packaged API is unavailable in unpackaged execution
                }
            }
        }
        catch
        {
            // Ignore if Windows App SDK globalization is unavailable in the current execution environment
        }

        ResetResourceLoader();
#endif

        CurrentLanguage = overrideValue;
    }

    /// <summary>
    /// Gets the currently applied Windows primary language override.
    /// Returns empty string if system default is active.
    /// </summary>
    public static string GetAppliedLanguageOverride()
    {
#if WINDOWS
        try
        {
            var sdkOverride = Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride;
            if (!string.IsNullOrEmpty(sdkOverride))
            {
                return sdkOverride;
            }
        }
        catch
        {
        }

        return CurrentLanguage;
#else
        return CurrentLanguage;
#endif
    }

#if WINDOWS
    /// <summary>
    /// Resets any cached ResourceLoader instance so subsequent lookups recreate it
    /// with the active ResourceContext and language settings.
    /// </summary>
    public static void ResetResourceLoader()
    {
        lock (s_loaderLock)
        {
            s_resourceLoader = null;
            s_resourceLoaderInitializationFailed = false;
        }
    }

#endif

    /// <summary>
    /// Normalizes a resource key for Windows MRT ResourceLoader.
    /// Segmented WinUI x:Uid property keys (such as "InspectorTabDetails.Text")
    /// are indexed in MRT PRI resources using slashes (e.g. "InspectorTabDetails/Text").
    /// </summary>
    public static string NormalizeKeyForMrt(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        var dotIndex = key.IndexOf('.');
        if (dotIndex < 0)
        {
            return key;
        }

        return key.Replace('.', '/');
    }

#if WINDOWS
    private static Microsoft.Windows.ApplicationModel.Resources.ResourceLoader? GetResourceLoader()
    {
        lock (s_loaderLock)
        {
            if (s_resourceLoaderInitializationFailed)
            {
                return null;
            }

            if (s_resourceLoader != null)
            {
                return s_resourceLoader;
            }

            try
            {
                s_resourceLoader = CreateResourceLoader();
                return s_resourceLoader;
            }
            catch
            {
                s_resourceLoaderInitializationFailed = true;
                return null;
            }
        }
    }

    private static Microsoft.Windows.ApplicationModel.Resources.ResourceLoader CreateResourceLoader()
    {
        try
        {
            return new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
        }
        catch
        {
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[] { "resources.pri", "HdrImageViewer.pri" };
            foreach (var candidate in candidates)
            {
                var priPath = System.IO.Path.Combine(baseDir, candidate);
                if (System.IO.File.Exists(priPath))
                {
                    try
                    {
                        return new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader(priPath);
                    }
                    catch
                    {
                        // Try next candidate
                    }
                }
            }

            throw;
        }
    }
#endif

    public static string GetString(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

#if WINDOWS
        var loader = GetResourceLoader();
        if (loader != null)
        {
            var mrtKey = NormalizeKeyForMrt(key);
            try
            {
                var resourceString = loader.GetString(mrtKey);
                if (!string.IsNullOrEmpty(resourceString))
                {
                    return resourceString;
                }
            }
            catch
            {
                // Individual lookup failed for normalized key (e.g. missing resource).
                // Do NOT mark the ResourceLoader as failed; continue to fallback.
            }

            if (!string.Equals(mrtKey, key, StringComparison.Ordinal))
            {
                try
                {
                    var directString = loader.GetString(key);
                    if (!string.IsNullOrEmpty(directString))
                    {
                        return directString;
                    }
                }
                catch
                {
                    // Individual lookup failed for direct key as well.
                }
            }
        }
#endif

        return FallbackResources.GetString(key);
    }

    public static string GetString(string key, params object[] args)
    {
        var format = GetString(key);
        if (string.IsNullOrEmpty(format))
        {
            return string.Empty;
        }

        if (args is null || args.Length == 0)
        {
            return format;
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }
}
