namespace DevPulse.Domain.Enums;

/// <summary>Zmapowany <c>statusCheckRollup.state</c> ostatniego commita na default branch.</summary>
public enum CiStatus
{
    None,
    Success,
    Failure,
    Pending,
}
