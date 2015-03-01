﻿using System;
using System.Threading;
using System.Threading.Tasks;
using NetMQ.Devices;
using NUnit.Framework;

namespace NetMQ.Tests.Devices
{
    public abstract class DeviceTestBase<TDevice, TWorkerSocket>
        where TDevice : IDevice
        where TWorkerSocket : NetMQSocket
    {

        protected const string Frontend = "inproc://front.addr";
        protected const string Backend = "inproc://back.addr";

        protected readonly Random Random = new Random();

        protected NetMQContext Context;
        protected TDevice Device;

        protected Func<NetMQContext, TDevice> CreateDevice;

        protected Func<NetMQContext, NetMQSocket> CreateClientSocket;
        protected abstract TWorkerSocket CreateWorkerSocket(NetMQContext context);

        protected int WorkerReceiveCount;

        private CancellationTokenSource m_workerCancellationSource;
        private CancellationToken m_workerCancellationToken;

        protected ManualResetEvent WorkerDone;

        [TestFixtureSetUp]
        protected virtual void Initialize()
        {
            WorkerReceiveCount = 0;
            WorkerDone = new ManualResetEvent(false);
            m_workerCancellationSource = new CancellationTokenSource();
            m_workerCancellationToken = m_workerCancellationSource.Token;

            Context = NetMQContext.Create();
            SetupTest();
            Device = CreateDevice(Context);
            Device.Start();

            StartWorker();
        }

        protected abstract void SetupTest();

        [TestFixtureTearDown]
        protected virtual void Cleanup()
        {
            Context.Dispose();
        }

        protected abstract void DoWork(NetMQSocket socket);

        protected virtual void WorkerSocketAfterConnect(TWorkerSocket socket) { }

        protected void StartWorker()
        {
            Task.Factory.StartNew(() =>
            {
                using (var socket = CreateWorkerSocket(Context))
                {
                    socket.Connect(Backend);
                    WorkerSocketAfterConnect(socket);

                    socket.ReceiveReady += (s, a) => { };
                    socket.SendReady += (s, a) => { };

                    while (!m_workerCancellationToken.IsCancellationRequested)
                    {
                        var has = socket.Poll(TimeSpan.FromMilliseconds(1));

                        if (!has)
                        {
                            Thread.Sleep(1);
                            continue;
                        }

                        DoWork(socket);
                        Interlocked.Increment(ref WorkerReceiveCount);
                    }
                }

                WorkerDone.Set();
            }, TaskCreationOptions.LongRunning);
        }

        protected void StopWorker()
        {
            m_workerCancellationSource.Cancel();
            WorkerDone.WaitOne();
        }

        protected abstract void DoClient(int id, NetMQSocket socket);

        protected void StartClient(int id, int waitBeforeSending = 0)
        {
            Task.Factory.StartNew(() =>
            {
                using (var client = CreateClientSocket(Context))
                {
                    client.Connect(Frontend);

                    if (waitBeforeSending > 0)
                        Thread.Sleep(waitBeforeSending);

                    DoClient(id, client);
                }
            });
        }

        protected void SleepUntilWorkerReceives(int messages, TimeSpan maxWait)
        {
            var start = DateTime.UtcNow + maxWait;
            while (WorkerReceiveCount != messages)
            {
                Thread.Sleep(1);

                if (DateTime.UtcNow <= start)
                    continue;

                Console.WriteLine("Max wait time exceeded for worker messages");
                return;
            }
        }
    }
}