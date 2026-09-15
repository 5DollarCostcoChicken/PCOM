namespace PCOM
{
    public enum GridPathFailureReason
    {
        None = 0,
        DependenciesUnavailable = 1,
        UnitNotInitialized = 2,
        AlreadyMoving = 3,
        StartMissingOrNotWalkable = 4,
        TargetMissing = 5,
        TargetNotWalkable = 6,
        TargetBlocked = 7,
        NoLegalPath = 8,
        ExceedsMovementBudget = 9,
        InsufficientActionPoints = 10,
        StalePath = 11,
        PlayerSelectionNotAccepted = 12
    }
}
