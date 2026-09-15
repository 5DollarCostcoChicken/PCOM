using System;
using UnityEngine;

namespace PCOM
{
    public static class MovementBudget
    {
        public static float CalculateMaximumCost(float movementAmount, int actionPoints)
        {
            ValidateMovementAmount(movementAmount);
            return movementAmount * Mathf.Max(0, actionPoints);
        }

        public static int CalculateRequiredActionPoints(float weightedPathCost, float movementAmount)
        {
            ValidateMovementAmount(movementAmount);
            if (weightedPathCost <= 0f)
            {
                return 0;
            }

            return Mathf.CeilToInt(weightedPathCost / movementAmount);
        }

        private static void ValidateMovementAmount(float movementAmount)
        {
            if (movementAmount <= 0f || float.IsNaN(movementAmount) || float.IsInfinity(movementAmount))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(movementAmount),
                    movementAmount,
                    "Movement amount must be finite and positive.");
            }
        }
    }
}
