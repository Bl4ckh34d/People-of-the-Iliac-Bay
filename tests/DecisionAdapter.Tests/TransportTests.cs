using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU.Tests
{
    internal static class TransportTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("real HTTP redirects never submit a second request", Redirects));
            cases.Add(("real HTTP error status is retained without consuming error body", ErrorStatus));
            cases.Add(("transport response size and UTF8 limits apply to streaming bodies", ResponseLimits));
            cases.Add(("absolute deadline stops a response that keeps sending bytes", SlowDrip));
            cases.Add(("pre-cancelled transport never connects and invalid URLs never post", PreCancelled));
        }

        private static LocalDecisionHttpResponse Exchange(Func<NetworkStream, string, Task> respond,
            int timeout = 1500, Action<long> observeDuration = null)
        {
            using (var listener = new TcpListener(IPAddress.Loopback, 0))
            {
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var server = Task.Run(async () =>
                {
                    using (var client = await listener.AcceptTcpClientAsync())
                    using (var stream = client.GetStream())
                    {
                        stream.ReadTimeout = 2500;
                        ReadRequest(stream);
                        try { await respond(stream, "http://127.0.0.1:" + port); }
                        catch (IOException) { } // Deadline/size rejection closes the peer.
                        catch (SocketException) { }
                    }
                });
                var watch = Stopwatch.StartNew();
                var response = new LocalDecisionTransport().Send(new Uri("http://127.0.0.1:" + port + "/v1/systemone"),
                    Encoding.UTF8.GetBytes("{}"), timeout, CancellationToken.None);
                watch.Stop();
                if (observeDuration != null) observeDuration(watch.ElapsedMilliseconds);
                Check.True(server.Wait(4000), "Owned HTTP test server must terminate.");
                return response;
            }
        }

        private static void Redirects()
        {
            foreach (int status in new[] { 302, 307 })
            using (var target = new TcpListener(IPAddress.Loopback, 0))
            {
                target.Start();
                int port = ((IPEndPoint)target.LocalEndpoint).Port;
                var response = Exchange((stream, origin) =>
                {
                    Write(stream, "HTTP/1.1 " + status + " Redirect\r\nLocation: http://127.0.0.1:" + port +
                        "/unexpected\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    return Task.CompletedTask;
                });
                Check.Equal((int?)status, response.StatusCode);
                Check.True(!target.Pending(), "Redirect target must receive zero connections.");
            }
        }

        private static void ErrorStatus()
        {
            var response = Exchange((stream, origin) =>
            {
                Write(stream, "HTTP/1.1 503 Unavailable\r\nContent-Length: 1000000\r\nConnection: close\r\n\r\n");
                return Task.CompletedTask;
            });
            Check.Equal((int?)503, response.StatusCode);
            Check.Equal(NpcDecisionFailureKind.None, response.FailureKind);
        }

        private static void ResponseLimits()
        {
            var oversized = Exchange((stream, origin) =>
            {
                Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 131073\r\nConnection: close\r\n\r\n");
                return Task.CompletedTask;
            });
            Check.Equal(NpcDecisionFailureKind.MalformedJson, oversized.FailureKind);
            var chunked = Exchange((stream, origin) =>
            {
                Write(stream, "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n");
                Write(stream, "20001\r\n" + new string('x', 131073) + "\r\n0\r\n\r\n");
                return Task.CompletedTask;
            });
            Check.Equal(NpcDecisionFailureKind.MalformedJson, chunked.FailureKind);
            var badUtf8 = Exchange((stream, origin) =>
            {
                Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\n");
                stream.Write(new byte[] { 0xc3, 0x28 }, 0, 2);
                return Task.CompletedTask;
            });
            Check.Equal(NpcDecisionFailureKind.MalformedJson, badUtf8.FailureKind);
        }

        private static void SlowDrip()
        {
            long elapsed = 0;
            var response = Exchange(async (stream, origin) =>
            {
                Write(stream, "HTTP/1.1 200 OK\r\nContent-Length: 50\r\nConnection: close\r\n\r\n");
                for (int i = 0; i < 50; i++)
                {
                    stream.WriteByte((byte)' ');
                    stream.Flush();
                    await Task.Delay(30);
                }
            }, 150, duration => elapsed = duration);
            Check.Equal(NpcDecisionFailureKind.Timeout, response.FailureKind);
            Check.True(elapsed < 1200, "An absolute deadline must stop slow-drip reads.");
        }

        private static void PreCancelled()
        {
            using (var listener = new TcpListener(IPAddress.Loopback, 0))
            using (var cancelled = new CancellationTokenSource())
            {
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                cancelled.Cancel();
                var transport = new LocalDecisionTransport();
                transport.Send(new Uri("http://127.0.0.1:" + port + "/v1/systemone"), new byte[0], 100, cancelled.Token);
                Check.True(!listener.Pending(), "Pre-cancellation must prevent connection.");
                foreach (string suffix in new[] { "/completion", "/v1/systemone?other=1", "/v1/systemone#fragment" })
                {
                    var invalid = transport.Send(new Uri("http://127.0.0.1:" + port + suffix), new byte[0], 100, CancellationToken.None);
                    Check.Equal(NpcDecisionFailureKind.Transport, invalid.FailureKind);
                    Check.True(!listener.Pending(), "Invalid endpoint must not connect.");
                }
            }
        }

        private static void ReadRequest(NetworkStream stream)
        {
            var header = new List<byte>();
            while (header.Count < 16384)
            {
                int value = stream.ReadByte();
                if (value < 0) throw new IOException("Request ended before headers.");
                header.Add((byte)value);
                int n = header.Count;
                if (n >= 4 && header[n - 4] == 13 && header[n - 3] == 10 && header[n - 2] == 13 && header[n - 1] == 10) break;
            }
            string text = Encoding.ASCII.GetString(header.ToArray());
            Check.True(text.StartsWith("POST /v1/systemone HTTP/1.1\r\n", StringComparison.Ordinal));
            string lengthLine = text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                .First(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            int length = int.Parse(lengthLine.Substring(lengthLine.IndexOf(':') + 1));
            for (int i = 0; i < length; i++) if (stream.ReadByte() < 0) throw new IOException("Request body incomplete.");
        }

        private static void Write(Stream stream, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }
    }
}
