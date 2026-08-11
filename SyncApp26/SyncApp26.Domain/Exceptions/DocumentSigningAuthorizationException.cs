using System;

namespace SyncApp26.Domain.Exceptions
{
    /// <summary>
    /// Thrown when a caller attempts to sign a document without holding the officer role required
    /// for its type. Kept distinct from InvalidOperationException (used for state/validation failures,
    /// e.g. "document isn't pending this step") so callers can tell an authorization violation apart
    /// from an ordinary state error instead of matching on message text.
    /// </summary>
    public class DocumentSigningAuthorizationException : Exception
    {
        public DocumentSigningAuthorizationException(string message) : base(message) { }
    }
}
