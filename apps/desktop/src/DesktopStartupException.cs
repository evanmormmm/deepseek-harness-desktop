namespace DeepSeekHarness.Desktop;

/// <summary>Reports a desktop startup condition with a user-actionable message.</summary>
internal sealed class DesktopStartupException : Exception
{
    internal DesktopStartupException(string message)
        : base(message)
    {
    }

    internal DesktopStartupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
