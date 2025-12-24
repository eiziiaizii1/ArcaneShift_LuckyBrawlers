using Unity.Netcode;
using Unity.Collections;
using System;

[Serializable]
public struct PlayerStateData : INetworkSerializable, IEquatable<PlayerStateData>
{
    public ulong ClientId;
    public FixedString32Bytes PlayerName; // Network listeleri için standart string yerine bu kullanılır
    public int Score;
    public bool IsSlimeForm;
    public float CurrentScale;

    // Quest 14: Verinin bit dizisine dönüştürülüp paketlenmesini sağlar
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref PlayerName);
        serializer.SerializeValue(ref Score);
        serializer.SerializeValue(ref IsSlimeForm);
        serializer.SerializeValue(ref CurrentScale);
    }

    // Quest 14: Listenin eleman karşılaştırması yapabilmesi için gereklidir
    public bool Equals(PlayerStateData other)
    {
        return ClientId == other.ClientId && 
               Score == other.Score && 
               IsSlimeForm == other.IsSlimeForm &&
               CurrentScale == other.CurrentScale;
    }
}