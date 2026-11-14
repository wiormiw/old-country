namespace OldCountry.Domain.Errors;

/// <summary>An expected refusal from a use case. Each refusal has a stable code that clients can check.</summary>
public interface IError
{
    string Code { get; }
}
