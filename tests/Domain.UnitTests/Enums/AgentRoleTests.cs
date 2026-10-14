using DevAgents.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Domain.UnitTests.Enums;

public class AgentRoleTests
{
    [TestCase(AgentRole.Router)]
    [TestCase(AgentRole.BackTranslator)]
    public void LanguageTasksShouldFallBackToTheTranslatorSettings(AgentRole role)
    {
        role.SettingsFallback().ShouldBe(AgentRole.Translator);
    }

    [TestCase(AgentRole.Translator)]
    [TestCase(AgentRole.Analyzer)]
    [TestCase(AgentRole.Coder)]
    [TestCase(AgentRole.Tester)]
    [TestCase(AgentRole.Reviewer)]
    public void OtherAgentsShouldHaveNoFallback(AgentRole role)
    {
        role.SettingsFallback().ShouldBeNull();
    }
}
