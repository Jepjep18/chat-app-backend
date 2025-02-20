using ChatAppBackend.Data;
using ChatAppBackend.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace ChatAppBackend.Hubs
{
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _context;
        private static ConcurrentDictionary<string, string> ConnectedUsers = new(); // Track connected users
        private static ConcurrentQueue<string> WaitingUsers = new(); // Users waiting for a match
        private static ConcurrentDictionary<string, string> ActiveChats = new(); // Store chat pairs

        public ChatHub(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task Connect(string userId)
        {
            ConnectedUsers[Context.ConnectionId] = userId;

            // Check if someone is already waiting
            if (WaitingUsers.TryDequeue(out string? otherUserId))
            {
                // Pair users
                ActiveChats[userId] = otherUserId;
                ActiveChats[otherUserId] = userId;

                // Notify both users they are matched
                await Clients.Client(Context.ConnectionId).SendAsync("Matched", otherUserId);
                await Clients.Client(ConnectedUsers.FirstOrDefault(u => u.Value == otherUserId).Key)
                    .SendAsync("Matched", userId);
            }
            else
            {
                // If no one is waiting, add to queue
                WaitingUsers.Enqueue(userId);
            }
        }

        public async Task SendMessage(string senderId, string content)
        {
            if (!ActiveChats.TryGetValue(senderId, out string receiverId))
            {
                // Instead of silently returning, notify the sender
                await Clients.Caller.SendAsync("Error", "Cannot send message - no active chat found");
                return;
            }

            var message = new Message
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Content = content,
                SentAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            // Send to both users
            await Clients.Client(ConnectedUsers.First(u => u.Value == receiverId).Key)
                .SendAsync("ReceiveMessage", senderId, content, message.SentAt);
            await Clients.Client(Context.ConnectionId)
                .SendAsync("ReceiveMessage", senderId, content, message.SentAt);
        }

        public async Task Disconnect()
        {
            if (ConnectedUsers.TryRemove(Context.ConnectionId, out string? userId))
            {
                // Remove from waiting queue
                var newQueue = new ConcurrentQueue<string>(WaitingUsers.Where(u => u != userId));
                WaitingUsers = newQueue;

                // Remove active chat
                if (ActiveChats.TryRemove(userId, out string? otherUserId))
                {
                    ActiveChats.TryRemove(otherUserId, out _);
                    await Clients.Client(ConnectedUsers.FirstOrDefault(u => u.Value == otherUserId).Key)
                        .SendAsync("PartnerDisconnected");
                }
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await Disconnect();
            await base.OnDisconnectedAsync(exception);
        }
    }
}
