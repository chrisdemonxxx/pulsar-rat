using Pulsar.Common.Messages.Other;
using Pulsar.Common.Networking;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Pulsar.Common.Messages
{
    /// <summary>
    /// Handles registrations of <see cref="IMessageProcessor"/>s and processing of <see cref="IMessage"/>s.
    /// </summary>
    public static class MessageHandler
    {
        /// <summary>
        /// Raised when a received message has no processor eligible to handle it.
        /// </summary>
        public static event Action<ISender, IMessage> UnsupportedMessageReceived;

        /// <summary>
        /// List of registered <see cref="IMessageProcessor"/>s.
        /// </summary>
        private static readonly List<IMessageProcessor> Processors = new List<IMessageProcessor>();

        /// <summary>
        /// Used in lock statements to synchronize access to <see cref="Processors"/> between threads.
        /// </summary>
        private static readonly object SyncLock = new object();
        private const int MaxRegisteredProcessors = 512;
        private const int MaxQueuedBatchesPerSender = 256;
        private static readonly ConditionalWeakTable<ISender, SenderDispatchQueue> SenderQueues = new ConditionalWeakTable<ISender, SenderDispatchQueue>();

        /// <summary>
        /// Registers a <see cref="IMessageProcessor"/> to the available <see cref="Processors"/>.
        /// </summary>
        /// <param name="proc">The <see cref="IMessageProcessor"/> to register.</param>
        public static void Register(IMessageProcessor proc)
        {
            lock (SyncLock)
            {
                if (Processors.Contains(proc)) return;
                if (Processors.Count >= MaxRegisteredProcessors)
                    throw new InvalidOperationException("The message processor registry is full.");
                Processors.Add(proc);
            }
        }

        /// <summary>
        /// Unregisters a <see cref="IMessageProcessor"/> from the available <see cref="Processors"/>.
        /// </summary>
        /// <param name="proc"></param>
        public static void Unregister(IMessageProcessor proc)
        {
            lock (SyncLock)
            {
                Processors.Remove(proc);
            }
        }

        /// <summary>
        /// Forwards the received <see cref="IMessage"/> to the appropriate <see cref="IMessageProcessor"/>s to execute it.
        /// </summary>
        /// <param name="sender">The sender of the message.</param>
        /// <param name="msg">The received message.</param>
        /// <param name="dispatchAsync">When true, executes each handler on the thread pool to avoid blocking the caller.</param>
        public static void Process(ISender sender, IMessage msg, bool dispatchAsync = true)
        {
            if (msg == null || sender == null)
            {
                return;
            }

            List<IMessageProcessor> availableProcessors;
            lock (SyncLock)
            {
                availableProcessors = Processors.Where(processor =>
                {
                    try { return processor.CanExecute(msg) && processor.CanExecuteFrom(sender); }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MessageHandler] Processor selection failed: {ex.Message}");
                        return false;
                    }
                }).ToList();
            }

            var processors = availableProcessors as ICollection<IMessageProcessor>;
            if (processors == null || processors.Count == 0)
            {
                Debug.WriteLine($"[MessageHandler] Unsupported message '{msg.GetType().FullName}' from '{sender?.GetType().FullName ?? "unknown"}'; no registered processor can execute it.");
                try
                {
                    UnsupportedMessageReceived?.Invoke(sender, msg);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MessageHandler] Unsupported-message diagnostic subscriber threw: {ex}");
                }
                return;
            }

            foreach (var executor in processors)
            {
                if (dispatchAsync)
                {
                    QueueProcessorExecution(availableProcessors, sender, msg);
                    return;
                }
                else
                {
                    ExecuteProcessorSafely(executor, sender, msg);
                }
            }
        }

        private static void QueueProcessorExecution(List<IMessageProcessor> processors, ISender sender, IMessage message)
        {
            var batch = new MessageDispatchContext
            {
                Processors = processors,
                Sender = sender,
                Message = message
            };

            var queue = SenderQueues.GetValue(sender, _ => new SenderDispatchQueue());
            if (!queue.Enqueue(batch))
                sender.Disconnect();
        }

        private static void ExecuteProcessorSafely(IMessageProcessor processor, ISender sender, IMessage message)
        {
            try
            {
                processor.Execute(sender, message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MessageHandler] Processor '{processor?.GetType().Name}' threw an exception: {ex}");
            }
        }

        private static readonly WaitCallback DispatchCallback = state =>
        {
            if (state is SenderDispatchQueue queue)
            {
                queue.Invoke();
            }
        };

        private sealed class MessageDispatchContext
        {
            public List<IMessageProcessor> Processors;
            public ISender Sender;
            public IMessage Message;

            public void Invoke()
            {
                foreach (var processor in Processors ?? Enumerable.Empty<IMessageProcessor>())
                {
                    ExecuteProcessorSafely(processor, Sender, Message);
                }
                Processors = null;
                Sender = null;
                Message = null;
            }
        }

        private sealed class SenderDispatchQueue
        {
            private readonly Queue<MessageDispatchContext> _items = new Queue<MessageDispatchContext>();
            private bool _running;

            public bool Enqueue(MessageDispatchContext item)
            {
                lock (_items)
                {
                    if (_items.Count >= MaxQueuedBatchesPerSender)
                        return false;
                    _items.Enqueue(item);
                    if (_running)
                        return true;
                    _running = true;
                }

                ThreadPool.UnsafeQueueUserWorkItem(DispatchCallback, this);
                return true;
            }

            public void Invoke()
            {
                while (true)
                {
                    MessageDispatchContext item;
                    lock (_items)
                    {
                        if (_items.Count == 0)
                        {
                            _running = false;
                            return;
                        }
                        item = _items.Dequeue();
                    }

                    item.Invoke();
                }
            }
        }
    }
}
