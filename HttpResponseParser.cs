using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public class HttpResponseParser
{
    private readonly List<byte> headerBuffer = new();
    private readonly List<byte> bodyBuffer = new();

    public bool HeadersReceived { get; private set; }
    public bool HasContentLength { get; private set; }
    public int ContentLength { get; private set; }

    public int BodyBytesReceived => bodyBuffer.Count;

    public bool IsComplete => HeadersReceived && HasContentLength && BodyBytesReceived >= ContentLength;

    public void ProcessBytes(byte[] buffer, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (!HeadersReceived)
            {
                headerBuffer.Add(buffer[i]);

                int size = headerBuffer.Count;

                if (size >= 4 &&
                    headerBuffer[size - 4] == '\r' &&
                    headerBuffer[size - 3] == '\n' &&
                    headerBuffer[size - 2] == '\r' &&
                    headerBuffer[size - 1] == '\n')
                {
                    HeadersReceived = true;

                    string headers = Encoding.ASCII.GetString(headerBuffer.ToArray());

                    ParseHeaders(headers);
                }
            }
            else
            {
                bodyBuffer.Add(buffer[i]);
            }
        }
    }

    private void ParseHeaders(string headers)
    {
        string[] lines = headers.Split("\r\n");

        foreach (string line in lines)
        {
            if (line.StartsWith(
                "Content-Length:",
                StringComparison.OrdinalIgnoreCase))
            {
                ContentLength = int.Parse(
                    line.Substring("Content-Length:".Length).Trim(),
                    CultureInfo.InvariantCulture);

                HasContentLength = true;
                return;
            }
        }

        HasContentLength = false;
    }

    public byte[] GetBody()
    {
        return bodyBuffer.ToArray();
    }
}
