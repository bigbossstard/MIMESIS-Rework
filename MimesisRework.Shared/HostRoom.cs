using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mimic.Actors;
using ReluNetwork.ConstEnum;

internal static class ReworkHost
{
    private static readonly FieldInfo PdataField = AccessTools.Field(typeof(Hub), "pdata");
    private static readonly FieldInfo RoomsField = AccessTools.Field(typeof(VRoomManager), "_vrooms");
    internal static bool TryGetLocalHostPlayer(out IVroom room, out VPlayer player)
    {
        room = null; player = null;
        if (Hub.s == null || Hub.Main == null) return false;
        var pdata = PdataField?.GetValue(Hub.s) as Hub.PersistentData;
        if (pdata == null || pdata.ClientMode != NetworkClientMode.Host) return false;
        ProtoActor avatar = Hub.Main.GetMyAvatar();
        if (avatar == null) return false;
        VWorld world = AccessTools.Property(typeof(Hub), "vworld")?.GetValue(Hub.s) as VWorld;
        IDictionary rooms = RoomsField?.GetValue(world?.VRoomManager) as IDictionary;
        if (rooms == null) return false;
        foreach (DictionaryEntry entry in rooms)
            if (entry.Value is IVroom candidate && (player = candidate.FindPlayerByObjectID(avatar.ActorID)) != null) { room = candidate; return true; }
        return false;
    }
}
