using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace PCOM
{
    /// <summary>
    /// Immutable bounded Dijkstra result used for movement-range presentation.
    /// Costs are physical movement costs used for AP-band assignment. Hazard avoidance
    /// preference is deliberately excluded because it never consumes movement budget.
    /// </summary>
    public sealed class MovementReachabilityResult
    {
        private readonly ReadOnlyDictionary<Vector3Int, float> costs;

        public MovementReachabilityResult(
            IDictionary<Vector3Int, float> sourceCosts,
            float movementAmount,
            int maximumActionPointBands)
        {
            if (sourceCosts == null)
            {
                throw new ArgumentNullException(nameof(sourceCosts));
            }

            if (movementAmount <= 0f || float.IsNaN(movementAmount) || float.IsInfinity(movementAmount))
            {
                throw new ArgumentOutOfRangeException(nameof(movementAmount));
            }

            costs = new ReadOnlyDictionary<Vector3Int, float>(
                new Dictionary<Vector3Int, float>(sourceCosts));
            MovementAmount = movementAmount;
            MaximumActionPointBands = Mathf.Clamp(maximumActionPointBands, 0, 3);
        }

        public IReadOnlyDictionary<Vector3Int, float> Costs => costs;
        public float MovementAmount { get; }
        public int MaximumActionPointBands { get; }

        public bool TryGetCost(Vector3Int coordinate, out float weightedCost)
        {
            return costs.TryGetValue(coordinate, out weightedCost);
        }

        public bool TryGetActionPointBand(Vector3Int coordinate, out int actionPointBand)
        {
            actionPointBand = 0;
            if (!costs.TryGetValue(coordinate, out float weightedCost))
            {
                return false;
            }

            actionPointBand = MovementBudget.CalculateRequiredActionPoints(weightedCost, MovementAmount);
            // The origin is useful to callers but never belongs to a visible AP band.
            return actionPointBand > 0 && actionPointBand <= MaximumActionPointBands;
        }
    }
}
