// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Net;
using GameHome.Sources.Updates;

namespace GameHome.Core.Tests;

public class ReleaseDownloadTests
{
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(5);
    private static readonly AvailableRelease Release = new(new Version(1, 0, 0, 0), "v1.0.0",
        "GameHome_x64.msix", new Uri("https://example.test/GameHome_x64.msix"), 3);

    [Fact]
    public async Task CopiesExactSizeWithoutContentLength()
    {
        byte[] package = [1, 2, 3];
        using var http = CreateClient(new StreamContent(new NonSeekableBody(package)));
        using var destination = new MemoryStream();
        await ReleaseDownload.CopyAsync(http, Release, destination, TestDeadline, null, TestContext.Current.CancellationToken);
        Assert.Equal(package, destination.ToArray());
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(4, 0)]
    public async Task RejectsTruncatedAndOversizedBodies(int actualBytes, int writtenBytes)
    {
        using var http = CreateClient(new StreamContent(new NonSeekableBody(new byte[actualBytes])));
        using var destination = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => ReleaseDownload.CopyAsync(
            http, Release, destination, TestDeadline, null, TestContext.Current.CancellationToken));
        Assert.Equal(writtenBytes, destination.Length);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(512L * 1024 * 1024 + 1)]
    public async Task RejectsInvalidPackageSizeBeforeRequest(long size)
    {
        using var handler = new ResponseHandler(new ByteArrayContent([1, 2, 3]));
        using var http = new HttpClient(handler, disposeHandler: false);
        using var destination = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => ReleaseDownload.CopyAsync(
            http, Release with { Size = size }, destination, TestDeadline, null, TestContext.Current.CancellationToken));
        Assert.False(handler.Requested);
    }

    [Fact]
    public async Task DeadlineCancelsAStalledBodyAfterHeaders()
    {
        var source = new StalledBody();
        using var http = CreateClient(new StreamContent(source));
        using var destination = new MemoryStream();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TestDeadline);
        var download = ReleaseDownload.CopyAsync(http, Release, destination, TimeSpan.FromMilliseconds(250), null, cancellation.Token);
        await source.Reading.Task.WaitAsync(TestDeadline, cancellation.Token);
        await Assert.ThrowsAsync<TimeoutException>(() => download);
    }

    [Fact]
    public async Task CallerCanCancelAStalledBodyAfterHeaders()
    {
        var source = new StalledBody();
        using var http = CreateClient(new StreamContent(source));
        using var destination = new MemoryStream();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var download = ReleaseDownload.CopyAsync(http, Release, destination, TestDeadline, null, cancellation.Token);
        await source.Reading.Task.WaitAsync(TestDeadline, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download.WaitAsync(TestDeadline, TestContext.Current.CancellationToken));
    }

    private static HttpClient CreateClient(HttpContent content) => new(new ResponseHandler(content));

    private sealed class ResponseHandler(HttpContent content) : HttpMessageHandler
    {
        public bool Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                content.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class NonSeekableBody(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class StalledBody : MemoryStream
    {
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanSeek => false;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reading.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
