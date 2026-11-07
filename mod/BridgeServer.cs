using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CS1McpBridge
{
    /// <summary>
    /// Loopback TCP server speaking newline-delimited JSON (see PROTOCOL.md).
    /// One JSON request per line in, one JSON response per line out. Multiple
    /// concurrent clients are allowed; each gets its own worker thread.
    /// </summary>
    public static class BridgeServer
    {
        const int DefaultPort = 50545;          // override with env CS1MCP_PORT
        static TcpListener _listener;
        static Thread _accept;
        static volatile bool _running;
        static readonly object _clientsGate = new object();
        static readonly HashSet<TcpClient> _clients = new HashSet<TcpClient>();

        public static void Start()
        {
            if (_running) return;

            int port = DefaultPort;
            var env = Environment.GetEnvironmentVariable("CS1MCP_PORT");
            if (!string.IsNullOrEmpty(env)) int.TryParse(env, out port);

            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
            }
            catch (Exception e)
            {
                Log.Error("failed to bind 127.0.0.1:" + port + " — " + e.Message);
                return;
            }

            _running = true;
            var listener = _listener;
            _accept = new Thread(() => AcceptLoop(listener)) { IsBackground = true, Name = "CS1McpBridge.Accept" };
            _accept.Start();
            Log.Info("listening on 127.0.0.1:" + port);
        }

        public static void Stop()
        {
            if (!_running) return;
            _running = false;
            Dispatch.InvalidatePending();
            try { _listener?.Stop(); } catch { }
            _listener = null;
            lock (_clientsGate)
            {
                foreach (var client in _clients) try { client.Close(); } catch { }
                _clients.Clear();
            }
            Log.Info("stopped");
        }

        static void AcceptLoop(TcpListener listener)
        {
            while (_running && Object.ReferenceEquals(_listener, listener))
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch { if (_running && Object.ReferenceEquals(_listener, listener)) continue; else break; }

                lock (_clientsGate)
                {
                    if (!_running || !Object.ReferenceEquals(_listener, listener)) { client.Close(); break; }
                    _clients.Add(client);
                }

                int generation = Dispatch.CurrentGeneration;
                var worker = new Thread(() => Serve(client, listener, generation)) { IsBackground = true };
                worker.Start();
            }
        }

        static void Serve(TcpClient client, TcpListener listener, int generation)
        {
            Dispatch.BindRequest(generation);
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
                {
                    string line;
                    while (_running && Object.ReferenceEquals(_listener, listener) && (line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        if (!_running || !Object.ReferenceEquals(_listener, listener)) break;
                        string response = Commands.Handle(line);
                        if (_running && Object.ReferenceEquals(_listener, listener)) writer.WriteLine(response);
                    }
                }
            }
            catch (IOException) { /* client disconnected or level unloaded */ }
            catch (ObjectDisposedException) { /* Stop closed the client */ }
            finally
            {
                Dispatch.UnbindRequest();
                lock (_clientsGate) _clients.Remove(client);
            }
        }
    }
}
