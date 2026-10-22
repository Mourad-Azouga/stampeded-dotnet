namespace Stampeded;

/// <summary>A failure reported by the upstream Rust library.</summary>
public sealed class StampededException(string operation, string message)
    : Exception($"Stampeded {operation}: {message}")
{
    public string Operation { get; } = operation;
}
