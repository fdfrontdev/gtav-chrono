namespace Chrono.Domain;

/// <summary>Crime severity, mapped from GTA wanted stars (FR-1.3): 1–2★ Minor, 3–4★ Moderate, 5★ Severe.</summary>
public enum CrimeSeverity { Minor, Moderate, Severe }

/// <summary>Identity state (FR-2): Burned = face seen during an offense (recognition + warrant).</summary>
public enum IdentityState { Clean, Burned }

/// <summary>Superpower used to escape prison (FR-10.1).</summary>
public enum EscapeKind { Dash, Fly, Invisible, TimeStop, Stealth, Fight }   // S13: player-chosen methods
