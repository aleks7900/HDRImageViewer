using System;
using System.IO;
using System.Threading.Tasks;
using HdrImageViewer.Services;

namespace HdrImageViewer.IntegrationTests;

internal static class MrtLocalizationRegressionChecks
{
    public static Task RunAsync()
    {
        Console.WriteLine("=== Windows MRT / PRI Resource Loading Regression Checks ===");

        var baseDir = AppContext.BaseDirectory;
        var priPath = File.Exists(Path.Combine(baseDir, "resources.pri"))
            ? Path.Combine(baseDir, "resources.pri")
            : Path.Combine(baseDir, "HdrImageViewer.pri");

        if (!File.Exists(priPath))
        {
            throw new FileNotFoundException($"Windows PRI file not found in {baseDir}");
        }

        Console.WriteLine($"[1] Located PRI file: {priPath} ({new FileInfo(priPath).Length:N0} bytes)");

        // 1. Direct ResourceLoader with normalized segmented key
        var loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader(priPath);
        var normalizedKey = Localization.NormalizeKeyForMrt("InspectorTabDetails.Text");
        if (normalizedKey != "InspectorTabDetails/Text")
        {
            throw new InvalidOperationException($"Expected InspectorTabDetails/Text but got {normalizedKey}");
        }

        var segmentedValue = loader.GetString(normalizedKey);
        if (string.IsNullOrWhiteSpace(segmentedValue))
        {
            throw new InvalidOperationException($"ResourceLoader returned empty string for {normalizedKey}");
        }
        Console.WriteLine($"[2] Segmented key '{normalizedKey}' resolved via ResourceLoader: '{segmentedValue}'");

        // 2. Non-poisoning check
        var firstValid = Localization.GetString("InspectorTabDetails.Text");
        var missing = Localization.GetString("NonExistentComponent.FakeKey123");
        var secondValid = Localization.GetString("InspectorTabAnalysis.Text");

        if (string.IsNullOrWhiteSpace(firstValid) || string.IsNullOrWhiteSpace(secondValid) || secondValid == "InspectorTabAnalysis.Text")
        {
            throw new InvalidOperationException("Non-poisoning check failed: valid resource did not resolve after missing key");
        }
        Console.WriteLine("[3] Non-poisoning verification passed: missing key did not disable ResourceLoader");

        // 3. PrimaryLanguageOverride state transition
        Localization.ApplyLanguagePreference("ru-RU");
        if (Localization.GetAppliedLanguageOverride() != "ru-RU")
        {
            throw new InvalidOperationException("PrimaryLanguageOverride was not set to ru-RU");
        }

        // Simulate restart with explicit language
        Localization.ApplyLanguagePreference("ru-RU");
        if (Localization.GetAppliedLanguageOverride() != "ru-RU")
        {
            throw new InvalidOperationException("Simulated restart failed to retain ru-RU");
        }

        // Select System default
        Localization.ApplyLanguagePreference(string.Empty);
        if (Localization.GetAppliedLanguageOverride() != string.Empty)
        {
            throw new InvalidOperationException("PrimaryLanguageOverride was not cleared when switching to System default");
        }

        // Simulate restart with System default
        Localization.ApplyLanguagePreference(null);
        if (Localization.GetAppliedLanguageOverride() != string.Empty)
        {
            throw new InvalidOperationException("Simulated restart failed to keep System default cleared");
        }
        Console.WriteLine("[4] PrimaryLanguageOverride state transition verified: System default correctly cleared override");

        // 4. Multi-language resolution from PRI
        var manager = new Microsoft.Windows.ApplicationModel.Resources.ResourceManager(priPath);
        var resourceMap = manager.MainResourceMap.GetSubtree("Resources");

        var ruContext = manager.CreateResourceContext();
        ruContext.QualifierValues["Language"] = "ru-RU";
        var ruVal = resourceMap.GetValue("InspectorTabDetails/Text", ruContext).ValueAsString;

        var zhContext = manager.CreateResourceContext();
        zhContext.QualifierValues["Language"] = "zh-CN";
        var zhVal = resourceMap.GetValue("InspectorTabDetails/Text", zhContext).ValueAsString;

        var enContext = manager.CreateResourceContext();
        enContext.QualifierValues["Language"] = "en-US";
        var enVal = resourceMap.GetValue("InspectorTabDetails/Text", enContext).ValueAsString;

        if (ruVal != "Сведения" || zhVal != "详情" || enVal != "Details")
        {
            throw new InvalidOperationException($"Multi-language PRI lookup mismatch: ru='{ruVal}', zh='{zhVal}', en='{enVal}'");
        }
        Console.WriteLine($"[5] Multi-language PRI lookup verified: ru='{ruVal}', zh='{zhVal}', en='{enVal}'");

        // 5. Gain Map format strings
        var heicVal = resourceMap.GetValue("BatchFormatGainMapHeic", enContext).ValueAsString;
        var avifVal = resourceMap.GetValue("BatchFormatGainMapAvif", enContext).ValueAsString;
        var cropP3Val = resourceMap.GetValue("CropUltraHdrBaseGamutP3", enContext).ValueAsString;

        if (!heicVal.Contains("HEIC") || !avifVal.Contains("AVIF") || cropP3Val != "Base P3 · Auto")
        {
            throw new InvalidOperationException($"Gain Map format keys mismatch in PRI: heic='{heicVal}', avif='{avifVal}', cropP3='{cropP3Val}'");
        }
        Console.WriteLine($"[6] Gain Map keys verified in PRI: heic='{heicVal}', avif='{avifVal}', cropP3='{cropP3Val}'");

        Console.WriteLine("=== All Windows MRT / PRI Resource Checks Passed Successfully ===");
        return Task.CompletedTask;
    }
}
