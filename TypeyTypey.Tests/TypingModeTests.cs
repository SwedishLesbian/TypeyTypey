using Xunit;

namespace TypeyTypey.Tests;

/// <summary>
/// A deterministic stand-in for a US keyboard, so planning is tested against a known layout rather
/// than whatever the machine running the tests happens to have installed. No test in this file
/// injects input: they assert what *would* be sent, which is the only way to test this without
/// typing into the developer's desktop.
/// </summary>
internal sealed class FakeUsLayout : IKeyboardLayoutMap
{
    private const string ShiftedDigits = ")!@#$%^&*(";

    /// <summary>Unshifted punctuation on a US layout, mapped to its OEM virtual key.</summary>
    private static readonly Dictionary<char, ushort> Unshifted = new()
    {
        [' '] = 0x20, [';'] = 0xBA, ['='] = 0xBB, [','] = 0xBC, ['-'] = 0xBD, ['.'] = 0xBE,
        ['/'] = 0xBF, ['`'] = 0xC0, ['['] = 0xDB, ['\\'] = 0xDC, [']'] = 0xDD, ['\''] = 0xDE
    };

    /// <summary>The same keys with Shift held.</summary>
    private static readonly Dictionary<char, ushort> Shifted = new()
    {
        [':'] = 0xBA, ['+'] = 0xBB, ['<'] = 0xBC, ['_'] = 0xBD, ['>'] = 0xBE,
        ['?'] = 0xBF, ['~'] = 0xC0, ['{'] = 0xDB, ['|'] = 0xDC, ['}'] = 0xDD, ['"'] = 0xDE
    };

    public bool TryMap(char character, out ushort virtualKey, out StrokeModifiers modifiers)
    {
        virtualKey = 0;
        modifiers = StrokeModifiers.None;

        if (character is >= 'a' and <= 'z')
        {
            virtualKey = (ushort)('A' + (character - 'a'));
            return true;
        }

        if (character is >= 'A' and <= 'Z')
        {
            virtualKey = character;
            modifiers = StrokeModifiers.Shift;
            return true;
        }

        if (character is >= '0' and <= '9')
        {
            virtualKey = character;
            return true;
        }

        int shiftedDigit = ShiftedDigits.IndexOf(character);
        if (shiftedDigit >= 0)
        {
            virtualKey = (ushort)('0' + shiftedDigit);
            modifiers = StrokeModifiers.Shift;
            return true;
        }

        if (Unshifted.TryGetValue(character, out virtualKey))
            return true;

        if (Shifted.TryGetValue(character, out virtualKey))
        {
            modifiers = StrokeModifiers.Shift;
            return true;
        }

        virtualKey = 0;
        return false;
    }

    /// <summary>Any non-zero value; the planner only has to carry it through unchanged.</summary>
    public ushort ScanCode(ushort virtualKey) => (ushort)(virtualKey + 0x1000);
}

/// <summary>A layout with an AltGr character, for the shift state the US fake never produces.</summary>
internal sealed class FakeAltGrLayout : IKeyboardLayoutMap
{
    public bool TryMap(char character, out ushort virtualKey, out StrokeModifiers modifiers)
    {
        if (character == '€')
        {
            virtualKey = (ushort)'E';
            modifiers = StrokeModifiers.Ctrl | StrokeModifiers.Alt;
            return true;
        }
        virtualKey = 0;
        modifiers = StrokeModifiers.None;
        return false;
    }

    public ushort ScanCode(ushort virtualKey) => 0x12;
}

public sealed class TypingModePolicyTests
{
    [Fact]
    public void PersistedValueOfUnicodeIsZero()
    {
        // A settings file from v1.0.5 has no TypingMode property, so it deserializes to zero. That
        // must be the mode those builds actually had, or upgrading changes behaviour silently.
        Assert.Equal(0, (int)TypingMode.Unicode);
    }

    [Fact]
    public void EveryModeHasItsOwnLabelAndDescription()
    {
        string[] labels = [.. TypingModeText.InDisplayOrder.Select(TypingModeText.Label)];
        string[] descriptions = [.. TypingModeText.InDisplayOrder.Select(TypingModeText.Description)];

        Assert.Equal(3, TypingModeText.InDisplayOrder.Length);
        Assert.Equal(labels.Length, labels.Distinct().Count());
        Assert.Equal(descriptions.Length, descriptions.Distinct().Count());
        Assert.All(descriptions, description => Assert.False(string.IsNullOrWhiteSpace(description)));
    }

    [Fact]
    public void UnicodeModeIsNotLabelledSendInput()
    {
        // Both backends call SendInput, so naming one of them after it would mislead.
        Assert.All(TypingModeText.InDisplayOrder,
            mode => Assert.DoesNotContain("SendInput", TypingModeText.Label(mode), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OverrideHotkeyIdsRoundTripThroughTheirMode()
    {
        foreach (TypingMode mode in TypingModeText.InDisplayOrder)
            Assert.Equal(mode, HotkeyManager.ModeForOverrideId(HotkeyManager.ModeOverrideHotkeyId(mode)));
    }

    [Fact]
    public void OverrideIdsDoNotCollideWithTheFixedHotkeyIds()
    {
        int[] fixedIds =
        [
            HotkeyManager.TypeClipboardHotkeyId,
            HotkeyManager.HistoryHotkeyId,
            HotkeyManager.StopTypingHotkeyId
        ];

        foreach (TypingMode mode in TypingModeText.InDisplayOrder)
            Assert.DoesNotContain(HotkeyManager.ModeOverrideHotkeyId(mode), fixedIds);

        Assert.Null(HotkeyManager.ModeForOverrideId(HotkeyManager.TypeClipboardHotkeyId));
    }
}

public sealed class PhysicalPlanningTests
{
    private static readonly FakeUsLayout Layout = new();

    private static TypingPlan PlanOrThrow(string text, TypingMode mode)
    {
        (TypingPlan? plan, TypingPlanFailure? failure) = TypingPlanner.Plan(text, mode, Layout);
        Assert.Null(failure);
        Assert.NotNull(plan);
        return plan;
    }

    [Fact]
    public void LowercaseAsciiNeedsNoModifier()
    {
        TypingPlan plan = PlanOrThrow("ab", TypingMode.Physical);

        Assert.All(plan.Steps, step => Assert.True(step.Physical));
        Assert.All(plan.Steps, step => Assert.Equal(StrokeModifiers.None, step.Modifiers));
        Assert.Equal((ushort)'A', plan.Steps[0].VirtualKey);
        Assert.Equal((ushort)'B', plan.Steps[1].VirtualKey);
    }

    [Fact]
    public void UppercaseAsciiTakesShiftOnTheSameKeyAsLowercase()
    {
        // The whole of issue #16: the remote console sees the key, and case comes from Shift.
        TypingPlan upper = PlanOrThrow("A", TypingMode.Physical);
        TypingPlan lower = PlanOrThrow("a", TypingMode.Physical);

        Assert.Equal(lower.Steps[0].VirtualKey, upper.Steps[0].VirtualKey);
        Assert.Equal(StrokeModifiers.Shift, upper.Steps[0].Modifiers);
        Assert.Equal(StrokeModifiers.None, lower.Steps[0].Modifiers);
    }

    [Fact]
    public void DigitsAreUnshiftedAndTheirSymbolsAreShifted()
    {
        TypingPlan digits = PlanOrThrow("019", TypingMode.Physical);
        Assert.All(digits.Steps, step => Assert.Equal(StrokeModifiers.None, step.Modifiers));

        TypingPlan symbols = PlanOrThrow("!@#", TypingMode.Physical);
        Assert.All(symbols.Steps, step => Assert.Equal(StrokeModifiers.Shift, step.Modifiers));
        Assert.Equal((ushort)'1', symbols.Steps[0].VirtualKey);
    }

    [Fact]
    public void TheAcceptanceStringPlansEntirelyPhysically()
    {
        TypingPlan plan = PlanOrThrow("AaZz019!@#_-+=[]{};:'\",.<>/?\\|`~", TypingMode.Physical);

        Assert.All(plan.Steps, step => Assert.True(step.Physical));
        Assert.False(plan.UsesUnicode);
    }

    [Fact]
    public void SpaceTabAndNewlineBecomeTheirOwnKeys()
    {
        TypingPlan plan = PlanOrThrow(" \t\n", TypingMode.Physical);

        Assert.Equal(0x20, plan.Steps[0].VirtualKey);
        Assert.Equal(TypingPlanner.VkTab, plan.Steps[1].VirtualKey);
        Assert.Equal(TypingPlanner.VkReturn, plan.Steps[2].VirtualKey);
    }

    [Fact]
    public void CarriageReturnInACrlfPairIsDroppedRatherThanTypedTwice()
    {
        TypingPlan plan = PlanOrThrow("a\r\nb", TypingMode.Physical);

        Assert.Equal(3, plan.Steps.Count);
        Assert.Equal(TypingPlanner.VkReturn, plan.Steps[1].VirtualKey);

        // A lone CR is still a line break.
        Assert.Single(PlanOrThrow("\r", TypingMode.Physical).Steps);
    }

    [Fact]
    public void ScanCodeFromTheLayoutIsCarriedOnEveryPhysicalStep()
    {
        // A browser console identifies the key from the scan code, so losing it here would defeat
        // the mode this whole feature exists for.
        TypingPlan plan = PlanOrThrow("a", TypingMode.Physical);

        Assert.NotEqual(0, plan.Steps[0].ScanCode);
        Assert.Equal(Layout.ScanCode(plan.Steps[0].VirtualKey), plan.Steps[0].ScanCode);
    }

    [Theory]
    [InlineData("café", 3)]
    [InlineData("™", 0)]
    [InlineData("a✓b", 1)]
    public void UnsupportedCharacterIsRefusedWithItsPosition(string text, int index)
    {
        (TypingPlan? plan, TypingPlanFailure? failure) = TypingPlanner.Plan(text, TypingMode.Physical, Layout);

        Assert.Null(plan);
        Assert.NotNull(failure);
        Assert.Equal(index, failure.Index);
        Assert.Equal(text[index], failure.CodePoint);
    }

    [Fact]
    public void FailureNamesThePositionAndCodePointButNotTheText()
    {
        (_, TypingPlanFailure? failure) = TypingPlanner.Plan("hunter€2", TypingMode.Physical, Layout);

        Assert.NotNull(failure);
        Assert.Contains("position 7", failure.Message);
        Assert.Contains("U+20AC", failure.Message);
        Assert.DoesNotContain("hunter", failure.Message);
    }

    [Fact]
    public void EmojiIsReportedAsOneCodePointRatherThanTwoBrokenHalves()
    {
        (_, TypingPlanFailure? failure) = TypingPlanner.Plan("a😀", TypingMode.Physical, Layout);

        Assert.NotNull(failure);
        Assert.Equal(0x1F600, failure.CodePoint);
        Assert.Contains("U+1F600", failure.Message);
    }

    [Fact]
    public void NothingIsEmittedWhenPlanningFails()
    {
        // The point of preflighting: a refused string leaves the destination untouched rather than
        // half-filled.
        (TypingPlan? plan, _) = TypingPlanner.Plan("ok then ✓ more text", TypingMode.Physical, Layout);

        Assert.Null(plan);
    }

    [Fact]
    public void AltGrCharacterMapsToCtrlPlusAlt()
    {
        (TypingPlan? plan, _) = TypingPlanner.Plan("€", TypingMode.Physical, new FakeAltGrLayout());

        Assert.NotNull(plan);
        Assert.Equal(StrokeModifiers.Ctrl | StrokeModifiers.Alt, plan.Steps[0].Modifiers);
    }
}

public sealed class UnicodePlanningTests
{
    private static readonly FakeUsLayout Layout = new();

    [Fact]
    public void EveryCharacterGoesThroughTheUnicodePathIncludingOnesAKeyCouldType()
    {
        (TypingPlan? plan, TypingPlanFailure? failure) = TypingPlanner.Plan("Aa1!✓", TypingMode.Unicode, Layout);

        Assert.Null(failure);
        Assert.NotNull(plan);
        Assert.All(plan.Steps, step => Assert.False(step.Physical));
        Assert.False(plan.UsesPhysical);
    }

    [Fact]
    public void UnicodeModeNeverFails()
    {
        (TypingPlan? plan, TypingPlanFailure? failure) = TypingPlanner.Plan("TypeyTypey ™ café ✓ 😀", TypingMode.Unicode, Layout);

        Assert.Null(failure);
        Assert.NotNull(plan);
    }

    [Fact]
    public void CharactersArePreservedInOrder()
    {
        const string text = "Zz09!~";
        (TypingPlan? plan, _) = TypingPlanner.Plan(text, TypingMode.Unicode, Layout);

        Assert.NotNull(plan);
        Assert.Equal(text, new string([.. plan.Steps.Select(step => step.Character)]));
    }
}

public sealed class AutomaticPlanningTests
{
    private static readonly FakeUsLayout Layout = new();

    private static TypingPlan Plan(string text)
    {
        (TypingPlan? plan, TypingPlanFailure? failure) = TypingPlanner.Plan(text, TypingMode.Automatic, Layout);
        Assert.Null(failure);
        Assert.NotNull(plan);
        return plan;
    }

    [Fact]
    public void FullyRepresentableTextIsEntirelyPhysical()
    {
        TypingPlan plan = Plan("AaZz019!@#");

        Assert.True(plan.UsesPhysical);
        Assert.False(plan.UsesUnicode);
    }

    [Fact]
    public void UnrepresentableTextIsEntirelyUnicode()
    {
        TypingPlan plan = Plan("™✓");

        Assert.False(plan.UsesPhysical);
        Assert.True(plan.UsesUnicode);
    }

    [Fact]
    public void MixedTextFallsBackPerCharacterRatherThanForTheWholeString()
    {
        // One unsupported character must not push the rest of a console string onto the Unicode
        // path, which is the behaviour the remote console cannot handle.
        TypingPlan plan = Plan("caf√©");

        Assert.True(plan.UsesPhysical);
        Assert.True(plan.UsesUnicode);
        Assert.All(plan.Steps.Take(3), step => Assert.True(step.Physical));
    }

    [Fact]
    public void OrderIsPreservedExactlyAcrossTheBoundary()
    {
        const string text = "ab™cd✓ef";
        TypingPlan plan = Plan(text);

        Assert.Equal(text, new string([.. plan.Steps.Select(step => step.Character)]));
        Assert.Equal([true, true, false, true, true, false, true, true],
            plan.Steps.Select(step => step.Physical));
    }

    [Fact]
    public void AutomaticNeverRefusesText()
    {
        foreach (string text in new[] { "😀", "́", "日本語", "TypeyTypey ™ café ✓ 😀" })
        {
            (TypingPlan? plan, TypingPlanFailure? failure) = TypingPlanner.Plan(text, TypingMode.Automatic, Layout);
            Assert.Null(failure);
            Assert.NotNull(plan);
        }
    }

    [Fact]
    public void SurrogatePairsStayIntactAndBothHalvesGoThroughUnicode()
    {
        TypingPlan plan = Plan("a😀b");

        Assert.Equal(4, plan.Steps.Count);
        Assert.False(plan.Steps[1].Physical);
        Assert.False(plan.Steps[2].Physical);
        Assert.True(char.IsHighSurrogate(plan.Steps[1].Character));
        Assert.True(char.IsLowSurrogate(plan.Steps[2].Character));
    }
}

public sealed class KeyEventOrderingTests
{
    [Fact]
    public void ShiftedCharacterIsShiftDownKeyDownKeyUpShiftUp()
    {
        var step = TypingStep.Key('A', 0x41, 0x1E, StrokeModifiers.Shift);

        Assert.Equal(
        [
            (KeyEventSequence.VkLShift, false),
            ((ushort)0x41, false),
            ((ushort)0x41, true),
            (KeyEventSequence.VkLShift, true)
        ], KeyEventSequence.ForStep(step).Select(e => (e.VirtualKey, e.KeyUp)));
    }

    [Fact]
    public void UnmodifiedCharacterIsJustDownThenUp()
    {
        var step = TypingStep.Key('a', 0x41, 0x1E, StrokeModifiers.None);

        Assert.Equal([(ushort)0x41, (ushort)0x41], KeyEventSequence.ForStep(step).Select(e => e.VirtualKey));
        Assert.Equal([false, true], KeyEventSequence.ForStep(step).Select(e => e.KeyUp));
    }

    [Fact]
    public void ModifiersAreReleasedInTheReverseOfTheOrderTheyWerePressed()
    {
        var step = TypingStep.Key('x', 0x58, 0x2D, StrokeModifiers.Ctrl | StrokeModifiers.Shift);
        ushort[] order = [.. KeyEventSequence.ForStep(step).Select(e => e.VirtualKey)];

        Assert.Equal(
        [
            KeyEventSequence.VkLControl, KeyEventSequence.VkLShift,
            0x58, 0x58,
            KeyEventSequence.VkLShift, KeyEventSequence.VkLControl
        ], order);
    }

    [Fact]
    public void AltGrUsesTheRightAltRatherThanTheLeft()
    {
        var step = TypingStep.Key('€', 0x45, 0x12, StrokeModifiers.Ctrl | StrokeModifiers.Alt);
        ushort[] keys = [.. KeyEventSequence.ForStep(step).Select(e => e.VirtualKey)];

        Assert.Contains(KeyEventSequence.VkRMenu, keys);
        Assert.DoesNotContain(KeyEventSequence.VkLMenu, keys);
    }

    [Fact]
    public void PlainAltUsesTheLeftAlt()
    {
        var step = TypingStep.Key('x', 0x58, 0x2D, StrokeModifiers.Alt);
        ushort[] keys = [.. KeyEventSequence.ForStep(step).Select(e => e.VirtualKey)];

        Assert.Contains(KeyEventSequence.VkLMenu, keys);
        Assert.DoesNotContain(KeyEventSequence.VkRMenu, keys);
    }

    [Fact]
    public void ScanCodeAccompaniesTheKeyButNotTheModifiers()
    {
        var step = TypingStep.Key('A', 0x41, 0x1E, StrokeModifiers.Shift);
        IReadOnlyList<KeyEvent> events = KeyEventSequence.ForStep(step);

        Assert.Equal(0x1E, events[1].ScanCode);
        Assert.Equal(0x1E, events[2].ScanCode);
        Assert.Equal(0, events[0].ScanCode);
    }

    [Fact]
    public void AUnicodeStepProducesNoKeyEvents()
    {
        Assert.Empty(KeyEventSequence.ForStep(TypingStep.Unicode('✓')));
    }

    [Fact]
    public void ReleaseEmitsKeyUpsForEverythingHeldAndNothingElse()
    {
        IReadOnlyList<KeyEvent> release = KeyEventSequence.ForRelease(StrokeModifiers.Shift | StrokeModifiers.Ctrl);

        Assert.All(release, keyEvent => Assert.True(keyEvent.KeyUp));
        Assert.Equal([KeyEventSequence.VkLShift, KeyEventSequence.VkLControl], release.Select(e => e.VirtualKey));
        Assert.Empty(KeyEventSequence.ForRelease(StrokeModifiers.None));
    }
}
