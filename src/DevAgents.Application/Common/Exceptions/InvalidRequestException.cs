namespace DevAgents.Application.Common.Exceptions;

/// <summary>The request cannot be processed as sent (empty question, unusable project path, ...).</summary>
public class InvalidRequestException(string message) : Exception(message);
