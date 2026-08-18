using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Buff
{
    [TestFixture]
    internal class BuffContractsTests
    {
        [Test]
        public void TargetHandle_FullValueEqualityAndStableDictionaryKey()
        {
            var current = new BuffTargetHandle(BuffEntityKind.Unit, 42, 7);
            var same = new BuffTargetHandle(BuffEntityKind.Unit, 42, 7);
            var stale = new BuffTargetHandle(BuffEntityKind.Unit, 42, 6);
            var otherKind = new BuffTargetHandle(BuffEntityKind.Enemy, 42, 7);
            var map = new Dictionary<BuffTargetHandle, string> { [current] = "current" };

            Assert.AreEqual(current, same);
            Assert.AreEqual(current.GetHashCode(), same.GetHashCode());
            Assert.AreEqual("current", map[same]);
            Assert.AreNotEqual(current, stale);
            Assert.AreNotEqual(current, otherKind);
            Assert.IsTrue(current.IsValid);
            Assert.IsFalse(new BuffTargetHandle((BuffEntityKind)99, 42, 7).IsValid);
        }

        [Test]
        public void SourceHandle_StoresStableSourceAndOptionalAttacker()
        {
            var entity = new BuffSourceHandle(12, 91);
            var system = new BuffSourceHandle(13);

            Assert.IsTrue(entity.IsValid);
            Assert.AreEqual(12, entity.SourceId);
            Assert.AreEqual(91, entity.AttackerRuntimeId);
            Assert.IsTrue(system.IsValid);
            Assert.AreEqual(13, system.SourceId);
            Assert.AreEqual(-1, system.AttackerRuntimeId);
            Assert.IsFalse(new BuffSourceHandle(-1).IsValid);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void Request_NonFiniteValue_IsRejected(double value)
        {
            BuffRequestValidationResult result = Request(value: value).Validate();

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(BuffRequestValidationCode.NonFiniteValue, result.Code);
        }

        [TestCase(-1d)]
        [TestCase(-2d)]
        public void Request_NonPositiveRatioFactor_IsRejected(double ratioDelta)
        {
            BuffRequestValidationResult result = Request(
                value: ratioDelta,
                valueMode: BuffValueMode.Ratio).Validate();

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(BuffRequestValidationCode.InvalidRatioFactor, result.Code);
        }

        [TestCase(0L)]
        [TestCase(-1L)]
        public void Request_NonPositiveDuration_IsRejected(long durationMs)
        {
            BuffRequestValidationResult result = Request(
                timeMode: BuffTimeMode.DurationMs,
                durationMs: durationMs).Validate();

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(BuffRequestValidationCode.InvalidDuration, result.Code);
        }

        [Test]
        public void Request_Permanent_NormalizesDurationAndCopiesPayload()
        {
            var sourcePayload = new List<byte> { 1, 2, 3 };
            BuffApplyRequest request = Request(
                timeMode: BuffTimeMode.Permanent,
                durationMs: 999,
                payload: sourcePayload);

            sourcePayload[0] = 99;
            sourcePayload.Add(4);

            Assert.IsTrue(request.Validate().IsValid);
            Assert.AreEqual(0, request.DurationMs);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, request.CustomPayload);
            Assert.Throws<NotSupportedException>(
                () => ((IList<byte>)request.CustomPayload)[0] = 5);
        }

        [Test]
        public void InstanceSnapshot_CopiesRequestPayloadAndPreservesRequestData()
        {
            BuffApplyRequest request = Request(payload: new byte[] { 4, 5 });
            var snapshot = new BuffInstanceSnapshot(123, request);

            Assert.AreEqual(123, snapshot.InstanceId);
            Assert.AreNotSame(request, snapshot.Request);
            Assert.AreEqual(request.BuffType, snapshot.Request.BuffType);
            Assert.AreEqual(request.Target, snapshot.Request.Target);
            Assert.AreEqual(request.Source, snapshot.Request.Source);
            CollectionAssert.AreEqual(new byte[] { 4, 5 }, snapshot.Request.CustomPayload);
            Assert.Throws<NotSupportedException>(
                () => ((IList<byte>)snapshot.Request.CustomPayload)[0] = 9);
        }

        [Test]
        public void OperationResult_DistinguishesAllRequiredStatuses()
        {
            BuffRequestValidationResult invalid = Request(value: double.NaN).Validate();

            AssertResult(BuffOperationResult.Applied(1), BuffOperationStatus.Applied, true);
            AssertResult(BuffOperationResult.Refreshed(1), BuffOperationStatus.Refreshed, true);
            BuffOperationResult rejected = BuffOperationResult.Rejected(invalid);
            AssertResult(rejected, BuffOperationStatus.Rejected, false);
            Assert.AreEqual(BuffRequestValidationCode.NonFiniteValue, rejected.ValidationCode);
            AssertResult(BuffOperationResult.Removed(1), BuffOperationStatus.Removed, true);
            AssertResult(BuffOperationResult.NotFound(), BuffOperationStatus.NotFound, false);
            AssertResult(BuffOperationResult.StaleTarget(), BuffOperationStatus.StaleTarget, false);
            AssertResult(BuffOperationResult.UnsupportedTarget(), BuffOperationStatus.UnsupportedTarget, false);
        }

        [Test]
        public void TargetCapabilities_CopySortAndRejectMutation()
        {
            var numeric = new List<BuffNumericChannel>
            {
                BuffNumericChannel.MoveSpeed,
                BuffNumericChannel.AttackPower,
            };
            var state = new List<BuffStateChannel> { BuffStateChannel.Suppressed };
            var capabilities = new BuffTargetCapabilities(numeric, state);

            numeric.Clear();
            state.Clear();

            CollectionAssert.AreEqual(
                new[] { BuffNumericChannel.AttackPower, BuffNumericChannel.MoveSpeed },
                capabilities.NumericChannels);
            Assert.IsTrue(capabilities.Supports(BuffNumericChannel.MoveSpeed));
            Assert.IsTrue(capabilities.Supports(BuffStateChannel.Suppressed));
            Assert.Throws<NotSupportedException>(
                () => ((IList<BuffNumericChannel>)capabilities.NumericChannels)[0] = BuffNumericChannel.Scale);
        }

        private static BuffApplyRequest Request(
            double value = 10,
            BuffValueMode valueMode = BuffValueMode.Flat,
            BuffTimeMode timeMode = BuffTimeMode.Permanent,
            long durationMs = 0,
            IReadOnlyList<byte> payload = null)
        {
            return new BuffApplyRequest(
                0,
                new BuffTargetHandle(BuffEntityKind.Unit, 1, 1),
                new BuffSourceHandle(0),
                value,
                valueMode,
                timeMode,
                durationMs,
                payload);
        }

        private static void AssertResult(
            BuffOperationResult result,
            BuffOperationStatus expectedStatus,
            bool expectedSuccess)
        {
            Assert.AreEqual(expectedStatus, result.Status);
            Assert.AreEqual(expectedSuccess, result.IsSuccess);
        }
    }
}
