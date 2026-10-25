using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    internal sealed class LocalDecisionHttpResponse
    {
        public int? StatusCode { get; }
        public string Body { get; }
        public NpcDecisionFailureKind FailureKind { get; }
        public string DiagnosticCode { get; }

        public LocalDecisionHttpResponse(int? statusCode, string body,
            NpcDecisionFailureKind failureKind, string diagnosticCode = null)
        {
            StatusCode = statusCode;
            Body = body;
            FailureKind = failureKind;
            DiagnosticCode = diagnosticCode;
        }
    }

    internal interface ILocalDecisionTransport
    {
        LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds,
            CancellationToken cancellationToken);
    }

    internal sealed class LocalDecisionTransport : ILocalDecisionTransport
    {
        private const int MaximumResponseBytes = 128 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            if (!ValidEndpoint(endpoint) || bytes == null || timeoutMilliseconds < 1 || timeoutMilliseconds > 10000)
                return Failure(NpcDecisionFailureKind.Transport, "invalid_transport_request");
            if (cancellationToken.IsCancellationRequested)
                return Failure(NpcDecisionFailureKind.Transport, "cancelled");

            var requestBytes = (byte[])bytes.Clone();
            using (var deadline = new CancellationTokenSource(timeoutMilliseconds))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token))
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
                using (linked.Token.Register(() =>
                {
                    try { request.Abort(); }
                    catch (ObjectDisposedException) { }
                }))
                {
                    try
                    {
                        if (linked.IsCancellationRequested)
                            throw new OperationCanceledException(linked.Token);
                        request.Method = "POST";
                        request.ContentType = "application/json; charset=utf-8";
                        request.ContentLength = requestBytes.Length;
                        request.Timeout = timeoutMilliseconds;
                        request.ReadWriteTimeout = timeoutMilliseconds;
                        request.AllowAutoRedirect = false;
                        request.Proxy = null;
                        request.KeepAlive = false;
                        using (Stream output = request.GetRequestStream())
                            output.Write(requestBytes, 0, requestBytes.Length);

                        using (var response = (HttpWebResponse)request.GetResponse())
                            return ReadResponse(response, linked.Token);
                    }
                    catch (WebException error)
                    {
                        if (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                            return Failure(NpcDecisionFailureKind.Timeout, "timeout");
                        if (cancellationToken.IsCancellationRequested)
                            return Failure(NpcDecisionFailureKind.Transport, "cancelled");
                        var http = error.Response as HttpWebResponse;
                        if (http != null)
                        {
                            using (http)
                                return new LocalDecisionHttpResponse((int)http.StatusCode, null,
                                    NpcDecisionFailureKind.None, "http_status");
                        }
                        if (error.Status == WebExceptionStatus.Timeout)
                            return Failure(NpcDecisionFailureKind.Timeout, "timeout");
                        return Failure(NpcDecisionFailureKind.Transport,
                            cancellationToken.IsCancellationRequested ? "cancelled" : "transport_failure");
                    }
                    catch (OperationCanceledException)
                    {
                        return Failure(deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                            ? NpcDecisionFailureKind.Timeout : NpcDecisionFailureKind.Transport,
                            deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested ? "timeout" : "cancelled");
                    }
                    catch (IOException)
                    {
                        return Failure(deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                            ? NpcDecisionFailureKind.Timeout : NpcDecisionFailureKind.Transport,
                            deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested ? "timeout" : "io_failure");
                    }
                    catch (ObjectDisposedException)
                    {
                        return Failure(deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                            ? NpcDecisionFailureKind.Timeout : NpcDecisionFailureKind.Transport,
                            deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested ? "timeout" : "cancelled");
                    }
                    catch (InvalidOperationException)
                    {
                        return Failure(NpcDecisionFailureKind.Transport, "transport_protocol_failure");
                    }
                }
            }
        }

        private static LocalDecisionHttpResponse ReadResponse(HttpWebResponse response, CancellationToken cancellationToken)
        {
            int status = (int)response.StatusCode;
            if (status != 200)
                return new LocalDecisionHttpResponse(status, null, NpcDecisionFailureKind.None, "http_status");
            if (response.ContentLength > MaximumResponseBytes)
                return new LocalDecisionHttpResponse(status, null, NpcDecisionFailureKind.MalformedJson, "response_too_large");
            Stream stream = response.GetResponseStream();
            if (stream == null)
                return new LocalDecisionHttpResponse(status, string.Empty, NpcDecisionFailureKind.None);
            using (stream)
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[4096];
                while (true)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read == 0) break;
                    if (output.Length + read > MaximumResponseBytes)
                        return new LocalDecisionHttpResponse(status, null, NpcDecisionFailureKind.MalformedJson, "response_too_large");
                    output.Write(buffer, 0, read);
                }
                string body;
                try { body = StrictUtf8.GetString(output.ToArray()); }
                catch (DecoderFallbackException)
                {
                    return new LocalDecisionHttpResponse(status, null, NpcDecisionFailureKind.MalformedJson, "invalid_utf8");
                }
                return new LocalDecisionHttpResponse(status, body, NpcDecisionFailureKind.None);
            }
        }

        private static bool ValidEndpoint(Uri endpoint)
        {
            return endpoint != null && endpoint.IsAbsoluteUri && endpoint.Scheme == Uri.UriSchemeHttp &&
                endpoint.IsLoopback && string.IsNullOrEmpty(endpoint.UserInfo) &&
                string.Equals(endpoint.AbsolutePath, "/v1/systemone", StringComparison.Ordinal) &&
                string.IsNullOrEmpty(endpoint.Query) && string.IsNullOrEmpty(endpoint.Fragment);
        }

        private static LocalDecisionHttpResponse Failure(NpcDecisionFailureKind kind, string diagnostic)
            => new LocalDecisionHttpResponse(null, null, kind, diagnostic);
    }
}
