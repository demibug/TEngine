using System;

namespace GameLogic
{
    public sealed class FguiLoadException : Exception
    {
        public string Stage { get; }
        public string PackageKey { get; }
        public string Location { get; }

        public FguiLoadException(string stage, string packageKey, string location, string message,
            Exception innerException = null) : base(message, innerException)
        {
            Stage = stage;
            PackageKey = packageKey;
            Location = location;
        }

        public override string ToString()
        {
            return $"{base.ToString()}\nFGUI stage={Stage}, package={PackageKey}, location={Location}";
        }
    }

    public sealed class FguiTimeoutException : TimeoutException
    {
        public FguiTimeoutException(string message, Exception innerException = null) : base(message, innerException) { }
    }
}
