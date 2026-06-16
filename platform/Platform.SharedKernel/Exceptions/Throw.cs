// ReSharper disable FlagArgument

using System.Diagnostics.CodeAnalysis;

namespace Platform.SharedKernel.Exceptions;

/// <summary>
/// Provides light-weight fluent guard clause methods for throwing exceptions.
/// Use for invariant checks that indicate bugs in the calling code.
/// </summary>
[SuppressMessage("Naming", "CA1716", Justification = "By design.")]
public static class Throw
{
    /// <summary>
    /// Throws the specified exception if the condition is true.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="exception">The exception to throw if the condition is true.</param>
    /// <example>
    /// <code>
    /// Throw.If(userId == Guid.Empty, new DataIntegrityException(
    ///     "Basket.UserIdRequired",
    ///     "A basket must belong to a user."));
    /// </code>
    /// </example>
    public static void If(bool condition, Exception exception)
    {
        if (condition)
        {
            throw exception;
        }
    }
}
