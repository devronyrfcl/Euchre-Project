using System;
using UnityEngine;

/// <summary>One named bot with its profile picture. Add more entries in the
/// GameplayNetworkManager Inspector — no code changes needed.</summary>
[Serializable]
public class BotProfile
{
    public string name;
    public Sprite avatar;
}
