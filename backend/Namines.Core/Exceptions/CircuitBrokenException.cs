using System;

namespace Namines.Core.Exceptions
{
    /// <summary>
    /// Harici bir servis (AI, Cloud DB, Docker) devre kesici (Circuit Breaker) tarafından
    /// geçici olarak devre dışı bırakıldığında fırlatılan kurumsal hata.
    /// HTTP 503 Service Unavailable ve RFC 7807 ProblemDetails ile eşlenir.
    /// </summary>
    public class CircuitBrokenException : Exception
    {
        public string ServiceName { get; }
        public TimeSpan? RetryAfter { get; }

        public CircuitBrokenException(string serviceName, string message, TimeSpan? retryAfter = null, Exception? innerException = null)
            : base(message, innerException)
        {
            ServiceName = serviceName;
            RetryAfter = retryAfter;
        }
    }
}
