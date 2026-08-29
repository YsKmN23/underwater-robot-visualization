using System;

namespace UnderwaterRobotScene.Visualization.Data
{
    public enum DataSourceStatus
    {
        Stopped = 0,
        Starting = 1,
        Running = 2,
        Stopping = 3,
        Faulted = 4,
        Disposed = 5
    }

    public readonly struct DataSourceError : IEquatable<DataSourceError>
    {
        public DataSourceError(string code, string message)
        {
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public static DataSourceError None => new DataSourceError(string.Empty, string.Empty);

        public string Code { get; }
        public string Message { get; }
        public bool IsNone => string.IsNullOrEmpty(Code);

        public bool Equals(DataSourceError other)
        {
            return string.Equals(Code, other.Code, StringComparison.Ordinal) &&
                   string.Equals(Message, other.Message, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is DataSourceError other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(Code ?? string.Empty);
                return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Message ?? string.Empty);
            }
        }
    }

    public readonly struct DataSourceStatistics
    {
        public DataSourceStatistics(
            ulong startCount,
            ulong stopCount,
            ulong attemptedSamples,
            ulong publishedSamples,
            ulong rejectedSamples,
            ulong faultCount,
            double lastPublishedAtMonotonicSeconds)
        {
            StartCount = startCount;
            StopCount = stopCount;
            AttemptedSamples = attemptedSamples;
            PublishedSamples = publishedSamples;
            RejectedSamples = rejectedSamples;
            FaultCount = faultCount;
            LastPublishedAtMonotonicSeconds = lastPublishedAtMonotonicSeconds;
        }

        public ulong StartCount { get; }
        public ulong StopCount { get; }
        public ulong AttemptedSamples { get; }
        public ulong PublishedSamples { get; }
        public ulong RejectedSamples { get; }
        public ulong FaultCount { get; }
        public double LastPublishedAtMonotonicSeconds { get; }
    }

    public interface IStateSink
    {
        PublishResult Publish(in ReceivedVehicleState sample);
    }

    public interface IDataSource : IDisposable
    {
        string SourceId { get; }
        DataSourceStatus Status { get; }
        bool IsRunning { get; }
        DataSourceError LastError { get; }

        DataSourceStatistics GetStatistics();
        void Start(IStateSink sink);
        void Stop();
    }

    public interface IManualStepDataSource : IDataSource
    {
        int Step(double receivedAtMonotonicSeconds);
    }
}
