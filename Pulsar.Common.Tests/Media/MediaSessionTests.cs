using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Common.Enums;
using Pulsar.Common.Messages.Audio;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Messages.Webcam;
using Pulsar.Common.Networking;
using Pulsar.Common.Video;

namespace Pulsar.Common.Tests.Media
{
    [TestClass]
    public class MediaSessionTests
    {
        [TestMethod]
        public void RoundTrip_GetWebcamRequest_PreservesSessionFieldsAndStatus()
        {
            var original = new GetWebcam
            {
                CreateNew = true,
                Quality = 82,
                DisplayIndex = 2,
                Status = RemoteWebcamStatus.Continue,
                UseGPU = true,
                FramesRequested = 6,
                IsBufferedMode = true,
                SessionId = "webcam-session-123",
                Sequence = 42
            };

            var result = RoundTrip(original);

            Assert.IsInstanceOfType(result, typeof(GetWebcam));
            var typed = (GetWebcam)result;
            Assert.AreEqual(original.CreateNew, typed.CreateNew);
            Assert.AreEqual(original.Quality, typed.Quality);
            Assert.AreEqual(original.DisplayIndex, typed.DisplayIndex);
            Assert.AreEqual(original.Status, typed.Status);
            Assert.AreEqual(original.UseGPU, typed.UseGPU);
            Assert.AreEqual(original.FramesRequested, typed.FramesRequested);
            Assert.AreEqual(original.IsBufferedMode, typed.IsBufferedMode);
            Assert.AreEqual(original.SessionId, typed.SessionId);
            Assert.AreEqual(original.Sequence, typed.Sequence);
        }

        [TestMethod]
        public void RoundTrip_GetWebcamResponse_PreservesSessionFieldsAndStatus()
        {
            var original = new GetWebcamResponse
            {
                Image = new byte[] { 0, 1, 2, 254, 255 },
                Quality = 91,
                Monitor = 1,
                Resolution = new Resolution { Width = 1920, Height = 1080 },
                Timestamp = 1700000000123,
                IsLastRequestedFrame = true,
                FrameRate = 29.97f,
                SessionId = "webcam-session-456",
                Sequence = 77,
                Status = MediaStreamStatus.Stalled,
                ErrorCode = "WEBCAM_STALLED",
                ErrorMessage = "No frame received"
            };

            var result = RoundTrip(original);

            Assert.IsInstanceOfType(result, typeof(GetWebcamResponse));
            var typed = (GetWebcamResponse)result;
            CollectionAssert.AreEqual(original.Image, typed.Image);
            Assert.AreEqual(original.Quality, typed.Quality);
            Assert.AreEqual(original.Monitor, typed.Monitor);
            Assert.AreEqual(original.Resolution, typed.Resolution);
            Assert.AreEqual(original.Timestamp, typed.Timestamp);
            Assert.AreEqual(original.IsLastRequestedFrame, typed.IsLastRequestedFrame);
            Assert.AreEqual(original.FrameRate, typed.FrameRate);
            Assert.AreEqual(original.SessionId, typed.SessionId);
            Assert.AreEqual(original.Sequence, typed.Sequence);
            Assert.AreEqual(original.Status, typed.Status);
            Assert.AreEqual(original.ErrorCode, typed.ErrorCode);
            Assert.AreEqual(original.ErrorMessage, typed.ErrorMessage);
        }

        [TestMethod]
        public void RoundTrip_GetMicrophoneRequest_PreservesSessionFields()
        {
            var original = new GetMicrophone
            {
                CreateNew = true,
                DeviceIndex = 3,
                Bitrate = 48000,
                Destroy = false,
                SessionId = "microphone-session-123",
                Sequence = 18,
                Channels = 2
            };

            var result = RoundTrip(original);

            Assert.IsInstanceOfType(result, typeof(GetMicrophone));
            var typed = (GetMicrophone)result;
            Assert.AreEqual(original.CreateNew, typed.CreateNew);
            Assert.AreEqual(original.DeviceIndex, typed.DeviceIndex);
            Assert.AreEqual(original.Bitrate, typed.Bitrate);
            Assert.AreEqual(original.Destroy, typed.Destroy);
            Assert.AreEqual(original.SessionId, typed.SessionId);
            Assert.AreEqual(original.Sequence, typed.Sequence);
            Assert.AreEqual(original.Channels, typed.Channels);
        }

        [TestMethod]
        public void RoundTrip_GetMicrophoneResponse_PreservesSessionFieldsAndStatus()
        {
            var original = new GetMicrophoneResponse
            {
                Audio = new byte[] { 10, 20, 30, 40 },
                Device = 3,
                SessionId = "microphone-session-456",
                Sequence = 19,
                Timestamp = 1700000000456,
                Status = MediaStreamStatus.DeviceBusy,
                ErrorCode = "MEDIA_DEVICE_BUSY",
                ErrorMessage = "Device is busy",
                Channels = 2
            };

            var result = RoundTrip(original);

            Assert.IsInstanceOfType(result, typeof(GetMicrophoneResponse));
            var typed = (GetMicrophoneResponse)result;
            CollectionAssert.AreEqual(original.Audio, typed.Audio);
            Assert.AreEqual(original.Device, typed.Device);
            Assert.AreEqual(original.SessionId, typed.SessionId);
            Assert.AreEqual(original.Sequence, typed.Sequence);
            Assert.AreEqual(original.Timestamp, typed.Timestamp);
            Assert.AreEqual(original.Status, typed.Status);
            Assert.AreEqual(original.ErrorCode, typed.ErrorCode);
            Assert.AreEqual(original.ErrorMessage, typed.ErrorMessage);
            Assert.AreEqual(original.Channels, typed.Channels);
        }

        [TestMethod]
        public void RemoteWebcamStatus_RoundTripsEveryDeclaredValue()
        {
            foreach (RemoteWebcamStatus status in Enum.GetValues(typeof(RemoteWebcamStatus)))
            {
                var original = new GetWebcam { Status = status };
                var typed = (GetWebcam)RoundTrip(original);
                Assert.AreEqual(status, typed.Status);
            }
        }

        [TestMethod]
        public void MediaStreamStatus_RoundTripsEveryDeclaredValue()
        {
            foreach (MediaStreamStatus status in Enum.GetValues(typeof(MediaStreamStatus)))
            {
                var original = new GetMicrophoneResponse { Status = status };
                var typed = (GetMicrophoneResponse)RoundTrip(original);
                Assert.AreEqual(status, typed.Status);
            }
        }

        [DataTestMethod]
        [DataRow(999, false)]
        [DataRow(1000, true)]
        [DataRow(48000, true)]
        [DataRow(192000, true)]
        [DataRow(192001, false)]
        public void ValidateBitrate_AcceptsOnlySupportedRange(int bitrate, bool expected)
        {
            Assert.AreEqual(expected, MediaSessionValidation.IsValidBitrate(bitrate));
        }

        [DataTestMethod]
        [DataRow(-1, false)]
        [DataRow(0, true)]
        [DataRow(50, true)]
        [DataRow(100, true)]
        [DataRow(101, false)]
        public void ValidateQuality_AcceptsOnlySupportedRange(int quality, bool expected)
        {
            Assert.AreEqual(expected, MediaSessionValidation.IsValidQuality(quality));
        }

        [DataTestMethod]
        [DataRow(0, false)]
        [DataRow(1, true)]
        [DataRow(6, true)]
        [DataRow(12, true)]
        [DataRow(13, false)]
        public void ValidateFrameBatch_AcceptsOnlySupportedRange(int frameBatch, bool expected)
        {
            Assert.AreEqual(expected, MediaSessionValidation.IsValidFrameBatch(frameBatch));
        }

        [TestMethod]
        public void NormalizeFrameBatch_ClampsToOneThroughTwelve()
        {
            Assert.AreEqual(1, MediaSessionValidation.NormalizeFrameBatch(-5));
            Assert.AreEqual(1, MediaSessionValidation.NormalizeFrameBatch(0));
            Assert.AreEqual(6, MediaSessionValidation.NormalizeFrameBatch(6));
            Assert.AreEqual(12, MediaSessionValidation.NormalizeFrameBatch(13));
            Assert.AreEqual(12, MediaSessionValidation.NormalizeFrameBatch(100));
        }

        [TestMethod]
        public void IsStaleSequence_DetectsDuplicateAndOutOfOrderResponses()
        {
            Assert.IsFalse(MediaSessionValidation.IsStaleSequence(10, 11));
            Assert.IsTrue(MediaSessionValidation.IsStaleSequence(10, 10));
            Assert.IsTrue(MediaSessionValidation.IsStaleSequence(10, 9));
            Assert.IsFalse(MediaSessionValidation.IsStaleSequence(10, 0));
            Assert.IsFalse(MediaSessionValidation.IsStaleSequence(10, -1));
        }

        private static IMessage RoundTrip(IMessage message)
        {
            var payload = PulsarMessagePackSerializer.Serialize(message);
            Assert.IsNotNull(payload);
            Assert.AreNotEqual(0, payload.Length);
            return PulsarMessagePackSerializer.Deserialize(payload);
        }
    }
}
