using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ValheimCliBridge
{
    public sealed class Dispatcher
    {
        private sealed class Job
        {
            public Request Request;
            public long Expires;
            public readonly object Gate = new object();
            public bool Started;
            public bool Cancelled;
            public Response Result;
        }
        private readonly Queue<Job> queue = new Queue<Job>();
        private readonly object gate = new object();
        private bool stopped;
        private readonly Func<Request, Response> execute;
        public Dispatcher(Func<Request, Response> execute) { this.execute = execute; }
        public Response Run(Request request, int timeoutMs = 2000)
        {
            var job = new Job { Request = request, Expires = Stopwatch.GetTimestamp() + timeoutMs * Stopwatch.Frequency / 1000 };
            lock (gate)
            {
                if (stopped) return Response.Error(request.id, "Bridge stopped", "cancelled");
                if (queue.Count >= 8) return Response.Error(request.id, "Bridge busy", "cancelled");
                queue.Enqueue(job);
            }
            lock (job.Gate)
            {
                while (job.Result == null)
                {
                    var remaining = (job.Expires - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency;
                    if (remaining <= 0) break;
                    Monitor.Wait(job.Gate, (int)Math.Min(remaining, int.MaxValue));
                }
                if (job.Result != null) return job.Result;
                if (job.Started) return Response.Error(request.id, "Execution started; outcome unknown. Do not retry automatically.");
                job.Cancelled = true;
                return Response.Error(request.id, "Expired before execution; action cancelled", "cancelled");
            }
        }
        public void Pump()
        {
            Job job;
            lock (gate)
            {
                if (stopped || queue.Count == 0) return;
                job = queue.Dequeue();
            }
            lock (job.Gate)
            {
                if (job.Cancelled || Stopwatch.GetTimestamp() >= job.Expires)
                {
                    job.Result = Response.Error(job.Request.id, "Expired before execution; action cancelled", "cancelled");
                    Monitor.PulseAll(job.Gate);
                    return;
                }
                job.Started = true;
            }
            Response result;
            try { result = execute(job.Request); }
            catch (Exception error) { result = Response.Error(job.Request.id, "Game operation failed: " + error.GetType().Name); }
            lock (job.Gate) { job.Result = result; Monitor.PulseAll(job.Gate); }
        }
        public void Stop()
        {
            lock (gate)
            {
                stopped = true;
                while (queue.Count > 0)
                {
                    var job = queue.Dequeue();
                    lock (job.Gate)
                    {
                        job.Cancelled = true;
                        job.Result = Response.Error(job.Request.id, "Bridge stopped", "cancelled");
                        Monitor.PulseAll(job.Gate);
                    }
                }
            }
        }
    }
}
