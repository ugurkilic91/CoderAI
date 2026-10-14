using DevAgents.Application.Agents;
using DevAgents.Application.Agents.Commands.RunAgentPipeline;
using DevAgents.Application.Common.Behaviours;
using DevAgents.Application.UnitTests.Fakes;
using NUnit.Framework;
using Shouldly;
using ValidationException = DevAgents.Application.Common.Exceptions.ValidationException;

namespace DevAgents.Application.UnitTests.Common.Behaviours;

public class StreamValidationBehaviourTests
{
    [Test]
    public async Task ShouldFailBeforeTheHandlerProducesAnything()
    {
        var behaviour = new StreamValidationBehaviour<RunAgentPipelineCommand, PipelineEvent>(
            [new RunAgentPipelineCommandValidator(new FakeWorkspace(), new FakeArchitectureCatalog())]);
        var handlerStarted = false;

        var exception = await Should.ThrowAsync<ValidationException>(async () =>
        {
            await foreach (var _ in behaviour.Handle(new RunAgentPipelineCommand { Question = "" }, Handler, CancellationToken.None))
            {
            }
        });

        exception.Message.ShouldBe("Soru boş olamaz.");
        exception.Errors["Question"].ShouldBe(["Soru boş olamaz."]);
        handlerStarted.ShouldBeFalse();

        async IAsyncEnumerable<PipelineEvent> Handler()
        {
            handlerStarted = true;
            await Task.Yield();
            yield return PipelineEvent.Failed("should not run");
        }
    }

    [Test]
    public async Task ShouldPassEventsThroughForAValidRequest()
    {
        var behaviour = new StreamValidationBehaviour<RunAgentPipelineCommand, PipelineEvent>(
            [new RunAgentPipelineCommandValidator(new FakeWorkspace(), new FakeArchitectureCatalog())]);
        var events = new List<PipelineEvent>();

        await foreach (var pipelineEvent in behaviour.Handle(new RunAgentPipelineCommand { Question = "x" }, Handler, CancellationToken.None))
            events.Add(pipelineEvent);

        events.Select(e => e.Type).ShouldBe(["token", "token"]);

        static async IAsyncEnumerable<PipelineEvent> Handler()
        {
            await Task.Yield();
            yield return PipelineEvent.Token("answer", "a");
            yield return PipelineEvent.Token("answer", "b");
        }
    }
}
