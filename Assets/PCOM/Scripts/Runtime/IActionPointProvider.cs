namespace PCOM
{
    /// <summary>
    /// Replaceable boundary between movement commitment and character-owned action points.
    /// </summary>
    public interface IActionPointProvider
    {
        int CurrentActionPoints { get; }

        bool TrySpendActionPoints(int amount);
    }
}
