using System;
using System.IO;
using System.Threading;

class Program
{
    enum DownloaderType
    {
        Callback,
        Continuations,
        AsyncAwait
    }

    static void Main(string[] args)
    {
        // Choose which implementation to run.
        DownloaderType selectedDownloader = DownloaderType.Callback;
        string downloadsFolder = Path.Combine(AppContext.BaseDirectory,"Downloads");
        Directory.CreateDirectory(downloadsFolder);

        string[] urls =
        {
            "http://example.com/",
            "http://neverssl.com/",
            "http://info.cern.ch/"
        };

        using CountdownEvent countdown = new CountdownEvent(urls.Length);

        Console.WriteLine($"Using: {selectedDownloader}\n");
        Console.WriteLine("Starting downloads...\n");

        foreach (string url in urls)
        {
            string fileName = new Uri(url).Host + ".html";
            string outputPath = Path.Combine(downloadsFolder, fileName);

            Action<bool, string> onComplete = (success, message) =>
            {
                Console.WriteLine($"{(success ? "SUCCESS" : "FAILED")}: {message}");
                countdown.Signal();
            };

            switch (selectedDownloader)
            {
                case DownloaderType.Callback:
                    new HttpDownloader(url, outputPath, onComplete).Start();
                    break;

                case DownloaderType.Continuations:
                    new ContinuationDownloader(url, outputPath, onComplete).Start();
                    break;

                case DownloaderType.AsyncAwait:
                    new AsyncAwaitDownloader(url, outputPath, onComplete).Start();
                    break;
            }
        }

        countdown.Wait();
        Console.WriteLine("\nAll downloads completed!");
    }
}
