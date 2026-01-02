using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncAwait.Exception
{
    public class RateLimitException : IOException
    {
        public TimeSpan RetryAfter { get; }
        public RateLimitException(TimeSpan retryAfter) : base("Rate limited") => RetryAfter = retryAfter;
    }

    //Failure that can succeed upon retries
    public class TransientSendException : IOException
    {
        public TransientSendException(string msg) : base(msg) { }
    }
    public class PermanentSendException : IOException
    {
        public PermanentSendException(string msg) : base(msg) { }
    }

}
