namespace Jellyfin.Plugin.Prdb.Api.Controllers;

/// <summary>
/// The outcome of a connection test.
/// </summary>
/// <param name="Outcome">One of <c>ok</c>, <c>missingKey</c>, <c>invalidUrl</c>, <c>unauthorized</c> or <c>unreachable</c>.</param>
/// <param name="Message">A sentence for the administrator.</param>
/// <param name="HourlyRemaining">Requests left in the hourly window, when the test succeeded.</param>
public sealed record ConnectionTestResult(string Outcome, string Message, int? HourlyRemaining);
