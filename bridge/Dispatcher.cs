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
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
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
                if (stopped) return Response.Error(request.id, "Bridge stopped");
                if (queue.Count >= 8) return Response.Error(request.id, "Bridge busy");
                queue.Enqueue(job);
            }
            if (job.Done.Wait(timeoutMs)) return job.Result;
            lock (job.Gate)
            {
                if (job.Result != null) return job.Result;
                if (job.Started) return Response.Error(request.id, "Execution started; outcome unknown. Do not retry automatically.");
                job.Cancelled = true;
                return Response.Error(request.id, "Expired before execution; action cancelled");
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
                    job.Result = Response.Error(job.Request.id, "Expired before execution; action cancelled");
                    job.Done.Set();
                    return;
                }
                job.Started = true;
            }
            Response result;
            try { result = execute(job.Request); }
            catch (Exception error) { result = Response.Error(job.Request.id, "Game operation failed: " + error.GetType().Name); }
            lock (job.Gate) { job.Result = result; job.Done.Set(); }
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
                        job.Result = Response.Error(job.Request.id, "Bridge stopped");
                        job.Done.Set();
                    }
                }
            }
        }
    }
}
