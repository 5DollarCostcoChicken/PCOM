namespace PCOM
{
    /// <summary>Color classification for a normalized portion of one executed segment.</summary>
    public readonly struct MovementRouteSection
    {
        public MovementRouteSection(int segmentIndex, float startProgress, float endProgress, bool isHazardous)
        {
            SegmentIndex = segmentIndex;
            StartProgress = startProgress;
            EndProgress = endProgress;
            IsHazardous = isHazardous;
        }

        public int SegmentIndex { get; }
        public float StartProgress { get; }
        public float EndProgress { get; }
        public bool IsHazardous { get; }
    }
}
