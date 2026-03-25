using FluentResults;

namespace Platform.SharedKernel.Errors;

/// <summary>
/// Extension methods for FluentResults error collections.
/// </summary>
public static class FluentResultExtensions
{
    /// <param name="errors">The collection of errors to convert.</param>
    extension(IEnumerable<IError> errors)
    {
        /// <summary>
        /// Joins all errors into a single string with the specified separator.
        /// Domain errors are formatted as "ErrorCode:ErrorMessage", regular errors as just their message.
        /// </summary>
        public string ToErrorsSummary(string separator = ";")
        {
            return string.Join(separator, errors.Select(e => e is DomainError domainError
                ? $"{domainError.ErrorCode}:{domainError.Message}"
                : e.Message));
        }

        /// <summary>
        /// Converts FluentResults errors to ErrorCode|ErrorMessage tuples.
        /// For <see cref="DomainError"/> instances, uses the ErrorCode property.
        /// For regular <see cref="IError"/> instances, ErrorCode is N/A.
        /// </summary>
        /// <returns>A list of tuples containing (ErrorCode, ErrorMessage) pairs.</returns>
        public IList<(string ErrorCode, string ErrorMessage)> ToErrorDetails()
        {
            return
            [
                .. errors.Select(e =>
                {
                    var errorCode = e switch
                    {
                        DomainError domainError => domainError.ErrorCode,
                        _ => "N/A"
                    };
                    return (ErrorCode: errorCode, ErrorMessage: e.Message);
                })
            ];
        }
    }
}
