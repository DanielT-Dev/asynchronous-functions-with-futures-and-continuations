
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class HttpDownloader
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

    public HttpDownloader(string url, string outputPath, Action<bool, string> onComplete)
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
                _ => Complete(false, $"Download timed out after 20 seconds: {url.Host}"),
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
            socket.BeginConnect(new IPEndPoint(address, url.Port), OnConnected, null);
        }
        catch (Exception ex)
        {
            Complete(false, ex.Message);
        }
    }

    private void OnConnected(IAsyncResult ar)
    {
        try
        {
            socket.EndConnect(ar);
            Console.WriteLine($"Connected to {url.Host}");

            string path = string.IsNullOrEmpty(url.PathAndQuery) ? "/" : url.PathAndQuery;
            string request = $"GET {path} HTTP/1.1\r\n" +
                             $"Host: {url.Host}\r\n" +
                             "Connection: close\r\n" +
                             "User-Agent: Lab4-HTTP-Client\r\n\r\n";

            requestBytes = Encoding.ASCII.GetBytes(request);
            SendNextPart();
        }
        catch (Exception ex)
        {
            Complete(false, ex.Message);
        }
    }

    private void SendNextPart()
    {
        try
        {
            socket.BeginSend(requestBytes, sendPosition, requestBytes.Length - sendPosition,
                SocketFlags.None, OnSent, null);
        }
        catch (Exception ex)
        {
            Complete(false, ex.Message);
        }
    }

    private void OnSent(IAsyncResult ar)
    {
        try
        {
            int bytesSent = socket.EndSend(ar);

            if (bytesSent == 0)
                throw new Exception("Connection closed while sending.");

            sendPosition += bytesSent;

            if (sendPosition < requestBytes.Length)
            {
                SendNextPart();
                return;
            }

            Console.WriteLine($"Request sent to {url.Host}");
            ReceiveNextPart();
        }
        catch (Exception ex)
        {
            Complete(false, ex.Message);
        }
    }

    private void ReceiveNextPart()
    {
        try
        {
            socket.BeginReceive(receiveBuffer, 0, receiveBuffer.Length,
                SocketFlags.None, OnReceived, null);
        }
        catch (Exception ex)
        {
            Complete(false, ex.Message);
        }
    }

    private void OnReceived(IAsyncResult ar)
    {
        try
        {
            int bytesReceived = socket.EndReceive(ar);

            if (bytesReceived == 0)
            {
                if (parser.HeadersReceived && (!parser.HasContentLength || parser.IsComplete))
                    SaveFile();
                else
                    Complete(false, "Connection closed before download completed.");

                return;
            }

            parser.ProcessBytes(receiveBuffer, bytesReceived);
            Console.WriteLine($"{url.Host}: received {bytesReceived} bytes");

            if (parser.IsComplete)
                SaveFile();
            else
                ReceiveNextPart();
        }
        catch (Exception ex)
        {
            Complete(false, ex.Message);
        }
    }

    private void SaveFile()
    {
        try
        {
            byte[] body = parser.GetBody();

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, body);

            Complete(true, $"Saved {body.Length} bytes to {outputPath}");
        }
        catch (Exception ex)
        {
            Complete(false, ex.Message);
        }
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
