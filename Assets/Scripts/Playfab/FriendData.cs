// FriendData.cs
[System.Serializable]
public class FriendData
{
    public string playFabId;
    public string displayName;
    public string profileImageUrl;
    public string email;

    public FriendData(string id, string name, string imageUrl = "", string email = "")
    {
        playFabId = id;
        displayName = name;
        profileImageUrl = imageUrl;
        this.email = email;
    }
}