using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Asp_Websocket;

public class WebSocketHandler
{
    private static ConcurrentDictionary<string, List<WebSocket>> _sessions = new ConcurrentDictionary<string, List<WebSocket>>();

    public static async Task HandleChatSessionAsync(HttpContext context, WebSocket webSocket)
    {

        // Lấy userId và partnerId từ query parameters
        var userId = context.Request.Query["userId"].ToString();
        var partnerId = context.Request.Query["partnerId"].ToString();

        // Kiểm tra nếu thiếu userId hoặc partnerId
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(partnerId))
        {
            await SendErrorMessageAndClose(webSocket, "Thiếu thông tin userId hoặc partnerId");
            return;
        }

        // Kiểm tra nếu userId và partnerId giống nhau
        if (userId == partnerId)
        {
            await SendErrorMessageAndClose(webSocket, "id người gửi và người nhận không thể giống nhau");
            return;
        }

        // Nếu tất cả kiểm tra hợp lệ, tiến hành tạo session WebSocket
        // (Thực hiện các bước khác trong quá trình thiết lập kết nối WebSocket của bạn ở đây)

        var sessionId = GenerateSessionId(userId, partnerId);
        _sessions.AddOrUpdate(sessionId, new List<WebSocket> { webSocket }, (key, existingList) =>
        {
            existingList.Add(webSocket);
            return existingList;
        });

        // Fetch tin nhắn cũ từ database khi user kết nối lại
        using (var dbContext = new ChatDbContext())
        {
            var previousMessages = dbContext.ChatMessages
                .Where(m => m.SessionId == sessionId)
                .OrderBy(m => m.Timestamp)
                .ToList();

            foreach (var msg in previousMessages)
            {
                var encodedMessage = Encoding.UTF8.GetBytes(msg.Content);
                await webSocket.SendAsync(new ArraySegment<byte>(encodedMessage, 0, encodedMessage.Length), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        var buffer = new byte[1024 * 4];
        WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

        while (!result.CloseStatus.HasValue)
        {
            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var content = ParseMessageContent(message);

            // Lưu tin nhắn mới vào database
            using (var dbContext = new ChatDbContext())
            {
                var newMessage = new ChatMessage
                {
                    SenderId = userId,
                    RecipientId = partnerId,
                    Content = content,
                    Timestamp = DateTime.UtcNow,
                    SessionId = sessionId
                };
                dbContext.ChatMessages.Add(newMessage);
                await dbContext.SaveChangesAsync();
            }

            // Gửi tin nhắn đến tất cả các kết nối trong session này (ngoại trừ người gửi)
            foreach (var socket in _sessions[sessionId])
            {
                if (socket != webSocket && socket.State == WebSocketState.Open)
                {
                    var encodedContent = Encoding.UTF8.GetBytes(content);
                    await socket.SendAsync(new ArraySegment<byte>(encodedContent, 0, encodedContent.Length), result.MessageType, result.EndOfMessage, CancellationToken.None);
                }
            }

            result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        }

        if (_sessions.TryGetValue(sessionId, out var webSocketList))
        {
            webSocketList.Remove(webSocket);
            if (webSocketList.Count == 0)
            {
                _sessions.TryRemove(sessionId, out _);
            }
        }
        await webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
    }

    // Phương thức để gửi thông báo lỗi và đóng kết nối
    private static async Task SendErrorMessageAndClose(WebSocket webSocket, string errorMessage)
    {
        // Gửi tin nhắn lỗi đến client
        var errorBuffer = Encoding.UTF8.GetBytes(errorMessage);
        await webSocket.SendAsync(new ArraySegment<byte>(errorBuffer), WebSocketMessageType.Text, true, CancellationToken.None);

        // Đóng kết nối với mã lỗi
        await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, errorMessage, CancellationToken.None);
    }
    private static string GenerateSessionId(string userId, string partnerId)
    {
        return string.Compare(userId, partnerId) < 0 ? $"{userId}-{partnerId}" : $"{partnerId}-{userId}";
    }

    private static string ParseMessageContent(string? message)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(message);
            return jsonDoc.RootElement.GetProperty("content").GetString();
        }
        catch
        {
            return null;
        }
    }
}
