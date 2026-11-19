namespace Foundation.Ccda;

/// <summary>
/// A read-only, forward-only wrapper that counts bytes read and throws
/// <see cref="CcdaParseException"/> (<see cref="CcdaParseFailure.TooLarge"/>) once the limit is passed,
/// so an oversized stream is rejected without being buffered: at most limit + 1 bytes are ever read
/// from the inner stream. Disposing it leaves the inner stream open.
/// </summary>
internal sealed class BoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;

    internal BoundedReadStream(Stream inner, long limit)
    {
        _inner = inner;
        _limit = limit;
    }

    /// <summary>Total bytes read so far.</summary>
    public long BytesRead { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Count(_inner.Read(buffer, offset, Clamp(count)));
    }

    public override int Read(Span<byte> buffer)
    {
        return Count(_inner.Read(buffer.Slice(0, Clamp(buffer.Length))));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Memory<byte> window = buffer.Slice(0, Clamp(buffer.Length));
        return Count(await _inner.ReadAsync(window, cancellationToken).ConfigureAwait(false));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <summary>Never asks the inner stream for more than one byte past the limit, so overshoot is bounded.</summary>
    private int Clamp(int requested)
    {
        long remaining = _limit - BytesRead + 1;
        return remaining < requested ? (int)Math.Max(remaining, 0) : requested;
    }

    private int Count(int read)
    {
        BytesRead += read;
        if (BytesRead > _limit)
        {
            throw new CcdaParseException(CcdaParseFailure.TooLarge);
        }

        return read;
    }
}
