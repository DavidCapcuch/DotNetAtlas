// ReSharper disable FlagArgument

using System.Diagnostics.CodeAnalysis;

namespace DotNetAtlas.SharedKernel.Exceptions;

/// <summary>
/// Provides fluent guard clause methods for throwing exceptions.
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
    /// Throw.If(tier == SubscriptionTier.Free, new DataIntegrityException(
    ///     "Alert.CannotCreatePaidSubscriptionWithFreeTier",
    ///     "Cannot create paid subscription with Free tier."));
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
