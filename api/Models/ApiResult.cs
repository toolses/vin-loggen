namespace VinLoggen.Api.Models;

public enum ApiErrorCode
{
    None = 0,
    ExternalServiceDown,
    ImageUnreadable,
    QuotaExceeded,
    Unauthorized,
    UnknownError
}

public record ApiResult<T>(
    bool         Success,
    T?           Data,
    ApiErrorCode ErrorCode = ApiErrorCode.None,
    string?      Message   = null
)
{
    public static ApiResult<T> Ok(T data) =>
        new(true, data);

    public static ApiResult<T> Fail(ApiErrorCode code, string message) =>
        new(false, default, code, message);
}
