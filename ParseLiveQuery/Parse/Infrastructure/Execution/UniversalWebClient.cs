using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Parse.Abstractions.Infrastructure;
using Parse.Abstractions.Infrastructure.Execution;
using BCLWebClient = System.Net.Http.HttpClient;

namespace Parse.Infrastructure.Execution;

/// <summary>
/// A universal implementation of <see cref="IWebClient"/>.
/// </summary>
public class UniversalWebClient : IWebClient
{
    static HashSet<string> ContentHeaders { get; } = new HashSet<string>
    {
        { "Allow" },
        { "Content-Disposition" },
        { "Content-Encoding" },
        { "Content-Language" },
        { "Content-Length" },
        { "Content-Location" },
        { "Content-MD5" },
        { "Content-Range" },
        { "Content-Type" },
        { "Expires" },
        { "Last-Modified" }
    };

    public UniversalWebClient() : this(new BCLWebClient { }) { }

    public UniversalWebClient(BCLWebClient client) => Client = client;

    BCLWebClient Client { get; set; }
    public async Task<Tuple<HttpStatusCode, string>> ExecuteAsync(
    WebRequest httpRequest,
    IProgress<IDataTransferLevel> uploadProgress,
    IProgress<IDataTransferLevel> downloadProgress,
    CancellationToken cancellationToken=default)
    {
        uploadProgress ??= new Progress<IDataTransferLevel> { };
        downloadProgress ??= new Progress<IDataTransferLevel> { };

        HttpRequestMessage message = new HttpRequestMessage(new HttpMethod(httpRequest.Method), httpRequest.Target);

        if ((httpRequest.Data is null && httpRequest.Method.ToLower().Equals("post")
             ? new MemoryStream(new byte[0])
             : httpRequest.Data) is Stream { } data)
        {
            message.Content = new StreamContent(data);
        }

        if (httpRequest.Headers != null)
        {
            foreach (KeyValuePair<string, string> header in httpRequest.Headers)
            {
                if (ContentHeaders.Contains(header.Key))
                {
                    message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                else
                {
                    message.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        // Avoid aggressive caching on Windows Phone 8.1.
        message.Headers.Add("Cache-Control", "no-cache");
        message.Headers.IfModifiedSince = DateTimeOffset.UtcNow;

        uploadProgress.Report(new DataTransferLevel { Amount = 0 });

        HttpResponseMessage response = await Client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        uploadProgress.Report(new DataTransferLevel { Amount = 1 });
        //response.EnsureSuccessStatusCode();

        long? totalLength = response.Content.Headers.ContentLength;

        MemoryStream resultStream = new MemoryStream();
        try
        {
            using (var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                byte[] buffer = new byte[4096];
                int bytesRead;
                long readSoFar = 0;

                while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await resultStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    readSoFar += bytesRead;


                    if (totalLength.HasValue && totalLength > 0)
                    {
                        downloadProgress.Report(new DataTransferLevel { Amount = (double)readSoFar / totalLength.Value });
                    }


                }
            }

            if (!totalLength.HasValue || totalLength <= 0)
            {
                downloadProgress.Report(new DataTransferLevel { Amount = 1.0 }); // Report completion if total length is unknown
            }


            byte[] resultAsArray = resultStream.ToArray();
            string resultString = Encoding.UTF8.GetString(resultAsArray, 0, resultAsArray.Length);

            return new Tuple<HttpStatusCode, string>(response.StatusCode, resultString);
        }
        finally
        {
           await resultStream.DisposeAsync();
        }
    }
}
