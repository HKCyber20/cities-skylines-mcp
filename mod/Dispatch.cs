using System;
using System.Collections.Generic;
using System.Threading;
using ColossalFramework;

namespace CS1McpBridge
{
    public enum RunOn { Sim, Main }

    /// <summary>
    /// Marshals work from socket worker threads onto Cities: Skylines' threads and
    /// blocks the caller until the result (or an exception) is ready.
    ///
    /// Two destinations:
    ///   RunOn.Sim  -> simulation thread, via SimulationManager.AddAction.
    ///                 Use for anything that reads/writes simulation state
    ///                 (buildings, citizens, economy, disasters, sim speed).
    ///   RunOn.Main -> main/render thread, via the queue pumped in Threading.OnUpdate.
    ///                 Use for camera and screenshots (render-thread sensitive).
    /// </summary>
    public static class Dispatch
    {
        static readonly object _gate = new object();
        static readonly Queue<Action> _mainQueue = new Queue<Action>();
        static readonly HashSet<PendingJob> _pending = new HashSet<PendingJob>();
        static int _generation;
        [ThreadStatic] static int _requestGeneration;
        [ThreadStatic] static bool _requestBound;

        public static int CurrentGeneration
        {
            get { lock (_gate) return _generation; }
        }

        public static void BindRequest(int generation)
        {
            _requestGeneration = generation;
            _requestBound = true;
        }

        public static void UnbindRequest() { _requestBound = false; }

        sealed class PendingJob
        {
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
            public int Generation;
            public bool Started;
            public bool Cancelled;
        }

        /// <summary>Cancel work that has not started before a city unloads.</summary>
        public static void InvalidatePending()
        {
            lock (_gate)
            {
                _generation++;
                _mainQueue.Clear();
                var cancelled = new List<PendingJob>();
                foreach (var item in _pending)
                {
                    if (item.Started) continue;
                    item.Cancelled = true;
                    item.Done.Set();
                    cancelled.Add(item);
                }
                foreach (var item in cancelled) _pending.Remove(item);
            }
        }

        /// <summary>Drains the main-thread queue. Called every frame from Threading.OnUpdate.</summary>
        public static void PumpMainThread()
        {
            while (true)
            {
                Action job;
                lock (_gate)
                {
                    if (_mainQueue.Count == 0) return;
                    job = _mainQueue.Dequeue();
                }
                try { job(); }
                catch (Exception e) { Log.Error(e); }
            }
        }

        public static T Run<T>(RunOn thread, Func<T> work, int timeoutMs = 5000)
        {
            T result = default(T);
            Exception captured = null;
            var item = new PendingJob();

            Action job = () =>
            {
                lock (_gate)
                {
                    if (item.Cancelled || item.Generation != _generation)
                    {
                        item.Cancelled = true;
                        _pending.Remove(item);
                        item.Done.Set();
                        return;
                    }
                    item.Started = true;
                }
                try { result = work(); }
                catch (Exception e) { captured = e; }
                finally
                {
                    item.Done.Set();
                    lock (_gate) _pending.Remove(item);
                }
            };

            lock (_gate)
            {
                item.Generation = _requestBound ? _requestGeneration : _generation;
                if (item.Generation != _generation)
                    throw new OperationCanceledException("Request belongs to an unloaded city.");
                _pending.Add(item);
                if (thread == RunOn.Main) _mainQueue.Enqueue(job);
            }
            if (thread == RunOn.Sim)
            {
                try { Singleton<SimulationManager>.instance.AddAction(job); }
                catch { lock (_gate) _pending.Remove(item); throw; }
            }

            if (!item.Done.WaitOne(timeoutMs))
            {
                lock (_gate)
                {
                    if (!item.Started)
                    {
                        item.Cancelled = true;
                        _pending.Remove(item);
                    }
                    else throw new TimeoutException("Command timed out on the " + thread +
                        " thread after starting; outcome unknown, check game state before retrying.");
                }
                throw new TimeoutException("Command timed out on the " + thread +
                    " thread before starting; queued work was cancelled.");
            }
            lock (_gate)
                if (item.Cancelled)
                    throw new OperationCanceledException("City unloaded before the " + thread + " command started.");
            if (captured != null) throw captured;
            return result;
        }
    }
}
