using System;
using System.Collections.Generic;
using System.Threading;

namespace DoctorWho.VoxelUniverse.Generation
{
    public sealed class VoxelJobScheduler : IDisposable
    {
        private sealed class Job
        {
            public int priority;
            public long sequence;
            public Func<object> work;
            public Action<object> complete;
        }

        private readonly object sync = new object();
        private readonly List<Job> jobs = new List<Job>();
        private readonly Queue<Action> mainThreadCallbacks = new Queue<Action>();
        private readonly AutoResetEvent signal = new AutoResetEvent(false);
        private readonly Thread[] workers;
        private bool stopping;
        private long nextSequence;
        private int activeWorkers;

        public VoxelJobScheduler(int workerCount)
        {
            workers = new Thread[Math.Max(1, workerCount)];
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = new Thread(WorkerLoop);
                workers[i].IsBackground = true;
                workers[i].Name = "VoxelUniverse Worker " + i;
                workers[i].Start();
            }
        }

        public int QueuedCount
        {
            get { lock (sync) return jobs.Count; }
        }

        public int ActiveWorkerCount
        {
            get { return Interlocked.CompareExchange(ref activeWorkers, 0, 0); }
        }

        public void Schedule(int priority, Func<object> work, Action<object> complete)
        {
            if (work == null) throw new ArgumentNullException("work");
            lock (sync)
            {
                if (stopping) return;
                Job job = new Job
                {
                    priority = priority,
                    sequence = nextSequence++,
                    work = work,
                    complete = complete
                };
                int index = jobs.BinarySearch(job, JobComparer.Instance);
                if (index < 0) index = ~index;
                jobs.Insert(index, job);
            }
            signal.Set();
        }

        public int PumpMainThread(int budget)
        {
            int completed = 0;
            while (completed < Math.Max(1, budget))
            {
                Action callback = null;
                lock (mainThreadCallbacks)
                {
                    if (mainThreadCallbacks.Count > 0)
                        callback = mainThreadCallbacks.Dequeue();
                }
                if (callback == null) break;
                callback();
                completed++;
            }
            return completed;
        }

        private void WorkerLoop()
        {
            while (true)
            {
                Job job = null;
                lock (sync)
                {
                    if (stopping) return;
                    if (jobs.Count > 0)
                    {
                        job = jobs[0];
                        jobs.RemoveAt(0);
                    }
                }

                if (job == null)
                {
                    signal.WaitOne(100);
                    continue;
                }

                object result = null;
                Exception failure = null;
                Interlocked.Increment(ref activeWorkers);
                try
                {
                    result = job.work();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    Interlocked.Decrement(ref activeWorkers);
                }

                if (job.complete != null)
                {
                    object capturedResult = result;
                    Exception capturedFailure = failure;
                    lock (mainThreadCallbacks)
                    {
                        mainThreadCallbacks.Enqueue(delegate
                        {
                            if (capturedFailure != null)
                                UnityEngine.Debug.LogException(capturedFailure);
                            else
                                job.complete(capturedResult);
                        });
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                stopping = true;
                jobs.Clear();
            }
            signal.Set();
            for (int i = 0; i < workers.Length; i++)
            {
                if (workers[i] != null && workers[i].IsAlive)
                    workers[i].Join(250);
            }
            signal.Dispose();
        }

        private sealed class JobComparer : IComparer<Job>
        {
            public static readonly JobComparer Instance = new JobComparer();

            public int Compare(Job a, Job b)
            {
                int priority = a.priority.CompareTo(b.priority);
                return priority != 0 ? priority : a.sequence.CompareTo(b.sequence);
            }
        }
    }
}
