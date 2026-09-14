namespace Rebelgent.Core.Publishing;

public sealed class PackagePublishedResult
{
    public bool Succeeded { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static PackagePublishedResult Ok() => new() { Succeeded = true };

    public static PackagePublishedResult Fail(string error) => new()
    {
        Succeeded = false,
        ErrorMessage = error
    };
}
