using System;
using System.Collections.Generic;
using System.Xml.Serialization;

/// <summary>
/// Single notification entry.
/// </summary>
[Serializable]
public class NotificationEntry
{
    [XmlAttribute] public string id;
    [XmlAttribute] public string title;
    [XmlAttribute] public string message;
    [XmlAttribute] public string timestamp;
    [XmlAttribute] public bool isRead;

    public NotificationEntry() { }

    public NotificationEntry(string title, string message)
    {
        this.id = Guid.NewGuid().ToString();
        this.title = title;
        this.message = message;
        this.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        this.isRead = false;
    }
}

/// <summary>
/// Root container for XML serialization.
/// </summary>
[Serializable]
[XmlRoot("NotificationHistory")]
public class NotificationHistory
{
    [XmlElement("Notification")]
    public List<NotificationEntry> entries = new List<NotificationEntry>();
}