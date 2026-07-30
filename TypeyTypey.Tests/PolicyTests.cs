using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using Xunit;

namespace TypeyTypey.Tests;

public sealed class AppSettingsNormalizeTests
{
    [Theory]
    [InlineData(-50, 0)]
    [InlineData(0, 0)]
    [InlineData(15, 15)]
    [InlineData(5_000, 1_000)]
    public void Normalize_ClampsCharacterDelay(int input, int expected)
    {
        var settings = new AppSettings { CharacterDelayMs = input };
        settings.Normalize();
        Assert.Equal(expected, settings.CharacterDelayMs);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(500, 500)]
    [InlineData(99_999, 10_000)]
    public void Normalize_ClampsInitialDelay(int input, int expected)
    {
        var settings = new AppSettings { InitialDelayMs = input };
        settings.Normalize();
        Assert.Equal(expected, settings.InitialDelayMs);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(50, 50)]
    [InlineData(10_000, 500)]
    public void Normalize_ClampsMaximumHistoryEntries(int input, int expected)
    {
        var settings = new AppSettings { MaximumHistoryEntries = input };
        settings.Normalize();
        Assert.Equal(expected, settings.MaximumHistoryEntries);
    }

    [Fact]
    public void Normalize_ReplacesMissingHotkeyBindings()
    {
        var settings = new AppSettings { TypeClipboardHotkey = null!, HistoryHotkey = null! };
        settings.Normalize();

        Assert.NotNull(settings.TypeClipboardHotkey);
        Assert.NotNull(settings.HistoryHotkey);
        Assert.True(settings.HistoryHotkey.Shift);
    }
}

public sealed class AppSettingsThemeTests
{
    [Fact]
    public void NewSettings_DefaultToSystemTheme()
    {
        Assert.Equal(AppTheme.System, new AppSettings().Theme);
    }

    [Fact]
    public void SettingsFileWithoutTheme_LoadsAsSystem()
    {
        // Settings written by v1.0.3 have no Theme property at all.
        const string legacy = """{"CharacterDelayMs":25,"InitialDelayMs":400,"MaximumHistoryEntries":75}""";

        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(legacy);

        Assert.NotNull(settings);
        settings.Normalize();
        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Equal(25, settings.CharacterDelayMs);
        Assert.Equal(75, settings.MaximumHistoryEntries);
    }

    // AppTheme is internal, so the theory carries the underlying integer rather than the enum.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ValidThemeValues_SurviveNormalize(int stored)
    {
        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>($$"""{"Theme":{{stored}}}""");

        Assert.NotNull(settings);
        settings.Normalize();
        Assert.Equal(stored, (int)settings.Theme);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-3)]
    public void UndefinedThemeValue_NormalizesToSystem(int stored)
    {
        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>($$"""{"Theme":{{stored}}}""");

        Assert.NotNull(settings);
        settings.Normalize();
        Assert.Equal(AppTheme.System, settings.Theme);
    }

    [Fact]
    public void Theme_SurvivesSerializationRoundTrip()
    {
        var original = new AppSettings { Theme = AppTheme.Dark, CharacterDelayMs = 42 };

        AppSettings? restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(original));

        Assert.NotNull(restored);
        restored.Normalize();
        Assert.Equal(AppTheme.Dark, restored.Theme);
        Assert.Equal(42, restored.CharacterDelayMs);
    }

    [Fact]
    public void Theme_IsPersistedNumerically()
    {
        string json = JsonSerializer.Serialize(new AppSettings { Theme = AppTheme.Dark });

        // Display strings must never become the persisted value.
        Assert.Contains("\"Theme\":2", json);
        Assert.DoesNotContain("Dark", json);
    }
}

public sealed class HotkeyBindingTests
{
    [Fact]
    public void Binding_WithoutModifiers_IsInvalid()
    {
        var binding = new HotkeyBinding { Ctrl = false, Alt = false, Shift = false, Win = false, Key = Keys.V };
        Assert.False(binding.IsValid);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void AnySingleModifier_MakesBindingValid(bool ctrl, bool alt, bool shift, bool win)
    {
        var binding = new HotkeyBinding { Ctrl = ctrl, Alt = alt, Shift = shift, Win = win, Key = Keys.V };
        Assert.True(binding.IsValid);
    }

    [Fact]
    public void IdenticalBindings_AreSame()
    {
        var left = new HotkeyBinding { Ctrl = true, Alt = true, Key = Keys.V };
        var right = new HotkeyBinding { Ctrl = true, Alt = true, Key = Keys.V };
        Assert.True(left.IsSameAs(right));
    }

    [Fact]
    public void DifferentKey_IsNotSame()
    {
        var left = new HotkeyBinding { Ctrl = true, Alt = true, Key = Keys.V };
        var right = new HotkeyBinding { Ctrl = true, Alt = true, Key = Keys.B };
        Assert.False(left.IsSameAs(right));
    }

    [Fact]
    public void DifferentModifiers_AreNotSame()
    {
        var left = new HotkeyBinding { Ctrl = true, Alt = true, Shift = false, Key = Keys.V };
        var right = new HotkeyBinding { Ctrl = true, Alt = true, Shift = true, Key = Keys.V };
        Assert.False(left.IsSameAs(right));
    }

    [Fact]
    public void NullBinding_IsNotSame()
    {
        Assert.False(new HotkeyBinding().IsSameAs(null));
    }

    [Fact]
    public void DefaultBindings_DifferAndAreBothValid()
    {
        var settings = new AppSettings();
        Assert.True(settings.TypeClipboardHotkey.IsValid);
        Assert.True(settings.HistoryHotkey.IsValid);
        Assert.False(settings.TypeClipboardHotkey.IsSameAs(settings.HistoryHotkey));
    }
}

public sealed class AdminCommandTests
{
    [Fact]
    public void AdminFlag_ParsesAsElevateCommand()
    {
        Assert.True(CommandLine.TryParse(["--admin"], out AppCommand command));
        Assert.Equal(AppCommand.Elevate, command);
    }

    [Fact]
    public void AdminFlag_IsCaseInsensitive()
    {
        Assert.True(CommandLine.TryParse(["--ADMIN"], out AppCommand command));
        Assert.Equal(AppCommand.Elevate, command);
    }

    [Fact]
    public void AdminFlag_CannotBeCombinedWithAnotherCommand()
    {
        Assert.False(CommandLine.TryParse(["--admin", "--history"], out _));
    }

    [Fact]
    public void AdminFlag_SurvivesTheElevationRelayAsACommandName()
    {
        // The relay transmits the enum name over the pipe, so it must round-trip.
        Assert.True(Enum.TryParse(AppCommand.Elevate.ToString(), ignoreCase: true, out AppCommand parsed));
        Assert.Equal(AppCommand.Elevate, parsed);
    }

    [Fact]
    public void ElevatedRestartMarker_IsDistinctFromTheAdminFlag()
    {
        Assert.NotEqual(CommandLine.AdminArgument, CommandLine.ElevatedRestartArgument);
        Assert.True(CommandLine.TryParse([CommandLine.AdminArgument, CommandLine.ElevatedRestartArgument],
            out AppCommand command, out bool elevatedRestart));
        Assert.Equal(AppCommand.Elevate, command);
        Assert.True(elevatedRestart);
    }
}

public sealed class VersionInfoTests
{
    [Theory]
    [InlineData("1.0.4.0", "1.0.4")]
    [InlineData("1.0.4", "1.0.4")]
    [InlineData("1.0.4+9a1b2c3d", "1.0.4")]
    [InlineData("1.0.4.0+9a1b2c3d", "1.0.4")]
    [InlineData("2.11.30.0", "2.11.30")]
    public void Format_ReducesToThreeParts(string raw, string expected)
    {
        Assert.Equal(expected, VersionInfo.Format(raw));
    }

    [Theory]
    [InlineData("1.0.4-beta.1", "1.0.4-beta.1")]
    [InlineData("1.0.4-rc1+abc", "1.0.4-rc1")]
    public void Format_PreservesPrereleaseSuffix(string raw, string expected)
    {
        Assert.Equal(expected, VersionInfo.Format(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_FallsBackWhenMetadataIsMissing(string? raw)
    {
        Assert.Equal("0.0.0", VersionInfo.Format(raw));
    }

    [Fact]
    public void Display_MatchesAssemblyVersionAndHasNoFourthComponent()
    {
        string display = VersionInfo.Display;

        // The project file is the single source of truth; the UI must not carry its own literal.
        Version? assembly = typeof(AppSettings).Assembly.GetName().Version;
        Assert.NotNull(assembly);
        Assert.Equal($"{assembly.Major}.{assembly.Minor}.{assembly.Build}", display);
        Assert.Equal(2, display.Count(character => character == '.'));
    }
}

public sealed class WindowPlacementTests
{
    private static readonly Rectangle Primary = new(0, 0, 2560, 1440);
    private static readonly Rectangle Secondary = new(2560, 0, 1920, 1080);

    [Fact]
    public void DefaultClientSize_SitsInsideTheSpecifiedRange()
    {
        Size size = WindowPlacement.DefaultSettingsClientSize;
        Assert.InRange(size.Width, 580, 620);
        Assert.InRange(size.Height, 700, 760);
    }

    [Fact]
    public void MinimumWindowSize_IsSmallerThanDefault()
    {
        Assert.True(WindowPlacement.MinimumSettingsWindowSize.Width < WindowPlacement.DefaultSettingsClientSize.Width);
        Assert.True(WindowPlacement.MinimumSettingsWindowSize.Height < WindowPlacement.DefaultSettingsClientSize.Height);
    }

    [Fact]
    public void PositionFullyOnAMonitor_IsUsable()
    {
        Assert.True(WindowPlacement.IsPositionUsable(new Rectangle(100, 100, 630, 737), [Primary]));
    }

    [Fact]
    public void PositionOnADisconnectedMonitor_IsNotUsable()
    {
        // Saved while a second monitor existed; only the primary remains connected.
        var saved = new Rectangle(3000, 200, 630, 737);
        Assert.True(WindowPlacement.IsPositionUsable(saved, [Primary, Secondary]));
        Assert.False(WindowPlacement.IsPositionUsable(saved, [Primary]));
    }

    [Fact]
    public void PositionAboveTheWorkingArea_IsNotUsable()
    {
        // Title bar off the top edge cannot be grabbed with the mouse.
        Assert.False(WindowPlacement.IsPositionUsable(new Rectangle(100, -400, 630, 737), [Primary]));
    }

    [Fact]
    public void PositionBarelyOverlapping_IsNotUsable()
    {
        Assert.False(WindowPlacement.IsPositionUsable(new Rectangle(2540, 700, 630, 737), [Primary]));
    }

    [Fact]
    public void Clamp_PullsAnOffScreenWindowFullyOnto_TheFallbackMonitor()
    {
        Point clamped = WindowPlacement.ClampToWorkingArea(new Rectangle(5000, 5000, 630, 737), [Primary], Primary);

        var result = new Rectangle(clamped, new Size(630, 737));
        Assert.True(Primary.Contains(result), $"expected {result} inside {Primary}");
    }

    [Fact]
    public void Clamp_NeverReturnsANegativeOriginOnASingleMonitor()
    {
        Point clamped = WindowPlacement.ClampToWorkingArea(new Rectangle(-900, -900, 630, 737), [Primary], Primary);

        Assert.Equal(Primary.Left, clamped.X);
        Assert.Equal(Primary.Top, clamped.Y);
    }

    [Fact]
    public void ResolveStartLocation_ReturnsNullWhenNothingWasSaved()
    {
        Assert.Null(WindowPlacement.ResolveStartLocation(null, null, new Size(630, 737), [Primary], Primary));
        Assert.Null(WindowPlacement.ResolveStartLocation(10, null, new Size(630, 737), [Primary], Primary));
    }

    [Fact]
    public void ResolveStartLocation_HonoursAValidSavedPosition()
    {
        Point? location = WindowPlacement.ResolveStartLocation(120, 140, new Size(630, 737), [Primary], Primary);

        Assert.Equal(new Point(120, 140), location);
    }

    [Fact]
    public void ResolveStartLocation_ClampsAPositionFromADisconnectedMonitor()
    {
        var size = new Size(630, 737);

        Point? location = WindowPlacement.ResolveStartLocation(3000, 200, size, [Primary], Primary);

        Assert.NotNull(location);
        Assert.True(Primary.Contains(new Rectangle(location.Value, size)),
            $"clamped location {location} with size {size} must be inside {Primary}");
    }
}

/// <summary>
/// Covers the decision rule behind the v1.0.5 fix for issue #13. The SendInput call itself cannot be
/// tested without a desktop; which keys it is asked to release, and in what order, can be.
/// </summary>
public sealed class ModifierReleaseTests
{
    [Fact]
    public void NoModifiers_ReleasesNothing()
    {
        // The tray menu, the Settings button and the command line hold no keys down.
        Assert.Empty(InputTyper.ModifierKeysToRelease(HotkeyModifiers.None));
    }

    [Fact]
    public void NoRepeatAlone_ReleasesNothing()
    {
        // NoRepeat is a registration flag, not a key a user can be holding.
        Assert.Empty(InputTyper.ModifierKeysToRelease(HotkeyModifiers.NoRepeat));
    }

    [Fact]
    public void CtrlAlt_ReleasesBothSidesOfEach()
    {
        Keys[] keys = InputTyper.ModifierKeysToRelease(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt);

        Assert.Equal([Keys.LMenu, Keys.RMenu, Keys.LControlKey, Keys.RControlKey], keys);
    }

    [Fact]
    public void Alt_IsReleasedBeforeCtrl()
    {
        // Releasing Alt while Ctrl is still down keeps the target from reading it as the lone Alt
        // tap that opens a menu bar.
        Keys[] keys = InputTyper.ModifierKeysToRelease(
            HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win);

        Assert.True(Array.IndexOf(keys, Keys.LMenu) < Array.IndexOf(keys, Keys.LControlKey),
            "Alt must be released before Ctrl");
    }

    [Fact]
    public void EveryModifier_ReleasesEightDistinctKeys()
    {
        Keys[] keys = InputTyper.ModifierKeysToRelease(
            HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win);

        Assert.Equal(8, keys.Length);
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Fact]
    public void DefaultTypeHotkey_ReleasesOnlyTheKeysItUses()
    {
        // Ctrl+Alt+V: both are held when WM_HOTKEY arrives, Shift and Win are not.
        Keys[] keys = InputTyper.ModifierKeysToRelease(new AppSettings().TypeClipboardHotkey.ToModifiers());

        Assert.Contains(Keys.LControlKey, keys);
        Assert.Contains(Keys.LMenu, keys);
        Assert.DoesNotContain(Keys.LShiftKey, keys);
        Assert.DoesNotContain(Keys.LWin, keys);
    }
}

public sealed class HotkeyValidationTests
{
    [Fact]
    public void DefaultHotkeys_AreAcceptedTogether()
    {
        Assert.Null(new AppSettings().ValidateHotkeys());
    }

    [Fact]
    public void DefaultStopHotkey_IsCtrlAltX()
    {
        HotkeyBinding stop = new AppSettings().StopTypingHotkey;

        Assert.True(stop.Ctrl);
        Assert.True(stop.Alt);
        Assert.False(stop.Shift);
        Assert.Equal(Keys.X, stop.Key);
    }

    [Fact]
    public void StopHotkeyMatchingTypeHotkey_IsRejected()
    {
        var settings = new AppSettings();
        settings.StopTypingHotkey = new HotkeyBinding { Key = settings.TypeClipboardHotkey.Key };

        string? error = settings.ValidateHotkeys();

        Assert.NotNull(error);
        Assert.Contains("same combination", error);
    }

    [Fact]
    public void StopHotkeyMatchingHistoryHotkey_IsRejected()
    {
        // The pair a two-way check would have missed: neither is the type hotkey.
        var settings = new AppSettings();
        settings.StopTypingHotkey = new HotkeyBinding { Shift = true, Key = settings.HistoryHotkey.Key };

        Assert.NotNull(settings.ValidateHotkeys());
    }

    [Fact]
    public void HotkeyWithoutAModifier_IsRejected()
    {
        var settings = new AppSettings();
        settings.StopTypingHotkey = new HotkeyBinding { Ctrl = false, Alt = false, Key = Keys.X };

        string? error = settings.ValidateHotkeys();

        Assert.NotNull(error);
        Assert.Contains("modifier", error);
    }

    [Fact]
    public void SettingsFileWithoutStopHotkey_GetsTheDefault()
    {
        // Files written before v1.0.5 have no StopTypingHotkey property at all.
        const string legacy = """{"CharacterDelayMs":25,"MaximumHistoryEntries":75}""";

        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(legacy);

        Assert.NotNull(settings);
        settings.Normalize();
        Assert.Equal(Keys.X, settings.StopTypingHotkey.Key);
        Assert.Null(settings.ValidateHotkeys());
    }

    [Fact]
    public void Normalize_ReplacesAMissingStopHotkey()
    {
        var settings = new AppSettings { StopTypingHotkey = null! };

        settings.Normalize();

        Assert.NotNull(settings.StopTypingHotkey);
    }
}

public sealed class WorkingAreaFitTests
{
    private static readonly Rectangle Small = new(0, 0, 1280, 720);

    [Fact]
    public void AWindowThatAlreadyFits_IsLeftAlone()
    {
        Assert.Equal(new Size(660, 500), WindowPlacement.FitToWorkingArea(new Size(660, 500), Small));
    }

    [Fact]
    public void AWindowTallerThanTheDesktop_IsShrunkToFit()
    {
        // 780 logical is 1170 device pixels at 150%, which does not fit a 1080p screen.
        Size fitted = WindowPlacement.FitToWorkingArea(new Size(990, 1170), Small);

        Assert.True(fitted.Height < Small.Height, $"{fitted.Height} must be inside {Small.Height}");
        Assert.True(fitted.Width <= Small.Width);
    }

    [Fact]
    public void AnAbsurdlySmallWorkingArea_StillLeavesAUsableWindow()
    {
        Size fitted = WindowPlacement.FitToWorkingArea(new Size(660, 780), new Rectangle(0, 0, 100, 100));

        Assert.Equal(new Size(240, 240), fitted);
    }

    [Fact]
    public void HelpIsTallerThanItWasWhenItScrolled()
    {
        // Raised after the first build of the help window scrolled on the maintainer's display.
        Assert.True(WindowPlacement.DefaultHelpClientSize.Height > 620);
    }
}

public sealed class ProductMetadataTests
{
    [Fact]
    public void Product_ComesFromTheAssemblyRatherThanALiteral()
    {
        Assert.Equal("TypeyTypey", VersionInfo.Product);
    }

    [Fact]
    public void AboutFields_AreAllPopulated()
    {
        // About renders these directly, so an unset project property would show as a blank row.
        Assert.NotEmpty(VersionInfo.Description);
        Assert.NotEmpty(VersionInfo.Company);
        Assert.NotEmpty(VersionInfo.Copyright);
    }
}

public sealed class TypingModeSettingsTests
{
    [Fact]
    public void NewSettingsDefaultToUnicode()
    {
        // Not Automatic. Automatic changes how keystrokes reach every target, and an upgrade must
        // not do that to an existing user without them asking. See AGENTS.md §9b.
        Assert.Equal(TypingMode.Unicode, new AppSettings().TypingMode);
    }

    [Fact]
    public void SettingsFileWithoutTypingModeLoadsAsUnicode()
    {
        // Exactly what a v1.0.5 settings file looks like: the property does not exist.
        const string legacy = """{"CharacterDelayMs":25,"InitialDelayMs":400}""";

        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(legacy);

        Assert.NotNull(settings);
        settings.Normalize();
        Assert.Equal(TypingMode.Unicode, settings.TypingMode);
        Assert.False(settings.TypingModeOverridesEnabled);
        Assert.Null(settings.PhysicalModeHotkey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DefinedTypingModeValuesSurviveNormalize(int stored)
    {
        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>($$"""{"TypingMode":{{stored}}}""");

        Assert.NotNull(settings);
        settings.Normalize();
        Assert.Equal(stored, (int)settings.TypingMode);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(-2)]
    public void UndefinedTypingModeValueNormalizesToUnicode(int stored)
    {
        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>($$"""{"TypingMode":{{stored}}}""");

        Assert.NotNull(settings);
        settings.Normalize();
        Assert.Equal(TypingMode.Unicode, settings.TypingMode);
    }

    [Fact]
    public void TypingModeIsPersistedNumerically()
    {
        string json = JsonSerializer.Serialize(new AppSettings { TypingMode = TypingMode.Physical });

        Assert.Contains("\"TypingMode\":1", json);
        Assert.DoesNotContain("Physical Keypresses", json);
    }

    [Fact]
    public void TypingModeAndOverridesSurviveARoundTrip()
    {
        var original = new AppSettings
        {
            TypingMode = TypingMode.Automatic,
            TypingModeOverridesEnabled = true,
            PhysicalModeHotkey = new HotkeyBinding { Ctrl = true, Alt = true, Shift = true, Key = Keys.V }
        };

        AppSettings? restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(original));

        Assert.NotNull(restored);
        restored.Normalize();
        Assert.Equal(TypingMode.Automatic, restored.TypingMode);
        Assert.True(restored.TypingModeOverridesEnabled);
        Assert.NotNull(restored.PhysicalModeHotkey);
        Assert.True(restored.PhysicalModeHotkey.IsSameAs(original.PhysicalModeHotkey));
        Assert.Null(restored.AutomaticModeHotkey);
    }
}

public sealed class TypingModeOverrideHotkeyTests
{
    private static AppSettings WithOverrides(bool enabled, HotkeyBinding? physical = null, HotkeyBinding? unicode = null) =>
        new()
        {
            TypingModeOverridesEnabled = enabled,
            PhysicalModeHotkey = physical,
            UnicodeModeHotkey = unicode
        };

    [Fact]
    public void DisabledOverridesAreNeitherRegisteredNorValidated()
    {
        // Deliberately a duplicate of the primary hotkey: while the feature is off it is inert, so
        // it must not block saving unrelated settings.
        AppSettings settings = WithOverrides(enabled: false, physical: new HotkeyBinding());

        Assert.Empty(settings.AssignedModeOverrides());
        Assert.Null(settings.ValidateHotkeys());
    }

    [Fact]
    public void EnabledWithNothingAssignedIsValidAndRegistersNothing()
    {
        AppSettings settings = WithOverrides(enabled: true);

        Assert.Empty(settings.AssignedModeOverrides());
        Assert.Null(settings.ValidateHotkeys());
    }

    [Fact]
    public void AssignedOverrideIsAcceptedAndReportedWithItsMode()
    {
        var binding = new HotkeyBinding { Ctrl = true, Alt = true, Shift = true, Key = Keys.V };
        AppSettings settings = WithOverrides(enabled: true, physical: binding);

        Assert.Null(settings.ValidateHotkeys());
        (TypingMode mode, HotkeyBinding assigned) = Assert.Single(settings.AssignedModeOverrides());
        Assert.Equal(TypingMode.Physical, mode);
        Assert.Same(binding, assigned);
    }

    [Fact]
    public void OverrideMayNotDuplicateThePrimaryTypeHotkey()
    {
        AppSettings settings = WithOverrides(enabled: true, physical: new HotkeyBinding());

        string? error = settings.ValidateHotkeys();

        Assert.NotNull(error);
        Assert.Contains("Type clipboard", error);
    }

    [Fact]
    public void OverridesMayNotDuplicateEachOther()
    {
        var same = new HotkeyBinding { Ctrl = true, Alt = true, Shift = true, Key = Keys.V };
        AppSettings settings = WithOverrides(enabled: true,
            physical: same,
            unicode: new HotkeyBinding { Ctrl = true, Alt = true, Shift = true, Key = Keys.V });

        string? error = settings.ValidateHotkeys();

        Assert.NotNull(error);
        Assert.Contains("override", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnOverrideWithoutAModifierIsRejected()
    {
        AppSettings settings = WithOverrides(enabled: true,
            physical: new HotkeyBinding { Ctrl = false, Alt = false, Key = Keys.V });

        Assert.NotNull(settings.ValidateHotkeys());
    }

    [Fact]
    public void ValidationNamesTheModeSoTheUserKnowsWhichRowToFix()
    {
        AppSettings settings = WithOverrides(enabled: true, physical: new HotkeyBinding());

        Assert.Contains(TypingModeText.Label(TypingMode.Physical), settings.ValidateHotkeys()!);
    }
}
