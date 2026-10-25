using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PeopleOfTheIliacBay.DFU;

internal static class Program
{
    private static async Task Main()
    {
        foreach (string uri in new[]{"https://localhost/completion","http://127.0.0.2/completion","http://user@localhost/completion"})
        {
            bool rejected=false;
            try { LocalDialogueHttp.Create(new Uri(uri)); } catch(ArgumentException) { rejected=true; }
            if(!rejected) throw new Exception("Unsafe endpoint accepted: "+uri);
        }
        await CheckResponse(200);
        foreach(int status in new[]{301,302,303,307,308}) await CheckResponse(status);
        Console.WriteLine("PASS local HTTP response, rejected endpoints, and five redirect statuses without prompt forwarding.");
    }

    private static async Task CheckResponse(int status)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var origin=new TcpListener(IPAddress.Loopback,0);
        var target=new TcpListener(IPAddress.Loopback,0);
        origin.Start(); target.Start();
        try
        {
            string originUrl="http://127.0.0.1:"+((IPEndPoint)origin.LocalEndpoint).Port+"/completion";
            string targetUrl="http://127.0.0.1:"+((IPEndPoint)target.LocalEndpoint).Port+"/capture";
            var targetAccept=target.AcceptTcpClientAsync(deadline.Token).AsTask();
            Task server=Task.Run(async()=>{
                using var peer=await origin.AcceptTcpClientAsync(deadline.Token);
                using var stream=peer.GetStream();
                using var reader=new StreamReader(stream,Encoding.UTF8,false,1024,true);
                string line; int length=0;
                while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync(deadline.Token)))
                    if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)) length=int.Parse(line.Substring(15).Trim());
                var chars=new char[length];int offset=0;
                while(offset<length) { int read=await reader.ReadAsync(chars.AsMemory(offset),deadline.Token);if(read==0)break;offset+=read; }
                if(new string(chars)!= "private-dialogue-context") throw new Exception("Origin prompt bytes changed.");
                string response="HTTP/1.1 "+status+" Test\r\n"+(status==200?"":"Location: "+targetUrl+"\r\n")+
                    "Content-Length: 2\r\nConnection: close\r\n\r\nOK";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response),deadline.Token);
            });
            var request=LocalDialogueHttp.Create(new Uri(originUrl));
            if(request.Proxy!=null) throw new Exception("Local prompt can be routed through a proxy.");
            request.Method="POST";request.Timeout=2000;request.ReadWriteTimeout=2000;
            byte[] body=Encoding.UTF8.GetBytes("private-dialogue-context");request.ContentLength=body.Length;
            using(var stream=await request.GetRequestStreamAsync()) await stream.WriteAsync(body,deadline.Token);
            using(var response=(HttpWebResponse)await request.GetResponseAsync())
            {
                if((int)response.StatusCode!=status || response.ResponseUri.AbsoluteUri!=originUrl)
                    throw new Exception("Response followed a redirect.");
            }
            await server;
            if(targetAccept.IsCompletedSuccessfully) { targetAccept.Result.Dispose();throw new Exception("Prompt reached redirect destination."); }
            deadline.Cancel();
            try { using var unexpected=await targetAccept;throw new Exception("Redirect destination was contacted."); }
            catch(OperationCanceledException) { }
        }
        finally { origin.Stop(); target.Stop(); }
    }
}
