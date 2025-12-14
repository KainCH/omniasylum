using System;

namespace OmniForge.Core.Exceptions
{
    public class ReauthRequiredException : Exception
    {
        public ReauthRequiredException(string message) : base(message)
        {
        }

        public ReauthRequiredException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
