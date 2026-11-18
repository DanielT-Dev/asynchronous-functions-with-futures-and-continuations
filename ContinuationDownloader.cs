using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class ContinuationDownloader
{
    private readonly Uri url;
    private readonly string outputPath;
    private readonly Socket socket;
    private readonly HttpResponseParser parser;
    private readonly byte[] receiveBuffer = new byte[8192];
    private readonly Action<bool, string> onComplete;

    private byte[] requestBytes = Array.Empty<byte>();
    private int sendPosition = 0;
    private int completed = 0;
    private Timer timeoutTimer;

    public ContinuationDownloader(string url, string outputPath, Action<bool, string> onComplete)
    {
        this.url = new Uri(url);
        this.outputPath = outputPath;
        this.onComplete = onComplete;

        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        parser = new HttpResponseParser();
    }

    public void Start()
    {
        try
        {
            timeoutTimer = new Timer(
                _ => Complete(false, $"Download timed out: {url.Host}"),
                null,
                TimeSpan.FromSeconds(20),
                Timeout.InfiniteTimeSpan);

            if (url.Scheme != "http")
                throw new NotSupportedException("Only HTTP is supported.");

            IPAddress[] addresses = Dns.GetHostAddresses(url.Host);
            IPAddress address = Array.Find(addresses, ip => ip.AddressFamily == AddressFamily.InterNetwork);

            if (address == null)
                throw new Exception("No IPv4 address found.");

            Console.WriteLine($"Connecting to {url.Host}...");

            ConnectAsync(new IPEndPoint(address, url.Port))
                .ContinueWith(connectTask =>
                {
                    connectTask.GetAwaiter().GetResult();
                    Console.WriteLine($"Connected to {url.Host}");

                    PrepareRequest();
                    return SendAllAsync();
                }, TaskScheduler.Default)
                .Unwrap()
                .ContinueWith(sendTask =>
                {
                    sendTask.GetAwaiter().GetResult();
                    Console.WriteLine($"Request sent to {url.Host}");

                    return ReceiveResponseAsync();
                }, TaskScheduler.Default)
                .Unwrap()
                .ContinueWith(receiveTask =>
                {
                    byte[] body = receiveTask.GetAwaiter().GetResult();
                    SaveFile(body);

                    Complete(true, $"Saved {body.Length} bytes to {outputPath}");
                }, TaskScheduler.Default)
                .ContinueWith(finalTask =>
                {
                    if (finalTask.IsFaulted)
                        Complete(false, finalTask.Exception!.GetBaseException().Message);
                    else if (finalTask.IsCanceled)
                        Complete(false, "Download was cancelled.");
                }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            Complete(false, ex.Message);
        }
    }

    private Task ConnectAsync(EndPoint endpoint)
    {
        TaskCompletionSource<bool> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            socket.BeginConnect(endpoint, ar =>
            {
                try
                {
                    socket.EndConnect(ar);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }

    private Task<int> SendAsync(byte[] buffer, int offset, int count)
    {
        TaskCompletionSource<int> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            socket.BeginSend(buffer, offset, count, SocketFlags.None, ar =>
            {
                try
                {
                    int sent = socket.EndSend(ar);
                    tcs.TrySetResult(sent);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }

    private Task<int> ReceiveAsync()
    {
        TaskCompletionSource<int> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            socket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, ar =>
            {
                try
                {
                    int received = socket.EndReceive(ar);
                    tcs.TrySetResult(received);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }

    private void PrepareRequest()
    {
        string path = string.IsNullOrEmpty(url.PathAndQuery) ? "/" : url.PathAndQuery;

        string request = $"GET {path} HTTP/1.1\r\n" +
                         $"Host: {url.Host}\r\n" +
                         "Connection: close\r\n" +
                         "User-Agent: Lab4-HTTP-Client\r\n\r\n";

        requestBytes = Encoding.ASCII.GetBytes(request);
    }

    private Task SendAllAsync()
    {
        if (sendPosition >= requestBytes.Length)
            return Task.CompletedTask;

        return SendAsync(requestBytes, sendPosition, requestBytes.Length - sendPosition)
            .ContinueWith(sendTask =>
            {
                int bytesSent = sendTask.GetAwaiter().GetResult();

                if (bytesSent == 0)
                    throw new Exception("Connection closed while sending.");

                sendPosition += bytesSent;
                return SendAllAsync();
            }, TaskScheduler.Default)
            .Unwrap();
    }

    private Task<byte[]> ReceiveResponseAsync()
    {
        return ReceiveAsync().ContinueWith(receiveTask =>
        {
            int bytesReceived = receiveTask.GetAwaiter().GetResult();

            if (bytesReceived == 0)
            {
                if (parser.HeadersReceived && (!parser.HasContentLength || parser.IsComplete))
                    return Task.FromResult(parser.GetBody());

                throw new Exception("Connection closed before download completed.");
            }

            parser.ProcessBytes(receiveBuffer, bytesReceived);
            Console.WriteLine($"{url.Host}: received {bytesReceived} bytes");

            if (parser.IsComplete)
                return Task.FromResult(parser.GetBody());

            return ReceiveResponseAsync();
        }, TaskScheduler.Default).Unwrap();
    }

    private void SaveFile(byte[] body)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, body);
    }

    private void Complete(bool success, string message)
    {
        if (Interlocked.Exchange(ref completed, 1) != 0)
            return;

        timeoutTimer?.Dispose();
        socket.Close();
        onComplete?.Invoke(success, message);
    }
}
