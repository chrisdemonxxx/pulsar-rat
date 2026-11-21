using Pulsar.Common.Messages.Other;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pulsar.Common.Networking
{
    /// <summary>
    /// Tracks responses while preventing timed-out waiters from consuming a later response.
    /// </summary>
    public sealed class MessageWaiterRegistry : IDisposable
    {
        private readonly ConcurrentDictionary<Type, ConcurrentDictionary<long, Waiter>> _waiters =
            new ConcurrentDictionary<Type, ConcurrentDictionary<long, Waiter>>();
        private long _nextWaiterId;
        private int _disposed;
        private int _pending;
        private const int MaxPendingWaiters = 1024;

        public Task<TMessage> WaitForAsync<TMessage>(TimeSpan timeout, Func<TMessage, bool> matches = null)
            where TMessage : class, IMessage
        {
            ThrowIfDisposed();

            var completion = new TaskCompletionSource<TMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.Increment(ref _pending) > MaxPendingWaiters)
            {
                Interlocked.Decrement(ref _pending);
                completion.TrySetResult(null);
                return completion.Task;
            }

            var waiter = new MessageWaiter<TMessage>(completion, matches);
            var waiters = _waiters.GetOrAdd(typeof(TMessage),
                _ => new ConcurrentDictionary<long, Waiter>());
            var id = Interlocked.Increment(ref _nextWaiterId);

            waiters[id] = waiter;

            if (timeout > Timeout.InfiniteTimeSpan && timeout != TimeSpan.Zero)
            {
                Task.Delay(timeout).ContinueWith(_ =>
                {
                    if (waiters.TryRemove(id, out var expired))
                    {
                        Interlocked.Decrement(ref _pending);
                        expired.Cancel();
                    }
                }, TaskScheduler.Default);
            }

            if (Volatile.Read(ref _disposed) != 0)
                Cancel(waiters, id, waiter);

            return completion.Task;
        }

        public bool Dispatch(IMessage message)
        {
            if (message == null || !_waiters.TryGetValue(message.GetType(), out var waiters))
                return false;

            foreach (var entry in waiters.OrderBy(entry => entry.Key))
            {
                if (!entry.Value.Matches(message))
                    continue;

                if (waiters.TryRemove(entry.Key, out var waiter))
                {
                    Interlocked.Decrement(ref _pending);
                    if (waiter.TryComplete(message))
                        return true;
                }
            }

            return false;
        }

        public void CancelAll()
        {
            foreach (var waiters in _waiters.Values)
            {
                foreach (var id in waiters.Keys.ToArray())
                    if (waiters.TryRemove(id, out var waiter))
                    {
                        Interlocked.Decrement(ref _pending);
                        waiter.Cancel();
                    }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                CancelAll();
        }

        private void Cancel(ConcurrentDictionary<long, Waiter> waiters, long id, Waiter waiter)
        {
            if (waiters.TryRemove(id, out var removed))
            {
                Interlocked.Decrement(ref _pending);
                removed.Cancel();
            }
            else
                waiter.Cancel();
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(GetType().FullName);
        }

        private abstract class Waiter
        {
            public abstract bool Matches(IMessage message);
            public abstract bool TryComplete(IMessage message);
            public abstract void Cancel();
        }

        private sealed class MessageWaiter<TMessage> : Waiter
            where TMessage : class, IMessage
        {
            private readonly TaskCompletionSource<TMessage> _completion;
            private readonly Func<TMessage, bool> _matches;

            public MessageWaiter(TaskCompletionSource<TMessage> completion, Func<TMessage, bool> matches)
            {
                _completion = completion;
                _matches = matches;
            }

            public override bool Matches(IMessage message)
            {
                if (!(message is TMessage typedMessage))
                    return false;

                try
                {
                    return _matches == null || _matches(typedMessage);
                }
                catch
                {
                    return false;
                }
            }

            public override bool TryComplete(IMessage message)
            {
                return message is TMessage typedMessage && _completion.TrySetResult(typedMessage);
            }

            public override void Cancel()
            {
                _completion.TrySetResult(null);
            }
        }
    }
}
