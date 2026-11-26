namespace SwarmCastStudio.Services;

/// <summary>
/// Raised when the OpenCode CLI cannot be located, started, or queried successfully.
/// The message is written to be shown directly to the user.
/// </summary>
public sealed class OpenCodeUnavailableException : Exception
{
    /// <summary>Creates an exception with an actionable message.</summary>
    public OpenCodeUnavailableException(string message) : base(message) { }

    /// <summary>Creates an exception with an actionable message and an inner cause.</summary>
    public OpenCodeUnavailableException(string message, Exception inner) : base(message, inner) { }
}
