using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class AsyncAwaitDownloader
{
    private readonly Uri url;
    private readonly string outputPath;
    private readonly Socket socket;
    private readonly HttpResponseParser parser;
    private readonly byte[] receiveBuffer = new byte[8192];
    private readonly Action<bool, string> onComplete;

    private byte[] requestBytes = Array.Empty<byte>();
    private int completed = 0;
    private Timer timeoutTimer;

    public AsyncAwaitDownloader(string url, string outputPath, Action<bool, string> onComplete)
    {
        this.url = new Uri(url);
        this.outputPath = outputPath;
        this.onComplete = onComplete;

        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        parser = new HttpResponseParser();
    }

    public void Start()
    {
        timeoutTimer = new Timer(
            _ => Complete(false, $"Download timed out: {url.Host}"),
            null,
            TimeSpan.FromSeconds(20),
            Timeout.InfiniteTimeSpan);

        _ = DownloadAsync();
    }

    private async Task DownloadAsync()
    {
        try
        {
            if (url.Scheme != "http")
                throw new NotSupportedException("Only HTTP is supported.");

            IPAddress[] addresses = Dns.GetHostAddresses(url.Host);
            IPAddress address = Array.Find(addresses, ip => ip.AddressFamily == AddressFamily.InterNetwork);

            if (address == null)
                throw new Exception("No IPv4 address found.");

            Console.WriteLine($"Connecting to {url.Host}...");
            await ConnectAsync(new IPEndPoint(address, url.Port));

            Console.WriteLine($"Connected to {url.Host}");

            PrepareRequest();
            await SendAllAsync();

            Console.WriteLine($"Request sent to {url.Host}");

            byte[] body = await ReceiveResponseAsync();
            SaveFile(body);

            Complete(true, $"Saved {body.Length} bytes to {outputPath}");
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

    private async Task SendAllAsync()
    {
        int sendPosition = 0;

        while (sendPosition < requestBytes.Length)
        {
            int bytesSent = await SendAsync(requestBytes, sendPosition, requestBytes.Length - sendPosition);

            if (bytesSent == 0)
                throw new Exception("Connection closed while sending.");

            sendPosition += bytesSent;
        }
    }

    private async Task<byte[]> ReceiveResponseAsync()
    {
        while (true)
        {
            int bytesReceived = await ReceiveAsync();

            if (bytesReceived == 0)
            {
                if (parser.HeadersReceived && (!parser.HasContentLength || parser.IsComplete))
                    return parser.GetBody();

                throw new Exception("Connection closed before download completed.");
            }

            parser.ProcessBytes(receiveBuffer, bytesReceived);
            Console.WriteLine($"{url.Host}: received {bytesReceived} bytes");

            if (parser.IsComplete)
                return parser.GetBody();
        }
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
