namespace TypeyTypey;

/// <summary>Persisted theme preference. Stored numerically; never persist display strings.</summary>
internal enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>The concrete appearance a window is painted with once <see cref="AppTheme.System"/> is resolved.</summary>
internal enum EffectiveTheme
{
    Light,
    Dark
}
