﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetty.Common.Concurrency
{
    public class DefaultValueTaskPromise: IValueTaskPromise
    {
        private readonly CancellationToken _token;
#if NET
        private readonly TaskCompletionSource _tcs;
#else
        private readonly ManualResetValueTaskSource<object> _tcs;
#endif

        private int v_uncancellable = SharedConstants.False;

        public DefaultValueTaskPromise()
        {
            _token = CancellationToken.None;
#if NET
            _tcs = new TaskCompletionSource();
#else
            _tcs = new ManualResetValueTaskSource<object>();
#endif
        }

        public DefaultValueTaskPromise(object state)
        {
            _token = CancellationToken.None;
#if NET
            _tcs = new TaskCompletionSource(state);
#else
            _tcs = new ManualResetValueTaskSource<object>(state);
#endif
        }

        public DefaultValueTaskPromise(CancellationToken cancellationToken)
        {
            _token= cancellationToken;
        }

        public ValueTask ValueTask
        {
            [MethodImpl(InlineMethod.AggressiveOptimization)]
            get => _tcs.AwaitVoid(_token);
        }

        public bool IsVoid => false;

        public bool IsSuccess => ValueTask.IsCompletedSuccessfully;

        public bool IsCompleted => ValueTask.IsCompleted;

        public bool IsFaulted => ValueTask.IsFaulted;

        public bool IsCanceled => ValueTask.IsCanceled;

       public  Task  Task => ValueTask.AsTask();

        public virtual bool TryComplete()
        {
#if NET
            return _tcs.TrySetResult();
#else
            return _tcs.SetResult(0);
#endif
        }

        public virtual Exception Execption()
        {
#if NET
            return _tcs.GetException(_tcs.Version).SourceException;;
#else
            return _tcs.GetException(_tcs.Version).SourceException;
#endif
        }

        public virtual void Complete()
        {
#if NET
            _tcs.SetResult();
#else
            _tcs.SetResult(0);
#endif
        }
        public virtual void SetCanceled()
        {
            if (SharedConstants.False < (uint)Volatile.Read(ref v_uncancellable)) { return; }
            _tcs.SetCanceled();
        }

        public virtual void SetException(Exception exception)
        {
            if (exception is AggregateException aggregateException)
            {
                SetException(aggregateException.InnerExceptions);
                return;
            }
            _tcs.SetException(exception);
        }

        public virtual void SetException(IEnumerable<Exception> exceptions)
        {
            _tcs.SetException(exceptions.FirstOrDefault());
        }

        public virtual bool TrySetCanceled()
        {
            if (SharedConstants.False < (uint)Volatile.Read(ref v_uncancellable)) { return false; }
              _tcs.SetCanceled();
            return true;
        }

        public virtual bool TrySetException(Exception exception)
        {
            if (exception is AggregateException aggregateException)
            {
                return TrySetException(aggregateException.InnerExceptions);
            }
              _tcs.SetException(exception);
            return true;
        }

        public virtual bool TrySetException(IEnumerable<Exception> exceptions)
        {
              _tcs.SetException(exceptions.FirstOrDefault());
            return true;
        }

        public bool SetUncancellable()
        {
            if (SharedConstants.False >= (uint)Interlocked.CompareExchange(ref v_uncancellable, SharedConstants.True, SharedConstants.False))
            {
                return true;
            }
            return !IsCompleted;
        }

        public override string ToString() => "TaskCompletionSource[status: " + _tcs.GetStatus(_tcs.Version).ToString() + "]";

        public IPromise Unvoid() => this;

        public void Dispose()
        {
            _tcs.Reset();
        }
    }
}
