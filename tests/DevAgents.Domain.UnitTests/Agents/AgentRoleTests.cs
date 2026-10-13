using DevAgents.Domain.Agents;

namespace DevAgents.Domain.UnitTests.Agents;

public class AgentRoleTests
{
    [TestCase(AgentRole.Router)]
    [TestCase(AgentRole.BackTranslator)]
    public void SettingsFallback_LanguageTasks_UseTheTranslatorSettings(AgentRole role)
    {
        Assert.That(role.SettingsFallback(), Is.EqualTo(AgentRole.Translator));
    }

    [TestCase(AgentRole.Translator)]
    [TestCase(AgentRole.Analyzer)]
    [TestCase(AgentRole.Coder)]
    [TestCase(AgentRole.Tester)]
    [TestCase(AgentRole.Reviewer)]
    public void SettingsFallback_OtherAgents_HaveNone(AgentRole role)
    {
        Assert.That(role.SettingsFallback(), Is.Null);
    }
}
