using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Parse.Abstractions.Infrastructure;
using Parse.Abstractions.Infrastructure.Execution;
using Parse.Abstractions.Platform.Files;
using Parse.Infrastructure.Execution;

namespace Parse.Platform.Files;
public class ParseFileController : IParseFileController
{
    private IParseCommandRunner _runner;
    public ParseFileController(IParseCommandRunner commandRunner)
        => _runner = commandRunner;

    public async Task<FileState> SaveAsync(
        FileState state,
        Stream dataStream,
        string sessionToken,
        IProgress<IDataTransferLevel> progress,
        CancellationToken cancellationToken = default)
    {
        if (state.Location != null)
            return state;

        cancellationToken.ThrowIfCancellationRequested();
        var oldPos = dataStream.CanSeek ? dataStream.Position : 0L;

        try
        {
            var (status, json) = await _runner.RunCommandAsync(
                new ParseCommand($"files/{state.Name}",
                                 method: "POST",
                                 sessionToken: sessionToken,
                                 contentType: state.MediaType,
                                 stream: dataStream),
                uploadProgress: progress,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (!json.TryGetValue("name", out var nObj) || !(nObj is string name) ||
                !json.TryGetValue("url", out var uObj) || !(uObj is string url))
                throw new InvalidDataException("Parse server returned bad file metadata.");

            return new FileState
            {
                Name      = name,
                Location  = new Uri(url, UriKind.Absolute),
                MediaType = state.MediaType,
                HttpCode= status,
            };
        }
        catch (OperationCanceledException)
        {
            if (dataStream.CanSeek)
                dataStream.Seek(oldPos, SeekOrigin.Begin);
            throw;
        }
        catch
        {
            if (dataStream.CanSeek)
                dataStream.Seek(oldPos, SeekOrigin.Begin);
            throw;
        }
    }
}
