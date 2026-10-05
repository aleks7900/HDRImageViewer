using System;
using System.IO;
using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.WindowsTests;

public sealed class MrtResourceLoadingTests
{
    static MrtResourceLoadingTests()
    {
        TryInitializeBootstrap();
    }

    private static void TryInitializeBootstrap()
    {
        try
        {
            // Windows App SDK 2.x Major.Minor version: 0x00020002
            Microsoft.Windows.ApplicationModel.DynamicDependency.Bootstrap.TryInitialize(0x00020002, "", out _);
        }
        catch
        {
            try
            {
                // Fallback attempt for 1.6/any installed version
                Microsoft.Windows.ApplicationModel.DynamicDependency.Bootstrap.TryInitialize(0x00010006, "", out _);
            }
            catch
            {
                // Ignore if running inside package or already bootstrapped
            }
        }
    }

    private static string GetPriPath()
    {
        var baseDir = AppContext.BaseDirectory;
        return File.Exists(Path.Combine(baseDir, "resources.pri"))
            ? Path.Combine(baseDir, "resources.pri")
            : Path.Combine(baseDir, "HdrImageViewer.pri");
    }

    [Fact]
    public void PriResourceFileExistsInTestOutputDirectory()
    {
        var priPath = GetPriPath();
        Assert.True(File.Exists(priPath), $"PRI file not found in test output directory: {priPath}");
        var fileInfo = new FileInfo(priPath);
        Assert.True(fileInfo.Length > 100_000, $"PRI file seems too small ({fileInfo.Length} bytes), resources may be incomplete");
    }

    [Fact]
    public void SegmentedKeyResolvesThroughWindowsResourceLoaderAgainstPri()
    {
        // Must exercise Microsoft.Windows.ApplicationModel.Resources.ResourceLoader directly
        // against the actual generated Windows PRI resources.
        var priPath = GetPriPath();
        var loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader(priPath);

        // Normalize segmented key: "InspectorTabDetails.Text" -> "InspectorTabDetails/Text"
        var normalizedKey = Localization.NormalizeKeyForMrt("InspectorTabDetails.Text");
        Assert.Equal("InspectorTabDetails/Text", normalizedKey);

        var value = loader.GetString(normalizedKey);
        Assert.False(string.IsNullOrWhiteSpace(value), $"ResourceLoader failed to resolve {normalizedKey} from {priPath}");
    }

    [Fact]
    public void MissingResourceLookupDoesNotPoisonSubsequentLookups()
    {
        // 1. Resolve a known valid localized resource
        var firstValid = Localization.GetString("InspectorTabDetails.Text");
        Assert.False(string.IsNullOrWhiteSpace(firstValid));

        // 2. Attempt a missing / invalid resource
        var missingResult = Localization.GetString("NonExistentComponent.InvalidProperty999");
        Assert.Equal("NonExistentComponent.InvalidProperty999", missingResult);

        // 3. Resolve another known valid localized resource
        var secondValid = Localization.GetString("InspectorTabAnalysis.Text");
        Assert.False(string.IsNullOrWhiteSpace(secondValid));

        // 4. Confirm third call resolves successfully
        Assert.NotEqual("InspectorTabAnalysis.Text", secondValid);
    }

    [Fact]
    public void SystemDefaultClearsPrimaryLanguageOverrideTransition()
    {
        try
        {
            // Step 1: Explicit language selection (Russian)
            Localization.ApplyLanguagePreference("ru-RU");
            var activeOverride1 = Localization.GetAppliedLanguageOverride();
            Assert.Equal("ru-RU", activeOverride1);
            Assert.Equal("Сведения", Localization.GetString("InspectorTabDetails.Text"));

            // Step 2: Simulate restart with explicit language (Russian)
            Localization.ApplyLanguagePreference("ru-RU");
            var activeOverride2 = Localization.GetAppliedLanguageOverride();
            Assert.Equal("ru-RU", activeOverride2);
            Assert.Equal("Сведения", Localization.GetString("InspectorTabDetails.Text"));

            // Step 3: Explicit language selection (English)
            Localization.ApplyLanguagePreference("en-US");
            Assert.Equal("en-US", Localization.GetAppliedLanguageOverride());
            Assert.Equal("Details", Localization.GetString("InspectorTabDetails.Text"));

            // Step 4: Explicit language selection (Chinese)
            Localization.ApplyLanguagePreference("zh-CN");
            Assert.Equal("zh-CN", Localization.GetAppliedLanguageOverride());
            Assert.Equal("详情", Localization.GetString("InspectorTabDetails.Text"));

            // Step 5: User selects System default (empty string)
            Localization.ApplyLanguagePreference(string.Empty);
            var clearedOverride = Localization.GetAppliedLanguageOverride();
            Assert.Equal(string.Empty, clearedOverride);

            // Step 6: Simulate restart with empty language from persisted settings
            Localization.ApplyLanguagePreference(null);
            var finalOverride = Localization.GetAppliedLanguageOverride();
            Assert.Equal(string.Empty, finalOverride);
            Assert.NotEqual("ru-RU", finalOverride);

            // Step 7: Verify resolved text resolves after clearing override
            var clearedText = Localization.GetString("InspectorTabDetails.Text");
            Assert.False(string.IsNullOrWhiteSpace(clearedText));
        }
        finally
        {
            Localization.ApplyLanguagePreference(string.Empty);
        }
    }

    [Fact]
    public void PriResourcesContainSegmentedKeysAcrossMultipleLocales()
    {
        var priPath = GetPriPath();
        var manager = new Microsoft.Windows.ApplicationModel.Resources.ResourceManager(priPath);
        var resourceMap = manager.MainResourceMap.GetSubtree("Resources");
        var normalizedKey = Localization.NormalizeKeyForMrt("InspectorTabDetails.Text");
        Assert.Equal("InspectorTabDetails/Text", normalizedKey);

        // Test Russian candidate
        var ruContext = manager.CreateResourceContext();
        ruContext.QualifierValues["Language"] = "ru-RU";
        var ruCandidate = resourceMap.GetValue(normalizedKey, ruContext);
        Assert.NotNull(ruCandidate);
        Assert.Equal("Сведения", ruCandidate.ValueAsString);

        // Test Chinese candidate
        var zhContext = manager.CreateResourceContext();
        zhContext.QualifierValues["Language"] = "zh-CN";
        var zhCandidate = resourceMap.GetValue(normalizedKey, zhContext);
        Assert.NotNull(zhCandidate);
        Assert.Equal("详情", zhCandidate.ValueAsString);

        // Test English candidate
        var enContext = manager.CreateResourceContext();
        enContext.QualifierValues["Language"] = "en-US";
        var enCandidate = resourceMap.GetValue(normalizedKey, enContext);
        Assert.NotNull(enCandidate);
        Assert.Equal("Details", enCandidate.ValueAsString);

        // Test German candidate
        var deContext = manager.CreateResourceContext();
        deContext.QualifierValues["Language"] = "de-DE";
        var deCandidate = resourceMap.GetValue(normalizedKey, deContext);
        Assert.NotNull(deCandidate);
        Assert.Equal("Details", deCandidate.ValueAsString);

        // Test French candidate
        var frContext = manager.CreateResourceContext();
        frContext.QualifierValues["Language"] = "fr-FR";
        var frCandidate = resourceMap.GetValue(normalizedKey, frContext);
        Assert.NotNull(frCandidate);
        Assert.Equal("Détails", frCandidate.ValueAsString);
    }

    [Fact]
    public void NewlyAddedGainMapFormatKeysResolveThroughPri()
    {
        var priPath = GetPriPath();
        var manager = new Microsoft.Windows.ApplicationModel.Resources.ResourceManager(priPath);
        var resourceMap = manager.MainResourceMap.GetSubtree("Resources");

        // Test HEIC Gain Map key
        var enContext = manager.CreateResourceContext();
        enContext.QualifierValues["Language"] = "en-US";
        var enHeic = resourceMap.GetValue("BatchFormatGainMapHeic", enContext);
        Assert.NotNull(enHeic);
        Assert.Contains("HEIC", enHeic.ValueAsString);
        Assert.Contains("Gain Map", enHeic.ValueAsString);

        var ruContext = manager.CreateResourceContext();
        ruContext.QualifierValues["Language"] = "ru-RU";
        var ruHeic = resourceMap.GetValue("BatchFormatGainMapHeic", ruContext);
        Assert.NotNull(ruHeic);
        Assert.Contains("HEIC", ruHeic.ValueAsString);
        Assert.Contains("Gain Map", ruHeic.ValueAsString);

        // Test AVIF Gain Map key
        var enAvif = resourceMap.GetValue("BatchFormatGainMapAvif", enContext);
        Assert.NotNull(enAvif);
        Assert.Contains("AVIF", enAvif.ValueAsString);
        Assert.Contains("Gain Map", enAvif.ValueAsString);

        var zhContext = manager.CreateResourceContext();
        zhContext.QualifierValues["Language"] = "zh-CN";
        var zhAvif = resourceMap.GetValue("BatchFormatGainMapAvif", zhContext);
        Assert.NotNull(zhAvif);
        Assert.Contains("AVIF", zhAvif.ValueAsString);
        Assert.Contains("Gain Map", zhAvif.ValueAsString);

        // Test Crop flyout P3 gamut key
        var enCropP3 = resourceMap.GetValue("CropUltraHdrBaseGamutP3", enContext);
        Assert.NotNull(enCropP3);
        Assert.Equal("Base P3 · Auto", enCropP3.ValueAsString);

        var zhCropP3 = resourceMap.GetValue("CropUltraHdrBaseGamutP3", zhContext);
        Assert.NotNull(zhCropP3);
        Assert.Equal("Base P3 · 自动", zhCropP3.ValueAsString);

        // Test another segmented key normalized
        var aboutAppKey = Localization.NormalizeKeyForMrt("AboutAppName.Text");
        Assert.Equal("AboutAppName/Text", aboutAppKey);
        var zhAbout = resourceMap.GetValue(aboutAppKey, zhContext);
        Assert.NotNull(zhAbout);
        Assert.Equal("HDR 图片查看器", zhAbout.ValueAsString);
    }

    [Fact]
    public void AppSettingsLanguagePersistenceAndLifecycleTransition()
    {
        try
        {
            // 1. Initial / System default
            AppSettingsService.SetLanguage(string.Empty);
            Localization.ApplyLanguagePreference(AppSettingsService.Current.Language);
            Assert.Equal(string.Empty, Localization.GetAppliedLanguageOverride());

            // 2. Select English
            AppSettingsService.SetLanguage("en-US");
            Localization.ApplyLanguagePreference(AppSettingsService.Current.Language);
            Assert.Equal("en-US", Localization.GetAppliedLanguageOverride());
            Assert.Equal("Details", Localization.GetString("InspectorTabDetails.Text"));

            // 3. Select Russian
            AppSettingsService.SetLanguage("ru-RU");
            Localization.ApplyLanguagePreference(AppSettingsService.Current.Language);
            Assert.Equal("ru-RU", Localization.GetAppliedLanguageOverride());
            Assert.Equal("Сведения", Localization.GetString("InspectorTabDetails.Text"));

            // 4. Return to System default
            AppSettingsService.SetLanguage(string.Empty);
            Localization.ApplyLanguagePreference(AppSettingsService.Current.Language);
            Assert.Equal(string.Empty, Localization.GetAppliedLanguageOverride());
            Assert.NotEqual("ru-RU", Localization.GetAppliedLanguageOverride());
            Assert.NotEqual("en-US", Localization.GetAppliedLanguageOverride());
        }
        finally
        {
            AppSettingsService.SetLanguage(string.Empty);
            Localization.ApplyLanguagePreference(string.Empty);
        }
    }
}


