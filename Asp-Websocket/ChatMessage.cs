using System;

namespace Asp_Websocket;

public class ChatMessage
{
    public int Id { get; set; }
    public string SenderId { get; set; }
    public string RecipientId { get; set; }
    public string Content { get; set; }
    public DateTime Timestamp { get; set; }
    public string SessionId { get; set; }  // Để xác định session của cặp người dùng
}
