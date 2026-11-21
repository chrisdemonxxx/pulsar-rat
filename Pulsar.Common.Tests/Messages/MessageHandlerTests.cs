using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Networking;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Pulsar.Common.Tests.Messages
{
    [TestClass]
    public class MessageHandlerTests
    {
        private const int AsyncTimeoutMs = 5000;

        [TestMethod]
        public void Process_SyncDispatch_ExecutesInline()
        {
            var processor = new RecordingProcessor();
            var sender = new FakeSender();
            var message = new SetStatus { Message = "sync" };

            MessageHandler.Register(processor);
            try
            {
                MessageHandler.Process(sender, message, dispatchAsync: false);

                // Synchronous dispatch must have completed before Process() returns.
                Assert.AreEqual(1, processor.ExecuteCount);
                Assert.AreSame(message, processor.LastMessage);
                Assert.AreSame(sender, processor.LastSender);
            }
            finally
            {
                MessageHandler.Unregister(processor);
            }
        }

        [TestMethod]
        public void Process_AsyncDispatch_ExecutesOnThreadPool()
        {
            using (var executed = new ManualResetEventSlim(false))
            {
                var processor = new RecordingProcessor(onExecute: executed.Set);
                var sender = new FakeSender();
                var message = new SetStatus { Message = "async" };

                MessageHandler.Register(processor);
                try
                {
                    MessageHandler.Process(sender, message); // dispatchAsync defaults to true

                    Assert.IsTrue(executed.Wait(AsyncTimeoutMs), "Async dispatch did not execute within the timeout.");
                    Assert.AreEqual(1, processor.ExecuteCount);
                    Assert.AreSame(message, processor.LastMessage);
                    Assert.AreSame(sender, processor.LastSender);
                    Assert.IsTrue(processor.ExecutedOnThreadPool, "Async dispatch must run on a thread-pool thread.");
                    Assert.AreNotEqual(Environment.CurrentManagedThreadId, processor.ExecutingThreadId);
                }
                finally
                {
                    MessageHandler.Unregister(processor);
                }
            }
        }

        [TestMethod]
        public void Process_AfterUnregister_StopsDelivery()
        {
            var processor = new RecordingProcessor();
            var sender = new FakeSender();

            MessageHandler.Register(processor);
            MessageHandler.Unregister(processor);

            MessageHandler.Process(sender, new SetStatus { Message = "dropped" }, dispatchAsync: false);

            Assert.AreEqual(0, processor.ExecuteCount);
        }

        [TestMethod]
        public void Process_RegisterTwice_DeliversOnlyOnce()
        {
            var processor = new RecordingProcessor();
            var sender = new FakeSender();

            MessageHandler.Register(processor);
            MessageHandler.Register(processor); // duplicate registration must be ignored
            try
            {
                MessageHandler.Process(sender, new SetStatus { Message = "dup" }, dispatchAsync: false);

                Assert.AreEqual(1, processor.ExecuteCount);
            }
            finally
            {
                MessageHandler.Unregister(processor);
            }
        }

        [TestMethod]
        public void Process_ProcessorCannotExecuteMessage_IsSkipped()
        {
            var gated = new RecordingProcessor(canExecute: m => false);
            var open = new RecordingProcessor();
            var sender = new FakeSender();

            MessageHandler.Register(gated);
            MessageHandler.Register(open);
            try
            {
                MessageHandler.Process(sender, new SetStatus { Message = "gated" }, dispatchAsync: false);

                Assert.AreEqual(0, gated.ExecuteCount);
                Assert.AreEqual(1, open.ExecuteCount);
            }
            finally
            {
                MessageHandler.Unregister(gated);
                MessageHandler.Unregister(open);
            }
        }

        [TestMethod]
        public void Process_ProcessorCannotExecuteFromSender_IsSkipped()
        {
            var gated = new RecordingProcessor(canExecuteFrom: s => false);
            var sender = new FakeSender();

            MessageHandler.Register(gated);
            try
            {
                MessageHandler.Process(sender, new SetStatus { Message = "gated sender" }, dispatchAsync: false);

                Assert.AreEqual(0, gated.ExecuteCount);
            }
            finally
            {
                MessageHandler.Unregister(gated);
            }
        }

        [TestMethod]
        public void Process_ProcessorThrows_ExceptionIsSwallowed()
        {
            var throwing = new RecordingProcessor(onExecute: () => { throw new InvalidOperationException("boom"); });
            var surviving = new RecordingProcessor();
            var sender = new FakeSender();

            MessageHandler.Register(throwing);
            MessageHandler.Register(surviving);
            try
            {
                // Must not throw: MessageHandler catches processor exceptions (sync path).
                MessageHandler.Process(sender, new SetStatus { Message = "fault" }, dispatchAsync: false);

                Assert.AreEqual(1, throwing.ExecuteCount);
                Assert.AreEqual(1, surviving.ExecuteCount);
            }
            finally
            {
                MessageHandler.Unregister(throwing);
                MessageHandler.Unregister(surviving);
            }
        }

        [TestMethod]
        public void Process_AsyncDispatch_PreservesOrderPerSender()
        {
            using (var release = new ManualResetEventSlim(false))
            using (var firstStarted = new ManualResetEventSlim(false))
            using (var completed = new ManualResetEventSlim(false))
            {
                var observed = new List<string>();
                var orderLock = new object();
                var sender = new FakeSender();
                var orderedProcessor = new OrderedProcessor(firstStarted, release, completed, observed, orderLock);

                MessageHandler.Register(orderedProcessor);
                try
                {
                    MessageHandler.Process(sender, new SetStatus { Message = "first" });
                    Assert.IsTrue(firstStarted.Wait(AsyncTimeoutMs));
                    MessageHandler.Process(sender, new SetStatus { Message = "second" });
                    MessageHandler.Process(sender, new SetStatus { Message = "third" });
                    release.Set();
                    Assert.IsTrue(completed.Wait(AsyncTimeoutMs));

                    lock (orderLock)
                        CollectionAssert.AreEqual(new[] { "first", "second", "third" }, observed);
                }
                finally
                {
                    release.Set();
                    MessageHandler.Unregister(orderedProcessor);
                }
            }
        }

        [TestMethod]
        public void Process_WithoutEligibleProcessor_RaisesUnsupportedDiagnostic()
        {
            var sender = new FakeSender();
            var message = new SetStatus { Message = "unsupported" };
            ISender reportedSender = null;
            IMessage reportedMessage = null;
            Action<ISender, IMessage> onUnsupported = (s, m) =>
            {
                reportedSender = s;
                reportedMessage = m;
            };

            MessageHandler.UnsupportedMessageReceived += onUnsupported;
            try
            {
                MessageHandler.Process(sender, message, dispatchAsync: false);

                Assert.AreSame(sender, reportedSender);
                Assert.AreSame(message, reportedMessage);
            }
            finally
            {
                MessageHandler.UnsupportedMessageReceived -= onUnsupported;
            }
        }

        [TestMethod]
        public void Process_WithEligibleProcessor_DoesNotRaiseUnsupportedDiagnostic()
        {
            var processor = new RecordingProcessor();
            var diagnosticCount = 0;
            Action<ISender, IMessage> onUnsupported = (s, m) => diagnosticCount++;

            MessageHandler.Register(processor);
            MessageHandler.UnsupportedMessageReceived += onUnsupported;
            try
            {
                MessageHandler.Process(new FakeSender(), new SetStatus(), dispatchAsync: false);

                Assert.AreEqual(0, diagnosticCount);
            }
            finally
            {
                MessageHandler.UnsupportedMessageReceived -= onUnsupported;
                MessageHandler.Unregister(processor);
            }
        }

        private sealed class FakeSender : ISender
        {
            public int SendCount;
            public int DisconnectCount;

            public void Send<T>(T message) where T : IMessage
            {
                SendCount++;
            }

            public void Disconnect()
            {
                DisconnectCount++;
            }
        }

        private sealed class RecordingProcessor : IMessageProcessor
        {
            private readonly Func<IMessage, bool> _canExecute;
            private readonly Func<ISender, bool> _canExecuteFrom;
            private readonly Action _onExecute;

            public int ExecuteCount;
            public IMessage LastMessage;
            public ISender LastSender;
            public bool ExecutedOnThreadPool;
            public int ExecutingThreadId = -1;

            public RecordingProcessor(Func<IMessage, bool> canExecute = null,
                Func<ISender, bool> canExecuteFrom = null,
                Action onExecute = null)
            {
                _canExecute = canExecute ?? (m => true);
                _canExecuteFrom = canExecuteFrom ?? (s => true);
                _onExecute = onExecute;
            }

            public bool CanExecute(IMessage message)
            {
                return _canExecute(message);
            }

            public bool CanExecuteFrom(ISender sender)
            {
                return _canExecuteFrom(sender);
            }

            public void Execute(ISender sender, IMessage message)
            {
                Interlocked.Increment(ref ExecuteCount);
                LastSender = sender;
                LastMessage = message;
                ExecutedOnThreadPool = Thread.CurrentThread.IsThreadPoolThread;
                ExecutingThreadId = Thread.CurrentThread.ManagedThreadId;
                if (_onExecute != null)
                    _onExecute();
            }
        }

        private sealed class OrderedProcessor : IMessageProcessor
        {
            private readonly ManualResetEventSlim _firstStarted;
            private readonly ManualResetEventSlim _release;
            private readonly ManualResetEventSlim _completed;
            private readonly List<string> _observed;
            private readonly object _sync;

            public OrderedProcessor(ManualResetEventSlim firstStarted, ManualResetEventSlim release,
                ManualResetEventSlim completed, List<string> observed, object sync)
            {
                _firstStarted = firstStarted;
                _release = release;
                _completed = completed;
                _observed = observed;
                _sync = sync;
            }

            public bool CanExecute(IMessage message) => message is SetStatus;
            public bool CanExecuteFrom(ISender sender) => true;

            public void Execute(ISender sender, IMessage message)
            {
                var value = ((SetStatus)message).Message;
                lock (_sync)
                    _observed.Add(value);
                if (value == "first")
                {
                    _firstStarted.Set();
                    _release.Wait();
                }
                if (value == "third")
                    _completed.Set();
            }
        }
    }
}
