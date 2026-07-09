using EquityLens.Api.Services.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class AgentRunWorkerTests
{
    [Fact]
    public async Task ProcessNextAsync_WhenQueueHasItem_ExecutesAndAcknowledges()
    {
        var message = new AgentRunQueueMessage(Guid.NewGuid(), Guid.NewGuid(), AgentWorkflowTypes.CriticReview, DateTime.UtcNow);
        var queue = new FakeAgentRunQueue(newMessage: new AgentRunQueueItem("stream-1", message));
        var executor = new FakeAgentRunExecutor();
        var worker = CreateWorker(queue, executor);

        var processed = await worker.ProcessNextAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Single(queue.ReadConsumers);
        Assert.Equal(message.RunId, executor.Executions.Single().RunId);
        Assert.Equal(message.UserId, executor.Executions.Single().UserId);
        Assert.Equal(["stream-1"], queue.AcknowledgedStreamIds);
    }

    [Fact]
    public async Task ProcessNextAsync_WhenQueueIsEmpty_ReturnsFalseAndDoesNotExecuteOrAck()
    {
        var queue = new FakeAgentRunQueue();
        var executor = new FakeAgentRunExecutor();
        var worker = CreateWorker(queue, executor);

        var processed = await worker.ProcessNextAsync(CancellationToken.None);

        Assert.False(processed);
        Assert.Single(queue.ReadConsumers);
        Assert.Empty(executor.Executions);
        Assert.Empty(queue.AcknowledgedStreamIds);
    }

    [Fact]
    public async Task ProcessNextAsync_WhenExecutorThrows_DoesNotAcknowledge()
    {
        var message = new AgentRunQueueMessage(Guid.NewGuid(), Guid.NewGuid(), AgentWorkflowTypes.CriticReview, DateTime.UtcNow);
        var queue = new FakeAgentRunQueue(newMessage: new AgentRunQueueItem("stream-2", message));
        var executor = new FakeAgentRunExecutor { ExceptionToThrow = new InvalidOperationException("executor failed") };
        var worker = CreateWorker(queue, executor);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ProcessNextAsync(CancellationToken.None));

        Assert.Equal("executor failed", exception.Message);
        Assert.Single(executor.Executions);
        Assert.Empty(queue.AcknowledgedStreamIds);
    }


    [Fact]
    public async Task ProcessNextAsync_WhenStalePendingExists_ProcessesPendingBeforeNewMessage()
    {
        var pendingMessage = new AgentRunQueueMessage(Guid.NewGuid(), Guid.NewGuid(), AgentWorkflowTypes.CriticReview, DateTime.UtcNow);
        var newMessage = new AgentRunQueueMessage(Guid.NewGuid(), Guid.NewGuid(), AgentWorkflowTypes.DraftRevision, DateTime.UtcNow);
        var queue = new FakeAgentRunQueue(
            stalePendingMessage: new AgentRunQueueItem("pending-1", pendingMessage),
            newMessage: new AgentRunQueueItem("new-1", newMessage));
        var executor = new FakeAgentRunExecutor();
        var worker = CreateWorker(queue, executor);

        var processed = await worker.ProcessNextAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Single(queue.StalePendingReads);
        Assert.Empty(queue.ReadConsumers);
        Assert.Equal(pendingMessage.RunId, executor.Executions.Single().RunId);
        Assert.Equal(["pending-1"], queue.AcknowledgedStreamIds);
    }


    [Fact]
    public async Task ProcessNextAsync_UsesConfiguredPendingMinIdleSeconds()
    {
        var queue = new FakeAgentRunQueue();
        var executor = new FakeAgentRunExecutor();
        var worker = CreateWorker(
            queue,
            executor,
            new AgentRunQueueOptions { PendingMinIdleSeconds = 42 });

        await worker.ProcessNextAsync(CancellationToken.None);

        var read = Assert.Single(queue.StalePendingReads);
        Assert.Equal(TimeSpan.FromSeconds(42), read.MinIdleTime);
    }

    private static AgentRunWorker CreateWorker(
        IAgentRunQueue queue,
        IAgentRunExecutor executor,
        AgentRunQueueOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(queue);
        services.AddSingleton(executor);
        var provider = services.BuildServiceProvider();
        return new AgentRunWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentRunWorker>.Instance,
            Options.Create(options ?? new AgentRunQueueOptions()));
    }

    private sealed class FakeAgentRunQueue : IAgentRunQueue
    {
        private readonly AgentRunQueueItem? _stalePendingMessage;
        private readonly AgentRunQueueItem? _newMessage;

        public FakeAgentRunQueue(AgentRunQueueItem? stalePendingMessage = null, AgentRunQueueItem? newMessage = null)
        {
            _stalePendingMessage = stalePendingMessage;
            _newMessage = newMessage;
        }

        public List<string> ReadConsumers { get; } = [];
        public List<(string ConsumerName, TimeSpan MinIdleTime)> StalePendingReads { get; } = [];
        public List<string> AcknowledgedStreamIds { get; } = [];
        public List<AgentRunQueueMessage> EnqueuedMessages { get; } = [];

        public Task EnqueueAsync(AgentRunQueueMessage message, CancellationToken cancellationToken = default)
        {
            EnqueuedMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task<AgentRunQueueItem?> ReadNextAsync(string consumerName, CancellationToken cancellationToken = default)
        {
            ReadConsumers.Add(consumerName);
            return Task.FromResult(_newMessage);
        }

        public Task<AgentRunQueueItem?> ReadStalePendingAsync(
            string consumerName,
            TimeSpan minIdleTime,
            CancellationToken cancellationToken = default)
        {
            StalePendingReads.Add((consumerName, minIdleTime));
            return Task.FromResult(_stalePendingMessage);
        }

        public Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default)
        {
            AcknowledgedStreamIds.Add(streamId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAgentRunExecutor : IAgentRunExecutor
    {
        public List<(Guid RunId, Guid UserId)> Executions { get; } = [];
        public Exception? ExceptionToThrow { get; set; }

        public Task ExecuteAsync(Guid runId, Guid userId, CancellationToken cancellationToken = default)
        {
            Executions.Add((runId, userId));
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.CompletedTask;
        }
    }
}
