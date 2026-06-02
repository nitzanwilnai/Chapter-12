using System;
using UnityEngine;

namespace Survivor
{
    [Serializable]
    public class SpawnData
    {
        public EnemySO EnemySO;
        public int Weight;
    }

    [CreateAssetMenu(fileName = "BalanceSO", menuName = "DOD/BalanceSO", order = 1)]
    public class BalanceSO : ScriptableObject
    {
        public int NumEnemies;
        public float SpawnRadius;
        public float PlayerVelocity;
        public float PlayerRadius;
        public float SpawnTime;

        public SpawnData[] SpawnData;
    }
}