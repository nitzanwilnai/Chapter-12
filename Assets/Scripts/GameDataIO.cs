using System;
using System.IO;
using UnityEngine;
using Unity.Mathematics;

namespace Survivor
{
    public static class GameDataIO
    {
        public static void Save(GameData gameData, Balance balance)
        {
            Debug.LogFormat("SaveGame()");

            if (!Directory.Exists(Application.persistentDataPath + "/DODSurvivor"))
                Directory.CreateDirectory(Application.persistentDataPath + "/DODSurvivor");

            string fileName = Application.persistentDataPath + "/DODSurvivor/gamedata.dat";
            using (FileStream fs = File.Create(fileName))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                int version = 2;
                bw.Write(version);

                bw.Write(gameData.InGame);

                bw.Write(gameData.AliveEnemyCount);
                for (int i = 0; i < gameData.AliveEnemyCount; i++)
                    bw.Write(gameData.AliveEnemyIndices[i]);

                bw.Write(gameData.DeadEnemyCount);
                for (int i = 0; i < gameData.DeadEnemyCount; i++)
                    bw.Write(gameData.DeadEnemyIndices[i]);

                bw.Write(balance.MaxEnemies);

                for (int i = 0; i < balance.MaxEnemies; i++)
                {
                    bw.Write(gameData.EnemyPosition[i].x);
                    bw.Write(gameData.EnemyPosition[i].y);
                }

                for (int i = 0; i < balance.MaxEnemies; i++)
                    bw.Write(gameData.EnemyType[i]);

                bw.Write(gameData.PlayerDirection.x);
                bw.Write(gameData.PlayerDirection.y);

                bw.Write(gameData.GameTime);

                bw.Write(balance.NumEnemyTypes);
                for (int enemyType = 0; enemyType < balance.NumEnemyTypes; enemyType++)
                    bw.Write(balance.EnemyIDToName[enemyType]);

                bw.Write(gameData.Rng.state);
            }
        }

        public static void Load(GameData gameData, Balance balance)
        {
            string fileName = Application.persistentDataPath + "/DODSurvivor/gamedata.dat";
            if (File.Exists(fileName))
            {
                using (FileStream stream = File.Open(fileName, FileMode.Open))
                using (BinaryReader br = new BinaryReader(stream))
                {
                    int version = br.ReadInt32();

                    gameData.InGame = br.ReadBoolean();

                    gameData.AliveEnemyCount = br.ReadInt32();
                    for (int i = 0; i < gameData.AliveEnemyCount; i++)
                        gameData.AliveEnemyIndices[i] = br.ReadInt32();

                    gameData.DeadEnemyCount = br.ReadInt32();
                    for (int i = 0; i < gameData.DeadEnemyCount; i++)
                        gameData.DeadEnemyIndices[i] = br.ReadInt32();

                    int numEnemies = br.ReadInt32();
                    for (int i = 0; i < numEnemies; i++)
                    {
                        float x = br.ReadSingle();
                        float y = br.ReadSingle();
                        gameData.EnemyPosition[i] = new float2(x, y);
                    }

                    for (int i = 0; i < numEnemies; i++)
                        gameData.EnemyType[i] = br.ReadInt32();

                    float px = br.ReadSingle();
                    float py = br.ReadSingle();
                    gameData.PlayerDirection = new float2(px, py);

                    gameData.GameTime = br.ReadSingle();

                    int numEnemyTypes = br.ReadInt32();
                    for (int enemyType = 0; enemyType < numEnemyTypes; enemyType++)
                    {
                        string enemyIdentifier = br.ReadString();
                        int newType = balance.EnemyNameToID[enemyIdentifier];
                        if (newType != enemyType)
                        {
                            for (int i = 0; i < numEnemies; i++)
                            {
                                if (gameData.EnemyType[i] == enemyType)
                                {
                                    gameData.EnemyType[i] = newType;
                                }
                            }
                        }
                    }

                    if (version >= 2)
                    {
                        uint rngState = br.ReadUInt32();
                        gameData.Rng = new Unity.Mathematics.Random { state = rngState };
                    }
                    else
                    {
                        gameData.Rng = new Unity.Mathematics.Random((uint)System.DateTime.Now.Ticks | 1u);
                    }
                }
            }
        }

        public static bool SaveGameExists()
        {
            bool inGame = false;
            string fileName = Application.persistentDataPath + "/DODSurvivor/gamedata.dat";
            if (File.Exists(fileName))
            {
                using (FileStream stream = File.Open(fileName, FileMode.Open))
                using (BinaryReader br = new BinaryReader(stream))
                {
                    int version = br.ReadInt32();

                    inGame = br.ReadBoolean();
                }
            }
            return inGame;
        }
    }
}
