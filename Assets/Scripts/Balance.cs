using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Unity.Collections;

namespace Survivor
{
    [Serializable]
    public class Balance
    {
        public int MaxEnemies;
        public int NumEnemyTypes;
        public int[] EnemyIDs;
        public string[] EnemyName;
        public string[] EnemyPrefabName;
        public NativeArray<float> EnemyVelocity;
        public NativeArray<float> EnemyRadius;

        public Dictionary<string, int> EnemyNameToID;
        public string[] EnemyIDToName;

        public NativeArray<int> SpawnDataID;
        public NativeArray<int> SpawnDataWeight;

        public float SpawnRadius;
        public float PlayerVelocity;
        public float PlayerRadius;
        public float SpawnTime;

        public void LoadBalance()
        {
            TextAsset asset = Resources.Load("balance") as TextAsset;
            LoadBalance(asset.bytes);
        }

        public void LoadBalance(byte[] array)
        {
            Free();

            Stream s = new MemoryStream(array);
            using (BinaryReader br = new BinaryReader(s))
            {
                int version = br.ReadInt32();

                MaxEnemies = br.ReadInt32();
                SpawnRadius = br.ReadSingle();
                PlayerVelocity = br.ReadSingle();
                PlayerRadius = br.ReadSingle();
                SpawnTime = br.ReadSingle();

                int numSpawnData = br.ReadInt32();
                SpawnDataID = new NativeArray<int>(numSpawnData, Allocator.Persistent);
                SpawnDataWeight = new NativeArray<int>(numSpawnData, Allocator.Persistent);
                for (int i = 0; i < numSpawnData; i++)
                {
                    SpawnDataID[i] = br.ReadInt32();
                    SpawnDataWeight[i] = br.ReadInt32();
                }

                NumEnemyTypes = br.ReadInt32();
                EnemyIDs = new int[NumEnemyTypes];
                EnemyName = new string[NumEnemyTypes];
                EnemyPrefabName = new string[NumEnemyTypes];
                EnemyVelocity = new NativeArray<float>(NumEnemyTypes, Allocator.Persistent);
                EnemyRadius = new NativeArray<float>(NumEnemyTypes, Allocator.Persistent);
                EnemyNameToID = new Dictionary<string, int>(NumEnemyTypes);
                EnemyIDToName = new string[NumEnemyTypes];
                for (int enemyIdx = 0; enemyIdx < NumEnemyTypes; enemyIdx++)
                {
                    EnemyIDs[enemyIdx] = br.ReadInt32();
                    EnemyName[enemyIdx] = br.ReadString();
                    EnemyPrefabName[enemyIdx] = br.ReadString();

                    EnemyNameToID.Add(EnemyName[enemyIdx], EnemyIDs[enemyIdx]);
                    EnemyIDToName[EnemyIDs[enemyIdx]] = EnemyName[enemyIdx];

                    EnemyVelocity[enemyIdx] = br.ReadSingle();
                    EnemyRadius[enemyIdx] = br.ReadSingle();
                }

                int magic = br.ReadInt32();
                Debug.Log(magic);
            }
        }

        public void Free()
        {
            if (EnemyVelocity.IsCreated) EnemyVelocity.Dispose();
            if (EnemyRadius.IsCreated) EnemyRadius.Dispose();
            if (SpawnDataID.IsCreated) SpawnDataID.Dispose();
            if (SpawnDataWeight.IsCreated) SpawnDataWeight.Dispose();
        }
    }
}
