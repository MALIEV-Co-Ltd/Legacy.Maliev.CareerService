using System.Text;
using Legacy.Maliev.CareerService.Application.Models;
using Legacy.Maliev.CareerService.Tests.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.CareerService.Tests.Serialization;

/// <summary>Checks formatter buffer ownership, drain policy, and decoder-failure classification.</summary>
/// <param name="fixture">The shared Career route fixture providing the application factory, request clients, and test database access.</param>
[Collection("Career shared level")]
public sealed class CareerOriginalBodyFormatterResourceTests(CareerRouteFixture fixture) : IClassFixture<CareerRouteFixture>
{
    /// <summary>Checks stock buffer ownership and original draining behavior on success and failure.</summary>
    /// <param name="candidate">Whether to exercise the registered candidate formatter instead of the original MVC formatter.</param>
    /// <param name="fault">Whether the fragmented request stream throws an I/O failure after 40,000 bytes instead of draining successfully.</param>
    /// <returns>A task that completes when drain behavior, response-owned buffer disposal, temporary-file removal, and request-stream ownership assertions finish.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StockBufferOwnershipAndOriginalDrainPolicy_AreRetained(bool candidate, bool fault)
    {
        var json = "{\"Name\":\"N\",\"Description\":\"D\",\"extra\":\"" + new string('ก', 40000) + "\"}";
        var bytes = Encoding.UTF8.GetBytes(json);
        using var body = new FragmentedInput(bytes, fault ? 40000 : int.MaxValue);
        var completion = new CompletionFeature();
        var http = new DefaultHttpContext();
        http.Features.Set<IHttpResponseFeature>(completion);
        http.Request.Body = body;
        http.Request.ContentType = "application/json";
        http.Request.ContentLength = bytes.LongLength;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        http.RequestAborted = canceled.Token;
        var services = fixture.Factory.Services;
        var formatter = candidate
            ? services.GetRequiredService<IOptions<MvcOptions>>().Value.InputFormatters
                .Single(item => item.GetType().FullName == CareerOriginalBodyNativeOptions.FormatterName)
            : CareerOriginalInputOracle.Formatter();
        var metadata = candidate
            ? services.GetRequiredService<IModelMetadataProvider>().GetMetadataForType(typeof(UpsertJobLevelRequest))
            : new EmptyModelMetadataProvider().GetMetadataForType(typeof(OriginalLevelBody));
        var state = new ModelStateDictionary();
        var context = new InputFormatterContext(http, "", state, metadata,
            static (stream, encoding) => new HttpRequestStreamReader(stream, encoding), false);
        try
        {
            if (fault)
                await Assert.ThrowsAsync<IOException>(() => formatter.ReadAsync(context));
            else
            {
                var result = await formatter.ReadAsync(context);
                Assert.True(result.IsModelSet);
                Assert.False(result.HasError);
            }
            Assert.False(body.CanceledTokenObserved);
            Assert.False(body.DisposedByFormatter);
            var buffer = Assert.Single(completion.Buffers);
            Assert.False(buffer.InMemory);
            var ownedPath = buffer.TempFileName;
            Assert.NotNull(ownedPath);
            await completion.CompleteAsync();
            Assert.False(buffer.CanRead);
            Assert.False(File.Exists(ownedPath));
            Assert.False(body.DisposedByFormatter);
        }
        finally
        {
            // Drain failure happens before the formatter's reader-finally block.
            // The response completion callback still owns and releases its buffer.
            await completion.CompleteAsync();
        }
    }

    /// <summary>Checks that decoder faults are not silently classified as malformed JSON.</summary>
    /// <returns>A task that completes when source and candidate readers both preserve the decoder fallback exception classification.</returns>
    [Fact]
    public async Task DecoderFault_IsNotSilentlyReclassifiedAsMalformedJson()
    {
        var bytes = new byte[] { 0xff };
        var source = await CareerOriginalInputOracle.ReadBytesAsync(CareerOriginalInputOracle.Formatter(),
            typeof(OriginalLevelBody), new EmptyModelMetadataProvider(), bytes, "application/json");
        Assert.Equal("throw:System.Text.DecoderFallbackException", source.Kind);
        var services = fixture.Factory.Services;
        var candidate = await CareerOriginalBodyNativeOptions.ReadAsync(services,
            services.GetRequiredService<IOptions<MvcOptions>>().Value, true, bytes);
        Assert.Equal(source.Kind, candidate.Kind);
    }

    private sealed class CompletionFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> callbacks = [];
        internal FileBufferingReadStream[] Buffers => callbacks.Select(item => item.State).OfType<FileBufferingReadStream>().ToArray();

        public override void OnCompleted(Func<object, Task> callback, object state) => callbacks.Add((callback, state));

        internal async Task CompleteAsync()
        {
            var pending = callbacks.ToArray();
            callbacks.Clear();
            foreach (var item in pending.Reverse()) await item.Callback(item.State);
        }
    }

    private sealed class FragmentedInput(byte[] bytes, int throwAfter) : Stream
    {
        private readonly MemoryStream input = new(bytes, writable: false);
        internal bool DisposedByFormatter { get; private set; }
        internal bool CanceledTokenObserved { get; private set; }
        public override bool CanRead => !DisposedByFormatter;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (input.Position >= throwAfter) throw new IOException("private-stream-marker");
            return input.Read(buffer, offset, Math.Min(count, 1024));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            CanceledTokenObserved |= cancellationToken.CanBeCanceled;
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            CanceledTokenObserved |= cancellationToken.CanBeCanceled;
            if (input.Position >= throwAfter) throw new IOException("private-stream-marker");
            return ValueTask.FromResult(input.Read(buffer.Span[..Math.Min(buffer.Length, 1024)]));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposedByFormatter = true;
                input.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
