using System.IO;
using UnityEngine;

namespace Survivor
{
public static class MetaDataIO
{
    public static void Save(MetaData metaData)
        {
            if (!Directory.Exists(Application.persistentDataPath + "/DODSurvivor"))
                Directory.CreateDirectory(Application.persistentDataPath + "/DODSurvivor");
        string fileName = Application.persistentDataPath + "/DODSurvivor/metadata.dat";
        using (FileStream fs = File.Create(fileName))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            int version = 1;
            bw.Write(version);
            bw.Write(metaData.BestTime);
            bw.Write((byte)metaData.MenuState);
        }
    }

    public static void Load(MetaData metaData)
    {
        string fileName = Application.persistentDataPath + "/DODSurvivor/metadata.dat";
        if (File.Exists(fileName))
        {
            using (var stream = File.Open(fileName, FileMode.Open))
            using (BinaryReader br = new BinaryReader(stream))
            {
                int verison = br.ReadInt32();
                metaData.BestTime = br.ReadSingle();
                metaData.MenuState = (MENU_STATE)br.ReadByte();
            }
        }
    }
}
}