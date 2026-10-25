using Sentinel.Models;
using Sentinel.Services.Config;

namespace Sentinel.Tests;

public class ConfigTests
{
    public ConfigTests() => TestData.Init();

    private static OptimizationPlan Plan(int level, int scale = 100, int cap = 141)
    {
        var p = new OptimizationPlan { RenderScale = scale, FpsCap = cap };
        foreach (var c in Enum.GetValues<SettingCategory>()) p.Levels[c] = level;
        return p;
    }

    [Fact]
    public void Ini_round_trips_untouched_lines_and_adds_keys_to_the_right_section()
    {
        var file = Path.Combine(TestData.TempDir(), "a.ini");
        File.WriteAllText(file, "; comment\r\n[A]\r\nx=1\r\n\r\n[B]\r\ny=2\r\n");
        var ini = IniFile.Load(file);
        Assert.True(ini.Set("A", "x", "5", addIfMissing: false));
        Assert.True(ini.Set("A", "z", "9", addIfMissing: true));
        Assert.False(ini.Set("B", "missing", "1", addIfMissing: false));
        ini.Save(file);
        Assert.Equal("; comment\r\n[A]\r\nx=5\r\nz=9\r\n\r\n[B]\r\ny=2\r\n", File.ReadAllText(file));
    }

    // Regression: the first IniFile prepended a UTF-8 BOM to Fortnite's BOM-less GameUserSettings.ini.
    [Fact]
    public void Ini_without_bom_is_saved_without_bom_and_byte_identical()
    {
        var file = Path.Combine(TestData.TempDir(), "GameUserSettings.ini");
        var original = ";METADATA=(Diff=true, UseCommands=true)\r\n[ScalabilityGroups]\r\nsg.ShadowQuality=0\r\n"u8.ToArray();
        File.WriteAllBytes(file, original);

        IniFile.Load(file).Save(file);
        Assert.Equal(original, File.ReadAllBytes(file));

        var ini = IniFile.Load(file);
        ini.Set("ScalabilityGroups", "sg.ShadowQuality", "2", addIfMissing: false);
        ini.Save(file);
        var bytes = File.ReadAllBytes(file);
        Assert.Equal((byte)';', bytes[0]);
        Assert.Equal(";METADATA=(Diff=true, UseCommands=true)\r\n[ScalabilityGroups]\r\nsg.ShadowQuality=2\r\n", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Ini_keeps_an_existing_bom()
    {
        var file = Path.Combine(TestData.TempDir(), "bom.ini");
        byte[] original = [0xEF, 0xBB, 0xBF, .. "[A]\r\nx=1\r\n"u8.ToArray()];
        File.WriteAllBytes(file, original);
        IniFile.Load(file).Save(file);
        Assert.Equal(original, File.ReadAllBytes(file));
    }

    [Fact]
    public void Ini_keeps_lf_line_endings_and_a_missing_final_newline()
    {
        var file = Path.Combine(TestData.TempDir(), "lf.ini");
        File.WriteAllBytes(file, "[A]\nx=1\ny=2"u8.ToArray());
        var ini = IniFile.Load(file);
        ini.Set("A", "x", "5", addIfMissing: false);
        ini.Save(file);
        Assert.Equal("[A]\nx=5\ny=2", File.ReadAllText(file));
    }

    [Fact]
    public void Unreal_adapter_writes_scalability_frame_cap_and_vsync()
    {
        var install = TestData.TempDir();
        var cfgDir = Path.Combine(install, "MyProj", "Saved", "Config", "Windows");
        Directory.CreateDirectory(cfgDir);
        var file = Path.Combine(cfgDir, "GameUserSettings.ini");
        File.WriteAllText(file, "[ScalabilityGroups]\r\nsg.ResolutionQuality=100\r\nsg.ShadowQuality=3\r\n\r\n[/Script/Engine.GameUserSettings]\r\nbUseVSync=True\r\nFrameRateLimit=0.000000\r\n");

        var game = new InstalledGame { Name = "MyProj test", InstallPath = install, UnrealProject = "MyProj", Engine = "Unreal 5" };
        var adapter = new UnrealAdapter(game, null);
        Assert.Equal(file, adapter.Target);

        var result = adapter.Apply(Plan(QualityLevel.Medium, scale: 67));
        Assert.True(result.Success, result.Message);

        var ini = IniFile.Load(file);
        Assert.Equal("1", ini.Get("ScalabilityGroups", "sg.ShadowQuality"));
        Assert.Equal("1", ini.Get("ScalabilityGroups", "sg.GlobalIlluminationQuality"));
        Assert.Equal("67", ini.Get("ScalabilityGroups", "sg.ResolutionQuality"));
        Assert.Equal("141.000000", ini.Get("/Script/Engine.GameUserSettings", "FrameRateLimit"));
        Assert.Equal("False", ini.Get("/Script/Engine.GameUserSettings", "bUseVSync"));

        Assert.Contains("Restored", BackupService.RestoreLatest("myproj-test"));
        Assert.Equal("3", IniFile.Load(file).Get("ScalabilityGroups", "sg.ShadowQuality"));
    }

    [Fact]
    public void Valve_key_values_only_change_known_keys_and_keep_formatting()
    {
        var file = Path.Combine(TestData.TempDir(), "cs2_video.txt");
        File.WriteAllText(file, "\"video.cfg\"\n{\n\t\"setting.videocfg_shadow_quality\"\t\t\"3\"\n\t\"setting.unrelated\"\t\t\"7\"\n}\n");
        int changed = ProfileAdapter.ApplyKeyValue(file, new(StringComparer.OrdinalIgnoreCase) { ["setting.videocfg_shadow_quality"] = "1", ["setting.not_present"] = "0" });
        Assert.Equal(1, changed);
        var text = File.ReadAllText(file);
        Assert.Contains("\t\"setting.videocfg_shadow_quality\"\t\t\"1\"", text);
        Assert.Contains("\"setting.unrelated\"\t\t\"7\"", text);
        Assert.DoesNotContain("not_present", text);
    }

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private static string WriteBytes(string name, byte[] bytes)
    {
        var file = Path.Combine(TestData.TempDir(), name);
        File.WriteAllBytes(file, bytes);
        return file;
    }

    private static byte[] Utf8(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Key_values_keep_bom_mixed_line_endings_and_missing_final_newline()
    {
        var file = WriteBytes("videoconfig.txt", [.. Bom, .. Utf8("\"VideoConfig\"\r\n{\n\t\"setting.ssao_enabled\"\t\t\"1\"\r\n\t\"setting.other\"\t\t\"5\"\n}")]);
        Assert.Equal(1, ProfileAdapter.ApplyKeyValue(file, new() { ["setting.ssao_enabled"] = "0" }));
        Assert.Equal([.. Bom, .. Utf8("\"VideoConfig\"\r\n{\n\t\"setting.ssao_enabled\"\t\t\"0\"\r\n\t\"setting.other\"\t\t\"5\"\n}")], File.ReadAllBytes(file));
    }

    [Fact]
    public void Key_values_without_bom_stay_without_bom_and_unchanged_files_are_not_rewritten()
    {
        var original = Utf8("\"video.cfg\"\n{\n\t\"setting.mat_vsync\"\t\t\"0\"\n}\n");
        var file = WriteBytes("cs2_video.txt", original);
        var stamp = File.GetLastWriteTimeUtc(file);
        Assert.Equal(0, ProfileAdapter.ApplyKeyValue(file, new() { ["setting.mat_vsync"] = "0" }));
        Assert.Equal(original, File.ReadAllBytes(file));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public void Colon_pairs_keep_lf_endings_and_trailing_newline()
    {
        var file = WriteBytes("options.txt", Utf8("version:3953\nrenderDistance:32\nmaxFps:120\n"));
        Assert.Equal(1, ProfileAdapter.ApplyColon(file, new() { ["renderDistance"] = "12" }));
        Assert.Equal(Utf8("version:3953\nrenderDistance:12\nmaxFps:120\n"), File.ReadAllBytes(file));
    }

    [Fact]
    public void Xml_attribute_edits_touch_only_the_values()
    {
        const string before = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Settings>\r\n\t<!-- user settings -->\r\n\t<graphics>\r\n" +
                              "\t\t<TextureQuality value=\"2\"/>\r\n\t\t<MSAA   value='4' />\r\n\t\t<ShadowQuality other=\"x\" value=\"3\" />\r\n\t</graphics>\r\n</Settings>";
        const string after = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Settings>\r\n\t<!-- user settings -->\r\n\t<graphics>\r\n" +
                             "\t\t<TextureQuality value=\"1\"/>\r\n\t\t<MSAA   value='0' />\r\n\t\t<ShadowQuality other=\"x\" value=\"3\" />\r\n\t</graphics>\r\n</Settings>";
        var file = WriteBytes("settings.xml", [.. Bom, .. Utf8(before)]);
        Assert.Equal(2, ProfileAdapter.ApplyXml(file, new() { ["TextureQuality"] = "1", ["MSAA"] = "0", ["ShadowQuality"] = "3" }, named: false));
        Assert.Equal([.. Bom, .. Utf8(after)], File.ReadAllBytes(file));
    }

    [Fact]
    public void Xml_named_edits_do_not_add_a_declaration_or_bom()
    {
        const string before = "<roblox version=\"4\">\n  <Item class=\"UserGameSettings\">\n    <Properties>\n" +
                              "      <int name=\"GraphicsQualityLevel\">10</int>\n      <token name=\"SavedQualityLevel\">10</token>\n" +
                              "      <string name=\"Other\">keep &amp; me</string>\n    </Properties>\n  </Item>\n</roblox>\n";
        var file = WriteBytes("GlobalSettings_13.xml", Utf8(before));
        Assert.Equal(2, ProfileAdapter.ApplyXml(file, new() { ["GraphicsQualityLevel"] = "4", ["SavedQualityLevel"] = "4" }, named: true));
        Assert.Equal(Utf8(before.Replace(">10<", ">4<")), File.ReadAllBytes(file));
    }

    [Fact]
    public void Minecraft_options_use_colon_pairs()
    {
        var file = Path.Combine(TestData.TempDir(), "options.txt");
        File.WriteAllText(file, "version:3953\nrenderDistance:32\nmaxFps:120\n");
        Assert.Equal(2, ProfileAdapter.ApplyColon(file, new() { ["renderDistance"] = "12", ["maxFps"] = "144" }));
        Assert.Equal(["version:3953", "renderDistance:12", "maxFps:144"], File.ReadAllLines(file));
    }

    [Fact]
    public void Xml_attribute_and_named_formats_are_supported()
    {
        var dir = TestData.TempDir();
        var gta = Path.Combine(dir, "settings.xml");
        File.WriteAllText(gta, "<Settings><graphics><TextureQuality value=\"2\" /><MSAA value=\"4\" /></graphics></Settings>");
        Assert.Equal(1, ProfileAdapter.ApplyXml(gta, new() { ["TextureQuality"] = "1" }, named: false));
        Assert.Contains("<TextureQuality value=\"1\" />", File.ReadAllText(gta));

        var roblox = Path.Combine(dir, "GlobalSettings_13.xml");
        File.WriteAllText(roblox, "<roblox><Item><Properties><int name=\"GraphicsQualityLevel\">10</int><token name=\"SavedQualityLevel\">10</token></Properties></Item></roblox>");
        Assert.Equal(2, ProfileAdapter.ApplyXml(roblox, new() { ["GraphicsQualityLevel"] = "4", ["SavedQualityLevel"] = "4" }, named: true));
        Assert.Contains("<int name=\"GraphicsQualityLevel\">4</int>", File.ReadAllText(roblox));
    }
}
