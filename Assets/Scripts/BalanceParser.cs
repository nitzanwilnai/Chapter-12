using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;


namespace Survivor
{
    public class BalanceParser : MonoBehaviour
    {
#if UNITY_EDITOR
        [MenuItem("DOD/Balance/Parse Local")]
        public static void ParseLocal()
        {
            Debug.Log("Parse balance started!");

            if (!Directory.Exists(Application.persistentDataPath + "/Resources"))
                Directory.CreateDirectory(Application.persistentDataPath + "/Resources");

            assignIDS();

            validate();
            byte[] array = parse();
            // save array
            string path = "Assets/Resources/balance.bytes";
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            using (FileStream fs = File.Create(path))
            using (BinaryWriter bw = new BinaryWriter(fs))
                bw.Write(array);

            Debug.Log("Parse balance finished!");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void assignIDS()
        {
            List<Object> objects = new List<Object>();
            AddObjectsFromDirectory("Assets/Data/Enemies", objects, typeof(EnemySO));
            int numEnemies = objects.Count;
            for (int enemyIdx = 0; enemyIdx < numEnemies; enemyIdx++)
            {
                EnemySO enemySO = (EnemySO)objects[enemyIdx];
                enemySO.ID = enemyIdx;
                EditorUtility.SetDirty(enemySO);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void validate()
        {
            BalanceSO balanceSO = (BalanceSO)AssetDatabase.LoadAssetAtPath("Assets/Data/Balance.asset", typeof(BalanceSO));

            if (balanceSO.NumEnemies == 0)
                Debug.LogError("NumEnemies set to 0!");
        }

static byte[] parse()
{
using (MemoryStream stream = new MemoryStream())
{
    using (BinaryWriter bw = new BinaryWriter(stream))
    {
        int version = 1;
        bw.Write(version);

        BalanceSO balanceSO = (BalanceSO)AssetDatabase.LoadAssetAtPath("Assets/Data/Balance.asset", typeof(BalanceSO));

        bw.Write(balanceSO.NumEnemies);
        bw.Write(balanceSO.SpawnRadius);
        bw.Write(balanceSO.PlayerVelocity);
        bw.Write(balanceSO.PlayerRadius);
        bw.Write(balanceSO.SpawnTime);

        int numSpawnData = balanceSO.SpawnData.Length;
        bw.Write(numSpawnData);
        for (int i = 0; i < numSpawnData; i++)
        {
            bw.Write(balanceSO.SpawnData[i].EnemySO.ID);
            bw.Write(balanceSO.SpawnData[i].Weight);
        }

        List<Object> objects = new List<Object>();
        AddObjectsFromDirectory("Assets/Data/Enemies", objects, typeof(EnemySO));
        int numEnemies = objects.Count;
        Debug.Log("numEnemies " + numEnemies);
        bw.Write(numEnemies);
        for (int enemyIdx = 0; enemyIdx < numEnemies; enemyIdx++)
        {
            EnemySO enemySO = (EnemySO)objects[enemyIdx];
            bw.Write(enemySO.ID);
            bw.Write(enemySO.Name);
            bw.Write(enemySO.Prefab.name);
            bw.Write(enemySO.Velocity);
            bw.Write(enemySO.Radius);
        }

        int magic = 123456789;
        bw.Write(magic);
        return stream.ToArray();
    }
}
        }

        public static void AddObjectsFromDirectory(string path, List<Object> items, System.Type type)
        {
            if (Directory.Exists(path))
            {
                string[] assets = Directory.GetFiles(path);
                foreach (string assetPath in assets)
                    if (assetPath.Contains(".asset") && !assetPath.Contains(".meta"))
                        items.Add(AssetDatabase.LoadAssetAtPath(assetPath, type));

                string[] directories = Directory.GetDirectories(path);
                foreach (string directory in directories)
                    if (Directory.Exists(directory))
                        AddObjectsFromDirectory(directory, items, type);
            }
        }
#endif
    }
}
