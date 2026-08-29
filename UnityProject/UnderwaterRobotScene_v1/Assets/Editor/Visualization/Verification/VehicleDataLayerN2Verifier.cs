using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnderwaterRobotScene.Visualization.Data;
using UnderwaterRobotScene.Visualization.Data.LocalTesting;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnderwaterRobotScene.EditorTools
{
    public static class VehicleDataLayerN2Verifier
    {
        private static readonly string[] ProductionAssetPaths =
        {
            "Assets/Scripts/Visualization/Data/VehicleState.cs",
            "Assets/Scripts/Visualization/Data/ReceivedVehicleState.cs",
            "Assets/Scripts/Visualization/Data/IDataSource.cs",
            "Assets/Scripts/Visualization/Data/LocalTestSource.cs",
            "Assets/Scripts/Visualization/Data/LocalTesting/IDeterministicVehicleStateGenerator.cs",
            "Assets/Scripts/Visualization/Data/LocalTesting/DefaultDeterministicVehicleStateGenerator.cs",
            "Assets/Scripts/Visualization/Data/LocalTesting/DeterministicAuvIntegrationTrajectory.cs",
            "Assets/Scripts/Visualization/Data/LocalTesting/DeterministicRovDiagnosticTrajectory.cs",
            "Assets/Scripts/Visualization/Data/LocalTesting/DeterministicUsvDiagnosticTrajectory.cs",
            "Assets/Scripts/Visualization/Data/VehicleStateStore.cs",
            "Assets/Scripts/Visualization/Data/VehicleStatePolicies.cs"
        };

#if UNITY_EDITOR
        [MenuItem("Tools/Underwater Demo/Verify Public Data Core N2")]
        public static void RunFromMenu()
        {
            int exitCode = RunVerification(Console.WriteLine);
            if (exitCode != 0)
            {
                throw new InvalidOperationException("Public data core N2 verification failed.");
            }
        }
#endif

        public static int Main(string[] args)
        {
            return RunVerification(Console.WriteLine);
        }

        public static int RunVerification(Action<string> writeLine)
        {
            if (writeLine == null) throw new ArgumentNullException(nameof(writeLine));

            var tests = new[]
            {
                new VerificationCase("VehicleState immutable value semantics", VerifyVehicleStateValueSemantics),
                new VerificationCase("IDataSource lifecycle contract", VerifyDataSourceContract),
                new VerificationCase("LocalTestSource deterministic lifecycle", VerifyLocalTestSource),
                new VerificationCase("Store ordering, rejection, and capacity policy", VerifyStorePolicies),
                new VerificationCase("Store multi-vehicle isolation and stable windows", VerifyStoreIsolationAndSnapshots),
                new VerificationCase("Store concurrent readers and writers", VerifyConcurrentAccess),
                new VerificationCase("Store clear and dispose behavior", VerifyClearAndDispose),
                new VerificationCase("Public core dependency boundary", VerifyDependencyBoundary)
            };

            int passed = 0;
            foreach (VerificationCase test in tests)
            {
                try
                {
                    test.Body();
                    passed++;
                    writeLine("PASS | " + test.Name);
                }
                catch (Exception exception)
                {
                    writeLine("FAIL | " + test.Name + " | " + exception.GetType().Name + ": " + exception.Message);
                }
            }

            writeLine("N2 verifier summary: " + passed + "/" + tests.Length + " passed.");
            return passed == tests.Length ? 0 : 1;
        }

        private static void VerifyVehicleStateValueSemantics()
        {
            RequireReadonlyValueType(typeof(Vector3d));
            RequireReadonlyValueType(typeof(Quaterniond));
            RequireReadonlyValueType(typeof(VehicleState));
            RequireReadonlyValueType(typeof(ReceivedVehicleState));

            var state = CreateState("AUV-01", VehicleType.Auv, 7UL, 7.0);
            VehicleState copied = state;

            Require(copied.VehicleId == "AUV-01", "VehicleId was not preserved.");
            Require(copied.VehicleType == VehicleType.Auv, "VehicleType was not preserved.");
            Require(copied.SequenceNumber == 7UL, "SequenceNumber was not preserved.");
            Require(copied.SourceTimestampSeconds == 7.0, "Timestamp was not preserved.");
            Require(copied.Position.Equals(new Vector3d(7.0, 14.0, -7.0)), "Position was not preserved.");
            Require(copied.Orientation.Equals(Quaterniond.Identity), "Orientation was not preserved.");
            Require((copied.ValidFields & VehicleStateFields.LinearVelocity) != 0, "Motion validity was not preserved.");
            Require(copied.IsStructurallyValid, "A valid state was rejected.");

            var normalizedQuaternion = new VehicleState(
                "AUV-01",
                VehicleType.Auv,
                7.5,
                8UL,
                Vector3d.Zero,
                new Quaterniond(0.0, 0.0, 0.0, 2.0),
                Vector3d.Zero,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position | VehicleStateFields.Orientation,
                WorldFrame.Unknown,
                BodyFrame.Unknown);
            Require(normalizedQuaternion.IsStructurallyValid, "A normalizable quaternion was rejected.");
            Require(normalizedQuaternion.Orientation.Equals(Quaterniond.Identity),
                "VehicleState must store a normalized orientation.");

            var invalidQuaternion = new VehicleState(
                "AUV-01",
                VehicleType.Auv,
                8.0,
                8UL,
                Vector3d.Zero,
                new Quaterniond(0.0, 0.0, 0.0, 0.0),
                Vector3d.Zero,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position | VehicleStateFields.Orientation,
                WorldFrame.Unknown,
                BodyFrame.Unknown);
            Require(!invalidQuaternion.IsStructurallyValid, "A zero-norm valid orientation must be rejected.");

            var nearZeroQuaternion = new VehicleState(
                "AUV-01",
                VehicleType.Auv,
                9.0,
                9UL,
                Vector3d.Zero,
                new Quaterniond(1e-9, 0.0, 0.0, 0.0),
                Vector3d.Zero,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position | VehicleStateFields.Orientation,
                WorldFrame.Unknown,
                BodyFrame.Unknown);
            Require(!nearZeroQuaternion.IsStructurallyValid, "A near-zero orientation must be rejected.");

            var nonFiniteQuaternion = new VehicleState(
                "AUV-01",
                VehicleType.Auv,
                10.0,
                10UL,
                Vector3d.Zero,
                new Quaterniond(double.NaN, 0.0, 0.0, 1.0),
                Vector3d.Zero,
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position | VehicleStateFields.Orientation,
                WorldFrame.Unknown,
                BodyFrame.Unknown);
            Require(!nonFiniteQuaternion.IsStructurallyValid, "A non-finite orientation must be rejected.");

            LocalTestVehicle[] vehicles =
            {
                new LocalTestVehicle("AUV-01", VehicleType.Auv, new Vector3d(1.0, 2.0, 3.0)),
                new LocalTestVehicle("ROV-01", VehicleType.Rov, new Vector3d(4.0, 5.0, 6.0))
            };
            var config = new LocalTestSourceConfig("LOCAL_COPY_TEST", 0.25, vehicles);
            vehicles[0] = new LocalTestVehicle("MUTATED", VehicleType.Unknown, Vector3d.Zero);
            Require(config.GetVehicle(0).VehicleId == "AUV-01", "Configuration retained the caller's mutable array.");
        }

        private static void VerifyDataSourceContract()
        {
            Type source = typeof(IDataSource);
            Require(typeof(IDisposable).IsAssignableFrom(source), "IDataSource must extend IDisposable.");
            Require(source.GetProperty("SourceId")?.PropertyType == typeof(string), "SourceId contract is missing.");
            Require(source.GetProperty("Status")?.PropertyType == typeof(DataSourceStatus), "Status contract is missing.");
            Require(source.GetProperty("IsRunning")?.PropertyType == typeof(bool), "IsRunning contract is missing.");
            Require(source.GetProperty("LastError")?.PropertyType == typeof(DataSourceError), "LastError contract is missing.");
            Require(source.GetMethod("GetStatistics", Type.EmptyTypes)?.ReturnType == typeof(DataSourceStatistics),
                "GetStatistics contract is missing.");
            Require(source.GetMethod("Start", new[] { typeof(IStateSink) }) != null, "Start(IStateSink) contract is missing.");
            Require(source.GetMethod("Stop", Type.EmptyTypes) != null, "Stop() contract is missing.");

            MethodInfo publish = typeof(IStateSink).GetMethod("Publish");
            Require(publish != null && publish.ReturnType == typeof(PublishResult), "IStateSink.Publish contract is missing.");
            ParameterInfo[] parameters = publish.GetParameters();
            Require(parameters.Length == 1 && parameters[0].ParameterType == typeof(ReceivedVehicleState).MakeByRefType(),
                "Publish must accept one ReceivedVehicleState by readonly reference.");
            Require(parameters[0].IsIn, "Publish parameter must be readonly in, not mutable ref.");
        }

        private static void VerifyLocalTestSource()
        {
            var vehicles = new[]
            {
                new LocalTestVehicle("AUV-01", VehicleType.Auv, new Vector3d(1.0, 0.0, 0.0)),
                new LocalTestVehicle("ROV-01", VehicleType.Rov, new Vector3d(10.0, 0.0, 0.0))
            };
            var config = new LocalTestSourceConfig("LOCAL_N2", 0.5, vehicles);
            var firstSink = new CaptureSink();
            var secondSink = new CaptureSink();

            using (var first = new LocalTestSource(config))
            using (var second = new LocalTestSource(config))
            {
                Require(first.Status == DataSourceStatus.Stopped && !first.IsRunning, "New source must be stopped.");
                Require(first.Step() == 0, "Stopped source must not publish.");

                first.Start(firstSink);
                first.Start(firstSink);
                second.Start(secondSink);
                Require(first.Status == DataSourceStatus.Running && first.IsRunning, "Start must enter Running.");

                Require(first.Step() == 2 && first.Step() == 2 && first.Step() == 2, "Each step must publish every vehicle.");
                Require(second.Step() == 2 && second.Step() == 2 && second.Step() == 2, "Second source did not publish.");
                Require(firstSink.Samples.Count == secondSink.Samples.Count, "Identical runs produced different counts.");

                for (int index = 0; index < firstSink.Samples.Count; index++)
                {
                    Require(firstSink.Samples[index].State.Equals(secondSink.Samples[index].State),
                        "Identical configuration diverged at output " + index + ".");
                }

                Require(firstSink.Samples[0].State.VehicleId == "AUV-01", "First vehicle was not distinguishable.");
                Require(firstSink.Samples[1].State.VehicleId == "ROV-01", "Second vehicle was not distinguishable.");
                Require(firstSink.Samples[0].State.SequenceNumber == 0UL, "First sequence must start at zero.");
                Require(firstSink.Samples[2].State.SequenceNumber == 1UL, "Sequence must advance once per step.");
                Require(firstSink.Samples[4].State.SourceTimestampSeconds == 1.0, "Timestamp must advance by sample interval.");
                VehicleState defaultSecondAuv = firstSink.Samples[2].State;
                Require(defaultSecondAuv.Position.Equals(new Vector3d(2.0, 2.0, -1.0)) &&
                        defaultSecondAuv.LinearVelocity.Equals(new Vector3d(2.0, 4.0, -2.0)) &&
                        defaultSecondAuv.Orientation.Equals(Quaterniond.Identity),
                    "Default generator no longer reproduces the original N2 deterministic state.");

                first.Stop();
                first.Stop();
                int stoppedCount = firstSink.Samples.Count;
                Require(first.Step() == 0 && firstSink.Samples.Count == stoppedCount, "Stopped source published new state.");

                first.Start(firstSink);
                Require(first.Step() == 2, "Restart did not publish.");
                Require(firstSink.Samples[stoppedCount].State.SequenceNumber == 0UL, "Restart must reset sequence.");
                Require(firstSink.Samples[stoppedCount].State.Equals(firstSink.Samples[0].State),
                    "Restart must reproduce the same VehicleState sequence.");
                Require(firstSink.Samples[stoppedCount].SourceEpoch != firstSink.Samples[0].SourceEpoch,
                    "Restart must open a new source epoch.");

                DataSourceStatistics statistics = first.GetStatistics();
                Require(statistics.StartCount == 2UL, "Repeated Start while running must be idempotent.");
                Require(statistics.StopCount == 1UL, "Repeated Stop must be idempotent.");
                Require(statistics.PublishedSamples == 8UL, "Published sample count is incorrect.");
                Require(first.LastError.IsNone, "Healthy local source reported an error.");
            }

            using (var faulted = new LocalTestSource(config))
            {
                faulted.Start(new ThrowingSink());
                Require(faulted.Step() == 0, "A throwing sink must not be reported as accepted.");
                Require(faulted.Status == DataSourceStatus.Faulted && !faulted.IsRunning,
                    "A sink failure must transition the source to Faulted.");
                Require(!faulted.LastError.IsNone && faulted.LastError.Code == "SINK_PUBLISH_FAILED",
                    "A sink failure must be exposed through LastError.");
            }

            using (var reentrant = new LocalTestSource(config))
            {
                var sink = new StopOnFirstPublishSink(reentrant);
                reentrant.Start(sink);
                Require(reentrant.Step() == 1, "A reentrant stop must finish only the in-flight publication.");
                Require(reentrant.Status == DataSourceStatus.Stopped && !reentrant.IsRunning,
                    "A reentrant Stop must leave the source stopped.");
                Require(sink.PublishedCount == 1, "A reentrant Stop allowed additional vehicle publications.");
            }

            var injectedGenerator = new RecordingGenerator();
            var injectedConfig = new LocalTestSourceConfig(
                "LOCAL_N2_INJECTED",
                0.5,
                vehicles,
                injectedGenerator);
            var injectedSink = new CaptureSink();
            using (var injected = new LocalTestSource(injectedConfig))
            {
                injected.Start(injectedSink);
                Require(injected.Step(12.5) == 2, "Injected generator did not publish every vehicle.");
                Require(injectedGenerator.EvaluationCount == 2,
                    "Generator injection changed multi-vehicle publication semantics.");
                Require(injectedSink.Samples.All(sample => sample.ReceivedAtMonotonicSeconds == 12.5),
                    "Explicit monotonic receive time was not preserved with generator injection.");
                Require(injectedSink.Samples[0].State.Position.Equals(new Vector3d(101.0, 0.0, 0.0)),
                    "Injected generator output was not used.");
            }

            LocalTestStateEvaluator compatibilityEvaluator = (vehicle, sampleIndex, timestamp) =>
                injectedGenerator.Evaluate(vehicle, sampleIndex, timestamp);
            var compatibilityConfig = new LocalTestSourceConfig(
                "LOCAL_N2_DELEGATE_COMPATIBILITY",
                0.5,
                vehicles,
                compatibilityEvaluator);
            Require(compatibilityConfig.StateEvaluator != null &&
                    compatibilityConfig.StateGenerator != null,
                "Delegate compatibility entry no longer adapts to the generator boundary.");
        }

        private static void VerifyStorePolicies()
        {
            using (var store = new VehicleStateStore(new VehicleStateStorePolicy(
                       3,
                       timeoutSeconds: 2.0,
                       timestampDiscontinuityThresholdSeconds: 10.0)))
            {
                var invalidState = new VehicleState(
                    "AUV-INVALID",
                    VehicleType.Auv,
                    1.0,
                    1UL,
                    new Vector3d(double.NaN, 0.0, 0.0),
                    Quaterniond.Identity,
                    Vector3d.Zero,
                    Vector3d.Zero,
                    Vector3d.Zero,
                    VehicleStateFields.Position | VehicleStateFields.Orientation,
                    WorldFrame.Unknown,
                    BodyFrame.Unknown);
                var invalidSample = new ReceivedVehicleState(
                    invalidState,
                    "SOURCE",
                    1UL,
                    1.0,
                    SequenceKind.Synthetic,
                    DecodeQualityFlags.None);
                Require(store.Publish(invalidSample) == PublishResult.InvalidSample, "Invalid sample was accepted.");
                Require(store.GetStatistics().InvalidSamples == 1UL, "Invalid sample was not counted.");

                Require(store.Publish(CreateReceived("SOURCE", 1UL, "AUV-01", 1UL, 1.0, 1.0)) == PublishResult.Accepted,
                    "First sample was rejected.");
                Require(store.Publish(CreateReceived("SOURCE", 1UL, "AUV-01", 2UL, 2.0, 2.0)) == PublishResult.Accepted,
                    "Second sample was rejected.");
                Require(store.Publish(CreateReceived("SOURCE", 1UL, "AUV-01", 2UL, 2.0, 2.1)) == PublishResult.DuplicateSequence,
                    "Identical duplicate was not classified.");
                Require(store.Publish(CreateReceived("SOURCE", 1UL, "AUV-01", 2UL, 2.5, 2.2)) == PublishResult.ConflictingDuplicate,
                    "Conflicting duplicate was not classified.");
                Require(store.Publish(CreateReceived("SOURCE", 1UL, "AUV-01", 1UL, 3.0, 3.0)) == PublishResult.OutOfOrderSequence,
                    "Out-of-order sequence was not rejected.");
                Require(store.Publish(CreateReceived("SOURCE", 1UL, "AUV-01", 3UL, 2.0, 3.0)) == PublishResult.NonIncreasingTimestamp,
                    "Non-increasing timestamp was not rejected.");
                Require(store.Publish(CreateReceived("SOURCE", 1UL, "AUV-01", 5UL, 5.0, 5.0)) == PublishResult.Accepted,
                    "Sequence jump should be accepted.");
                Require(store.Publish(CreateReceived("SOURCE", 1UL, "AUV-01", 6UL, 6.0, 6.0)) == PublishResult.Accepted,
                    "Post-jump sample was rejected.");
                Require(store.Publish(CreateReceived("SOURCE", 1UL, "AUV-01", 7UL, 7.0, 5.5)) == PublishResult.LocalClockRegression,
                    "Local receive-clock regression was not rejected.");

                Require(store.TryReadWindow("SOURCE", 1UL, "AUV-01", out VehicleStateWindow window), "Window was not found.");
                Require(window.Count == 3, "Ring capacity was not enforced.");
                Require(window[0].State.SequenceNumber == 2UL && window[1].State.SequenceNumber == 5UL &&
                        window[2].State.SequenceNumber == 6UL, "Ring window is not in logical order.");

                Require(store.TryGetChannelStatistics("SOURCE", 1UL, "AUV-01", out VehicleStateChannelStatistics stats),
                    "Channel statistics were not found.");
                Require(stats.AcceptedSamples == 4UL, "Accepted count is incorrect.");
                Require(stats.DuplicateSamples == 1UL && stats.ConflictingDuplicateSamples == 1UL,
                    "Duplicate counts are incorrect.");
                Require(stats.OutOfOrderSamples == 1UL && stats.NonIncreasingTimestampSamples == 1UL,
                    "Ordering rejection counts are incorrect.");
                Require(stats.LocalClockRegressionSamples == 1UL, "Local clock regression count is incorrect.");
                Require(stats.MissingSequenceCount == 2UL, "Missing sequence count is incorrect.");

                Require(store.Publish(CreateReceived("SOURCE", 1UL, "AUV-01", 7UL, 100.0, 7.0)) == PublishResult.Accepted,
                    "A timestamp discontinuity should start a new window.");
                Require(store.TryReadWindow("SOURCE", 1UL, "AUV-01", out VehicleStateWindow resetWindow) &&
                        resetWindow.Count == 1 && resetWindow.Latest.State.SequenceNumber == 7UL,
                    "Timestamp discontinuity did not clear the old history.");
                Require(store.TryGetChannelStatistics("SOURCE", 1UL, "AUV-01", out stats) &&
                        stats.DiscontinuityResets == 1UL, "Timestamp discontinuity reset was not counted.");
            }
        }

        private static void VerifyStoreIsolationAndSnapshots()
        {
            using (var store = new VehicleStateStore(new VehicleStateStorePolicy(4, timeoutSeconds: 2.0)))
            {
                store.Publish(CreateReceived("SOURCE", 10UL, "AUV-01", 1UL, 1.0, 1.0));
                store.Publish(CreateReceived("SOURCE", 10UL, "ROV-01", 1UL, 1.0, 1.0));
                store.Publish(CreateReceived("SOURCE", 11UL, "AUV-01", 1UL, 1.0, 1.0));

                Require(store.TryReadLatest("SOURCE", 11UL, "AUV-01", out ReceivedVehicleState nextEpoch), "New epoch was not found.");
                Require(!store.TryReadLatest("SOURCE", 10UL, "AUV-01", out _), "Retired AUV epoch remained readable.");
                Require(!store.TryReadLatest("SOURCE", 10UL, "ROV-01", out _), "Retired ROV epoch remained readable.");
                Require(store.Publish(CreateReceived("SOURCE", 10UL, "AUV-01", 2UL, 2.0, 2.0)) ==
                        PublishResult.RetiredEpoch, "A late packet recreated a retired epoch.");
                Require(nextEpoch.SourceEpoch == 11UL, "Source epoch was not preserved.");

                store.Publish(CreateReceived("SOURCE", 11UL, "ROV-01", 1UL, 1.0, 1.0));
                Require(store.TryReadLatest("SOURCE", 11UL, "ROV-01", out ReceivedVehicleState rov), "ROV was not found.");
                Require(rov.State.VehicleId != nextEpoch.State.VehicleId, "Vehicle channels were mixed.");
                Require(!store.TryReadLatest("SOURCE", 11UL, "UNKNOWN", out _), "Unknown vehicle must return not found.");

                Require(store.TryReadWindow("SOURCE", 11UL, "AUV-01", out VehicleStateWindow before), "Initial window missing.");
                store.Publish(CreateReceived("SOURCE", 11UL, "AUV-01", 2UL, 2.0, 2.0));
                Require(before.Count == 1 && before.Latest.State.SequenceNumber == 1UL,
                    "Published state mutated a previously returned window.");
                ReceivedVehicleState[] callerCopy = before.ToArray();
                callerCopy[0] = default;
                Require(before.Latest.State.SequenceNumber == 1UL, "ToArray exposed internal window storage.");

                Require(store.TryReadLatest("SOURCE", 11UL, "AUV-01", out ReceivedVehicleState latest), "Latest AUV missing.");
                Require(latest.State.SequenceNumber == 2UL, "Later write did not replace latest state.");
                Require(store.TryReadLatest("SOURCE", 11UL, "AUV-01", out ReceivedVehicleState repeated) &&
                        repeated.Equals(latest), "Repeated reads changed stored state.");

                Require(store.TryReadSnapshot("SOURCE", 11UL, "AUV-01", 3.0, out VehicleSnapshot healthy) &&
                        healthy.Health == SourceHealth.Healthy && !healthy.IsTimedOut && healthy.AgeSeconds == 1.0,
                    "Fresh snapshot health is incorrect.");
                Require(store.TryReadSnapshot("SOURCE", 11UL, "AUV-01", 4.1, out VehicleSnapshot timedOut) &&
                        timedOut.Health == SourceHealth.TimedOut && timedOut.IsTimedOut,
                    "Timeout must use local monotonic receive time.");
            }
        }

        private static void VerifyConcurrentAccess()
        {
            const int vehicleCount = 4;
            const int samplesPerVehicle = 500;
            using (var store = new VehicleStateStore(new VehicleStateStorePolicy(8)))
            {
                var failures = new ConcurrentQueue<Exception>();
                int activeWriters = vehicleCount;
                int concurrentObservations = 0;
                var startGate = new ManualResetEventSlim(false);
                var tasks = new List<Task>();

                for (int vehicleIndex = 0; vehicleIndex < vehicleCount; vehicleIndex++)
                {
                    int capturedIndex = vehicleIndex;
                    tasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            startGate.Wait();
                            string vehicleId = "VEHICLE-" + capturedIndex;
                            for (ulong sequence = 1UL; sequence <= samplesPerVehicle; sequence++)
                            {
                                PublishResult result = store.Publish(
                                    CreateReceived("CONCURRENT", 1UL, vehicleId, sequence, sequence, sequence));
                                Require(result == PublishResult.Accepted, vehicleId + " publish failed: " + result);
                                Thread.SpinWait(1000);
                            }
                        }
                        catch (Exception exception)
                        {
                            failures.Enqueue(exception);
                        }
                        finally
                        {
                            Interlocked.Decrement(ref activeWriters);
                        }
                    }));
                }

                for (int readerIndex = 0; readerIndex < 2; readerIndex++)
                {
                    tasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            startGate.Wait();
                            while (Volatile.Read(ref activeWriters) > 0)
                            {
                                for (int vehicleIndex = 0; vehicleIndex < vehicleCount; vehicleIndex++)
                                {
                                    if (store.TryReadLatest("CONCURRENT", 1UL, "VEHICLE-" + vehicleIndex,
                                            out ReceivedVehicleState latest))
                                    {
                                        double sequence = latest.State.SequenceNumber;
                                        Require(latest.State.Position.X == sequence &&
                                                latest.State.Position.Y == sequence * 2.0 &&
                                                latest.State.Position.Z == -sequence,
                                            "Reader observed a torn state.");
                                        Interlocked.Increment(ref concurrentObservations);
                                    }

                                    if (store.TryReadWindow("CONCURRENT", 1UL, "VEHICLE-" + vehicleIndex,
                                            out VehicleStateWindow window))
                                    {
                                        Require(window.Count > 0 && window.Latest.State.VehicleId == "VEHICLE-" + vehicleIndex,
                                            "Reader observed a mixed window.");
                                    }
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            failures.Enqueue(exception);
                        }
                    }));
                }

                startGate.Set();
                Task.WaitAll(tasks.ToArray());
                Require(failures.IsEmpty, failures.TryPeek(out Exception failure) ? failure.Message : "Concurrent access failed.");
                Require(concurrentObservations > 0, "Concurrency test made no reads while writers were active.");
                for (int vehicleIndex = 0; vehicleIndex < vehicleCount; vehicleIndex++)
                {
                    Require(store.TryReadLatest("CONCURRENT", 1UL, "VEHICLE-" + vehicleIndex,
                            out ReceivedVehicleState latest), "Final concurrent state missing.");
                    Require(latest.State.SequenceNumber == samplesPerVehicle, "Final concurrent sequence is incorrect.");
                }
            }
        }

        private static void VerifyClearAndDispose()
        {
            var store = new VehicleStateStore(new VehicleStateStorePolicy(2));
            ReceivedVehicleState sample = CreateReceived("SOURCE", 1UL, "AUV-01", 1UL, 1.0, 1.0);
            Require(store.Publish(sample) == PublishResult.Accepted, "Setup publish failed.");
            store.Clear();
            Require(!store.TryReadLatest("SOURCE", 1UL, "AUV-01", out _), "Clear retained a channel.");
            Require(store.Publish(sample) == PublishResult.Accepted, "Clear should leave the store reusable.");
            store.Dispose();
            store.Dispose();
            Require(store.Publish(sample) == PublishResult.StoreDisposed, "Disposed store accepted a publish.");
            Require(!store.TryReadLatest("SOURCE", 1UL, "AUV-01", out _), "Disposed store exposed state.");
        }

        private static void VerifyDependencyBoundary()
        {
            string[] forbiddenTokens =
            {
                "using UnityEngine",
                "UnityEngine.",
                "MonoBehaviour",
                "GameObject",
                "Transform",
                "DemoMotionController",
                "AuvPose",
                "UdpClient",
                "System.Net.Sockets",
                "MemoryMappedFile",
                "SharedMemory",
                "MechanicalArm"
            };

            foreach (string relativePath in ProductionAssetPaths)
            {
                string path = Path.Combine(Environment.CurrentDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Require(File.Exists(path), "Production file missing: " + relativePath);
                string source = File.ReadAllText(path);
                foreach (string token in forbiddenTokens)
                {
                    Require(source.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0,
                        "Forbidden dependency token '" + token + "' found in " + relativePath + ".");
                }
            }
        }

        private static VehicleState CreateState(string vehicleId, VehicleType type, ulong sequence, double timestamp)
        {
            double sequenceValue = sequence;
            return new VehicleState(
                vehicleId,
                type,
                timestamp,
                sequence,
                new Vector3d(sequenceValue, sequenceValue * 2.0, -sequenceValue),
                Quaterniond.Identity,
                new Vector3d(0.5, 0.0, -0.25),
                Vector3d.Zero,
                Vector3d.Zero,
                VehicleStateFields.Position | VehicleStateFields.Orientation | VehicleStateFields.LinearVelocity,
                WorldFrame.Unknown,
                BodyFrame.Unknown);
        }

        private static ReceivedVehicleState CreateReceived(
            string sourceId,
            ulong sourceEpoch,
            string vehicleId,
            ulong sequence,
            double timestamp,
            double receivedAt)
        {
            return new ReceivedVehicleState(
                CreateState(vehicleId, VehicleType.Unknown, sequence, timestamp),
                sourceId,
                sourceEpoch,
                receivedAt,
                SequenceKind.Synthetic,
                DecodeQualityFlags.None);
        }

        private static void RequireReadonlyValueType(Type type)
        {
            Require(type.IsValueType, type.Name + " must be a value type.");
            bool isReadonly = type.GetCustomAttributes(false)
                .Any(attribute => attribute.GetType().FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
            Require(isReadonly, type.Name + " must be declared readonly.");
            Require(type.GetFields(BindingFlags.Public | BindingFlags.Instance).All(field => field.IsInitOnly),
                type.Name + " exposes a mutable public field.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private readonly struct VerificationCase
        {
            public VerificationCase(string name, Action body)
            {
                Name = name;
                Body = body;
            }

            public string Name { get; }
            public Action Body { get; }
        }

        private sealed class CaptureSink : IStateSink
        {
            private readonly object gate = new object();
            private readonly List<ReceivedVehicleState> samples = new List<ReceivedVehicleState>();

            public IReadOnlyList<ReceivedVehicleState> Samples
            {
                get
                {
                    lock (gate)
                    {
                        return samples.ToArray();
                    }
                }
            }

            public PublishResult Publish(in ReceivedVehicleState sample)
            {
                lock (gate)
                {
                    samples.Add(sample);
                }

                return PublishResult.Accepted;
            }
        }

        private sealed class ThrowingSink : IStateSink
        {
            public PublishResult Publish(in ReceivedVehicleState sample)
            {
                throw new InvalidOperationException("Synthetic sink failure.");
            }
        }

        private sealed class StopOnFirstPublishSink : IStateSink
        {
            private readonly LocalTestSource source;

            public StopOnFirstPublishSink(LocalTestSource source)
            {
                this.source = source;
            }

            public int PublishedCount { get; private set; }

            public PublishResult Publish(in ReceivedVehicleState sample)
            {
                PublishedCount++;
                source.Stop();
                return PublishResult.Accepted;
            }
        }

        private sealed class RecordingGenerator : IDeterministicVehicleStateGenerator
        {
            public int EvaluationCount { get; private set; }

            public VehicleState Evaluate(
                LocalTestVehicle vehicle,
                ulong sampleIndex,
                double sourceTimestampSeconds)
            {
                EvaluationCount++;
                return new VehicleState(
                    vehicle.VehicleId,
                    vehicle.VehicleType,
                    sourceTimestampSeconds,
                    sampleIndex,
                    new Vector3d(
                        vehicle.PositionOffset.X + 100.0 + sampleIndex,
                        vehicle.PositionOffset.Y,
                        vehicle.PositionOffset.Z),
                    Quaterniond.Identity,
                    Vector3d.Zero,
                    Vector3d.Zero,
                    Vector3d.Zero,
                    VehicleStateFields.Position | VehicleStateFields.Orientation,
                    vehicle.WorldFrame,
                    vehicle.BodyFrame);
            }
        }
    }
}
