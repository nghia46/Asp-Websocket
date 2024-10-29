using System;
using Microsoft.EntityFrameworkCore;

namespace Asp_Websocket;

public class ChatDbContext : DbContext
{
public DbSet<ChatMessage> ChatMessages { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql("Host=localhost; Database=ChatDB; Username=postgres; Password=Abcd1234"); // Thay bằng chuỗi kết nối của bạn
    }
}
