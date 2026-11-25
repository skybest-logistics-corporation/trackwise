using Trackwise.Infrastructure.AI.Agents;
using Xunit;

namespace Trackwise.Infrastructure.AI.Tests.Agents;

public class AgentSessionCancellationRegistryTests
{
    private readonly AgentSessionCancellationRegistry sut = new();

    [Fact]
    public void Register_ReturnsLinkedToken()
    {
        var sessionId = Guid.NewGuid();
        var token = sut.Register(sessionId, CancellationToken.None);

        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public async Task Register_DeadlineElapses_CancelsToken()
    {
        var token = sut.Register(Guid.NewGuid(), CancellationToken.None, TimeSpan.FromMilliseconds(50));

        Assert.False(token.IsCancellationRequested);

        // The per-request HTTP timeout bounds one call; this bounds the whole session.
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await using var _ = token.Register(() => cancelled.TrySetResult());
        await using var __ = timeoutCts.Token.Register(() => cancelled.TrySetCanceled());

        await cancelled.Task;
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void Register_NoDeadline_LeavesTokenOpen()
    {
        var token = sut.Register(Guid.NewGuid(), CancellationToken.None, deadline: null);

        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public void TryCancel_RegisteredSession_CancelsToken()
    {
        var sessionId = Guid.NewGuid();
        var token = sut.Register(sessionId, CancellationToken.None);

        var result = sut.TryCancel(sessionId);

        Assert.True(result);
        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void TryCancel_UnknownSession_ReturnsFalse()
    {
        var result = sut.TryCancel(Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public void Unregister_RemovesSession()
    {
        var sessionId = Guid.NewGuid();
        sut.Register(sessionId, CancellationToken.None);

        sut.Unregister(sessionId);

        Assert.False(sut.TryCancel(sessionId));
    }

    [Fact]
    public void Register_LinkedToExternalToken_CancelsWhenExternalCancels()
    {
        var cts = new CancellationTokenSource();
        var sessionId = Guid.NewGuid();
        var token = sut.Register(sessionId, cts.Token);

        cts.Cancel();

        Assert.True(token.IsCancellationRequested);
    }
}
