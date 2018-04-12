﻿using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace JsonRpcLib.Server
{
    public partial class JsonRpcServer
    {
        internal class ClientConnection : IClient
        {
            private const int MAX_BUFFER_SIZE = 1 * 1024 * 1024;

            public int Id { get; }
            public bool IsConnected { get; private set; }
            public string Address { get; }
            public Encoding Encoding { get; internal set; }

            private readonly Stream _stream;
            private readonly Func<ClientConnection, string, bool> _process;
            private byte[] _buffer = new byte[32];
            private int _receivePosition;

            public ClientConnection(int id, string address, Stream stream, Func<ClientConnection, string, bool> process)
            {
                Id = id;
                Address = address;
                _stream = stream;
                _process = process;
                IsConnected = true;

                BeginRead();
            }

            private void BeginRead() => _stream.BeginRead(_buffer, _receivePosition, _buffer.Length - _receivePosition, ReadCompleted, this);

            private void ReadCompleted(IAsyncResult ar)
            {
                try
                {
                    int readCount = _stream.EndRead(ar);
                    if (readCount <= 0)
                    {
                        KillConnection();
                        return;
                    }
                    _receivePosition += readCount;
                    if (_receivePosition == _buffer.Length)
                    {
                        if (_buffer.Length == MAX_BUFFER_SIZE)
                        {
                            // Buffer is to large, kill client
                            // Don't know what to do here really !?!
                            KillConnection();
                            return;
                        }
                        // Grow buffer
                        var newBuffer = new byte[_buffer.Length * 2];
                        Array.Copy(_buffer, newBuffer, _buffer.Length);
                        _buffer = newBuffer;
                    }

                    if (_receivePosition > 1 && _buffer[_receivePosition - 1] == '\n')
                    {
                        var message = Encoding.GetString(_buffer, 0, _receivePosition);
                        if (!_process(this, message))
                        {
                            KillConnection();
                            return;
                        }
                    }

                    BeginRead();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception in ClientConnection.ReadCompleted: " + ex.Message);
                    KillConnection();
                }
            }

            virtual public bool Write(string data)
            {
                Debug.WriteLine($"#{Id} TX: {data}");

                try
                {
                    var bytes = Encoding.GetBytes(data);
                    _stream.Write(bytes, 0, bytes.Length);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Exception in ClientConnection.Write(): " + ex.Message);
                    KillConnection();
                    return false;
                }
            }

            public void Dispose() => KillConnection();

            private void KillConnection()
            {
                if (!IsConnected)
                    return;

                _stream.Close();
                IsConnected = false;
                _process(this, null);
            }
        }
    }
}