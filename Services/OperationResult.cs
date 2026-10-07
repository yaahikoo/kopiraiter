namespace Kopiraiter.Services;

internal readonly record struct OperationResult<T>(T? Value, string? Error, bool IsSuccess)
{
    public static OperationResult<T> Ok(T value) => new(value, null, true);

    public static OperationResult<T> Fail(string error) => new(default, error, false);
}
