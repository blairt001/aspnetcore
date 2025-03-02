// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.Http.Result;

/// <summary>
/// A <see cref="PhysicalFileResult"/> on execution will write a file from disk to the response
/// using mechanisms provided by the host.
/// </summary>
internal sealed partial class PhysicalFileResult : FileResult, IResult
{
    /// <summary>
    /// Creates a new <see cref="PhysicalFileResult"/> instance with
    /// the provided <paramref name="fileName"/> and the provided <paramref name="contentType"/>.
    /// </summary>
    /// <param name="fileName">The path to the file. The path must be an absolute path.</param>
    /// <param name="contentType">The Content-Type header of the response.</param>
    public PhysicalFileResult(string fileName, string? contentType)
        : base(contentType)
    {
        FileName = fileName;
    }

    /// <summary>
    /// Gets or sets the path to the file that will be sent back as the response.
    /// </summary>
    public string FileName { get; }

    // For testing
    public Func<string, FileInfoWrapper> GetFileInfoWrapper { get; init; } =
        static path => new FileInfoWrapper(path);

    protected override ILogger GetLogger(HttpContext httpContext)
    {
        return httpContext.RequestServices.GetRequiredService<ILogger<PhysicalFileResult>>();
    }

    public override Task ExecuteAsync(HttpContext httpContext)
    {
        var fileInfo = GetFileInfoWrapper(FileName);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException($"Could not find file: {FileName}", FileName);
        }

        LastModified = LastModified ?? fileInfo.LastWriteTimeUtc;
        FileLength = fileInfo.Length;

        return base.ExecuteAsync(httpContext);
    }

    protected override Task ExecuteCoreAsync(HttpContext httpContext, RangeItemHeaderValue? range, long rangeLength)
    {
        var response = httpContext.Response;
        if (!Path.IsPathRooted(FileName))
        {
            throw new NotSupportedException($"Path '{FileName}' was not rooted.");
        }

        var offset = 0L;
        var count = (long?)null;
        if (range != null)
        {
            offset = range.From ?? 0L;
            count = rangeLength;
        }

        return response.SendFileAsync(
            FileName,
            offset: offset,
            count: count);
    }

    internal readonly struct FileInfoWrapper
    {
        public FileInfoWrapper(string path)
        {
            var fileInfo = new FileInfo(path);

            // It means we are dealing with a symlink and need to get the information
            // from the target file instead.
            if (fileInfo.Exists && !string.IsNullOrEmpty(fileInfo.LinkTarget))
            {
                fileInfo = (FileInfo?)fileInfo.ResolveLinkTarget(returnFinalTarget: true) ?? fileInfo;
            }

            Exists = fileInfo.Exists;
            Length = fileInfo.Length;
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
        }

        public bool Exists { get; init; }

        public long Length { get; init; }

        public DateTimeOffset LastWriteTimeUtc { get; init; }
    }
}
