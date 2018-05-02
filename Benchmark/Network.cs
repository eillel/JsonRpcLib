﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Attributes.Jobs;

namespace Benchmark
{
    [CoreJob]
    [MemoryDiagnoser]
    public class ResponseTime
    {
        const int PORT = 53438;

        private SimpleEchoServer _server;
        private TcpClient _client;
        private byte[] _request = Encoding.UTF8.GetBytes("Hello\n");
        private Socket _socketClient;

        [GlobalSetup]
        public void Setup()
        {
            _server = new SimpleEchoServer(PORT);
            _client = new TcpClient(IPAddress.Loopback.ToString(), PORT);

            _socketClient = new Socket(SocketType.Stream, ProtocolType.Tcp);
            _socketClient.Connect(IPAddress.Loopback, PORT);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _socketClient.Dispose();
            _client.Dispose();
            _server.Dispose();
        }

        [Benchmark(Baseline = true)]
        public void UsingTcpClient()
        {
            Span<byte> buffer = stackalloc byte[32];
            _client.Client.Send(_request);
            int n = _client.Client.Receive(buffer);
            Debug.Assert(n == _request.Length);
        }

        [Benchmark]
        public void UsingSocketClient()
        {
            Span<byte> buffer = stackalloc byte[32];
            _socketClient.Send(_request);
            int n = _socketClient.Receive(buffer);
            Debug.Assert(n == _request.Length);
        }
    }
}