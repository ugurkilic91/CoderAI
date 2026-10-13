namespace DevAgents.Application.Common.Exceptions;

/// <summary>The language model server could not be reached or returned an error.</summary>
public class LanguageModelException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>The language model did not finish its answer within the configured time.</summary>
public sealed class LanguageModelTimeoutException(string message) : LanguageModelException(message);
